using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using RemoteDebuggerVSX.Debugging;
using RemoteDebuggerVSX.Interop;
using RemoteDebuggerVSX.Utilities;

namespace RemoteDebuggerVSX.Breakpoint
{
    internal sealed class BreakpointForwarder : IDisposable
    {
        static public readonly string EngineEventTransportDisconnected = "transport_disconnected";
        static public readonly string EngineCommandBreakpointChanged = "breakpoint_changed";

        private readonly AsyncPackage _package;
        private readonly RemoteCommandBroker _broker;
        private readonly RemoteSessionService _session;

        private DTE2 _dte;
        private DebuggerEvents _debuggerEvents;

        private readonly HashSet<BreakpointKey> _issuedBreakpoints = new HashSet<BreakpointKey>();
        private readonly Dictionary<BreakpointKey, bool> _pendingSyncs = new Dictionary<BreakpointKey, bool>();
        private readonly Dictionary<BreakpointKey, string> _lastSentPayloads = new Dictionary<BreakpointKey, string>();
        private readonly Dictionary<BreakpointKey, bool?> _engineEnabledOverrides = new Dictionary<BreakpointKey, bool?>();

        private bool _isProcessingSyncs;
        private int _setBreakpointCount;
        private bool _readyCommandSent;

        public BreakpointForwarder(AsyncPackage package, RemoteSessionService session)
        {
            _package = package;
            _session = session ?? throw new ArgumentNullException(nameof(session));

            _broker = new RemoteCommandBroker(package);
            _broker.EngineCommandParsed += OnEngineCommandParsed;
            _broker.RemoteConnectionFailed += OnRemoteConnectionFailed;
            _broker.TransportConnected += OnTransportConnected;
        }

        private void OnTransportConnected()
        {
            _ = _package.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

                _session.SetTransportReady(true);
                ProcessPendingSyncs();
            });
        }

        private void OnRemoteConnectionFailed(string message)
        {
            FireAndForget.Run(_package, async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

                var hadTransport = _session.TransportReady;
                DeactivateRemoteDebugging();

                if (!hadTransport && !IsGracefulTransportDisconnect(message))
                {
                    var errorText = string.IsNullOrWhiteSpace(message)
                        ? "Failed to connect to remote debugger."
                        : $"Failed to connect to remote debugger: {message}";

                    try
                    {
                        VsShellUtilities.ShowMessageBox(
                            _package,
                            errorText,
                            "Remote Debugger",
                            OLEMSGICON.OLEMSGICON_CRITICAL,
                            OLEMSGBUTTON.OLEMSGBUTTON_OK,
                            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    }
                    catch
                    {
                    }
                }

                if (_dte != null)
                {
                    try
                    {
                        _dte.Debugger.Stop(true);
                    }
                    catch
                    {
                        try
                        {
                            _dte.ExecuteCommand("Debug.StopDebugging");
                        }
                        catch
                        {
                        }
                    }
                }
            }, category: "Transport");
        }

        private void OnEngineCommandParsed(string command, Dictionary<string, object> payload)
        {
            if (!ThreadHelper.CheckAccess())
            {
                FireAndForget.Run(_package, async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
                    OnEngineCommandParsed(command, payload);
                }, category: "Threading");
                return;
            }

            if (!_session.RemoteDebuggingActive || !_session.TransportReady)
            {
                return;
            }

            if (IsTransportDisconnectedEvent(payload))
            {
                _ = _package.JoinableTaskFactory.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
                    DeactivateRemoteDebugging();
                });

                return;
            }

            if (string.Equals(command, EngineCommandBreakpointChanged, StringComparison.OrdinalIgnoreCase)
                && TryParseBreakpointChange(payload, out var change))
            {
                _ = _package.JoinableTaskFactory.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

                    if (_dte == null)
                    {
                        return;
                    }

                    if (change.ShouldRemove)
                    {
                        _engineEnabledOverrides.Remove(change.Key);
                        QueueSync(change.Key);
                        return;
                    }

                    if (change.ShouldForceResend)
                    {
                        _issuedBreakpoints.Remove(change.Key);
                        _lastSentPayloads.Remove(change.Key);
                        if (change.EngineEnabled.HasValue)
                        {
                            _engineEnabledOverrides[change.Key] = change.EngineEnabled.Value;
                        }
                        QueueSync(change.Key, forceResend: true);
                        return;
                    }

                    if (change.EngineEnabled.HasValue)
                    {
                        _engineEnabledOverrides[change.Key] = change.EngineEnabled.Value;
                    }
                    QueueSync(change.Key);
                });
            }
            else if (string.Equals(command, Protocol.Command.GetThreads, StringComparison.OrdinalIgnoreCase))
            {
                if (!_readyCommandSent && GetCurrentBreakpointCount() == 0)
                {
                    TrySendReadyCommand();
                }
            }
        }

        private static bool IsTransportDisconnectedEvent(Dictionary<string, object> payload)
        {
            if (payload == null)
            {
                return false;
            }

            if (!payload.TryGetValue("type", out var typeObj) || typeObj == null)
            {
                return false;
            }

            if (!string.Equals(typeObj.ToString(), Protocol.Type.Event, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!payload.TryGetValue("event", out var evtObj) || evtObj == null)
            {
                return false;
            }

            return string.Equals(evtObj.ToString(), EngineEventTransportDisconnected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseBreakpointChange(Dictionary<string, object> payload, out BreakpointChange change)
        {
            change = default;

            if (payload == null)
            {
                return false;
            }

            if (!payload.TryGetValue("file", out var fileObj) || fileObj == null)
            {
                return false;
            }

            if (!payload.TryGetValue("line", out var lineObj) || lineObj == null)
            {
                return false;
            }

            var line = Convert.ToInt32(lineObj, System.Globalization.CultureInfo.InvariantCulture);
            var key = new BreakpointKey(fileObj.ToString(), line);
            if (!key.IsValid)
            {
                return false;
            }

            payload.TryGetValue("changeType", out var changeTypeObj);
            var changeType = changeTypeObj?.ToString() ?? string.Empty;

            bool? enabledFromPayload = null;
            if (payload.TryGetValue("enabled", out var enabledObj) && enabledObj != null)
            {
                if (enabledObj is bool b)
                {
                    enabledFromPayload = b;
                }
                else if (bool.TryParse(enabledObj.ToString(), out var parsed))
                {
                    enabledFromPayload = parsed;
                }
            }

            change = new BreakpointChange(
                key,
                ShouldRemoveChange(changeType),
                ShouldForceResendChange(changeType),
                enabledFromPayload);

            return true;
        }

        private static bool ShouldRemoveChange(string type)
        {
            return string.Equals(type, "removed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "deleted", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldForceResendChange(string type)
        {
            return string.Equals(type, "enabled_changed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "condition_changed", StringComparison.OrdinalIgnoreCase);
        }

        private readonly struct BreakpointChange
        {
            internal BreakpointChange(BreakpointKey key, bool shouldRemove, bool shouldForceResend, bool? engineEnabled)
            {
                Key = key;
                ShouldRemove = shouldRemove;
                ShouldForceResend = shouldForceResend;
                EngineEnabled = engineEnabled;
            }

            internal BreakpointKey Key { get; }
            internal bool ShouldRemove { get; }
            internal bool ShouldForceResend { get; }
            internal bool? EngineEnabled { get; }
        }

        private void OnEnterDesignMode(dbgEventReason Reason)
        {
            if (!ThreadHelper.CheckAccess())
            {
                FireAndForget.Run(_package, async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
                    OnEnterDesignMode(Reason);
                }, category: "Threading");
                return;
            }

            DeactivateRemoteDebugging();
        }

        private static Dictionary<string, object> MapBreakpoint(EnvDTE.Breakpoint bp, bool? enabledOverride)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string file;
            try
            {
                file = bp.File;
            }
            catch
            {
                file = string.Empty;
            }

            int line;
            try
            {
                line = bp.FileLine;
            }
            catch
            {
                line = 0;
            }

            var normalized = new BreakpointKey(file ?? string.Empty, line);
            file = normalized.File;
            line = normalized.Line;

            bool enabled;
            if (enabledOverride.HasValue)
            {
                enabled = enabledOverride.Value;
            }
            else
            {
                try
                {
                    enabled = bp.Enabled;
                }
                catch
                {
                    enabled = true;
                }
            }

            string condition;
            try
            {
                condition = bp.Condition;
            }
            catch
            {
                condition = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(condition))
            {
                condition = string.Empty;
            }

            string conditionType;
            try
            {
                switch (bp.ConditionType)
                {
                    case dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenTrue:
                    case dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenChanged:
                        conditionType = string.IsNullOrEmpty(condition) ? "none" : "expression";
                        break;
                    default:
                        conditionType = "none";
                        break;
                }
            }
            catch
            {
                conditionType = "none";
            }

            if (string.IsNullOrEmpty(condition))
            {
                conditionType = "none";
            }

            string function;
            try
            {
                function = bp.FunctionName ?? string.Empty;
            }
            catch
            {
                function = string.Empty;
            }

            int functionLineOffset;
            try
            {
                functionLineOffset = bp.FunctionLineOffset;
            }
            catch
            {
                functionLineOffset = 0;
            }

            return new Dictionary<string, object>
            {
                { "file", file },
                { "line", line },
                { "function", function ?? string.Empty },
                { "functionLineOffset", functionLineOffset },
                { "conditionType", conditionType ?? "none" },
                { "condition", condition ?? string.Empty },
                { "enabled", enabled }
            };
        }

        private readonly struct BreakpointKey : IEquatable<BreakpointKey>
        {
            public BreakpointKey(string file, int line)
            {
                File = NormalizePath(file ?? string.Empty);
                Line = line > 0 ? line : 0;
            }

            public string File { get; }
            public int Line { get; }

            public bool IsValid => !string.IsNullOrEmpty(File) && Line > 0;

            public bool Equals(BreakpointKey other)
            {
                return Line == other.Line && string.Equals(File, other.File, StringComparison.OrdinalIgnoreCase);
            }

            public override bool Equals(object obj)
            {
                return obj is BreakpointKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(File ?? string.Empty);
                return (hash * 397) ^ Line;
            }

            public override string ToString()
            {
                return string.Concat(File, ":", Line);
            }

            private static string NormalizePath(string file)
            {
                if (string.IsNullOrWhiteSpace(file))
                {
                    return string.Empty;
                }

                var normalized = file.Replace('/', '\\').Trim();

                try
                {
                    if (LooksLikeWindowsPath(normalized))
                    {
                        normalized = Path.GetFullPath(normalized);
                    }
                }
                catch
                {
                }

                return normalized;
            }

            private static bool LooksLikeWindowsPath(string path)
            {
                if (string.IsNullOrEmpty(path))
                {
                    return false;
                }

                if (path.StartsWith("\\\\", StringComparison.Ordinal))
                {
                    return true;
                }

                return path.Length > 1
                    && path[1] == ':'
                    && char.IsLetter(path[0]);
            }
        }

        private static bool IsGracefulTransportDisconnect(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            return string.Equals(message.Trim(), "Transport disconnected.", StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputePayloadSignature(Dictionary<string, object> payload)
        {
            if (payload == null || payload.Count == 0)
            {
                return string.Empty;
            }

            var keys = payload.Keys.OrderBy(k => k, StringComparer.Ordinal);
            var builder = new StringBuilder();

            foreach (var key in keys)
            {
                builder.Append(key);
                builder.Append('=');
                builder.Append(payload[key]?.ToString() ?? string.Empty);
                builder.Append(';');
            }

            return builder.ToString();
        }

        private void ResetSessionCaches()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _issuedBreakpoints.Clear();
            _pendingSyncs.Clear();
            _lastSentPayloads.Clear();
            _engineEnabledOverrides.Clear();
            _setBreakpointCount = 0;
            _readyCommandSent = false;
        }

        private void DeactivateRemoteDebugging()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            ResetSessionCaches();
            _session.Deactivate();
            _broker.ShutdownTransport();
        }

        public async System.Threading.Tasks.Task InitializeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

            var dteObj = await _package.GetServiceAsync(typeof(EnvDTE.DTE));
            if (dteObj == null)
            {
                return;
            }

            _dte = dteObj as DTE2;
            if (_dte == null)
            {
                return;
            }

            ResetSessionCaches();

            _debuggerEvents = _dte.Events.DebuggerEvents;
            _debuggerEvents.OnEnterDesignMode += OnEnterDesignMode;
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_debuggerEvents != null)
            {
                _debuggerEvents.OnEnterDesignMode -= OnEnterDesignMode;
            }

            ResetSessionCaches();

            _session.Deactivate();
            _broker.EngineCommandParsed -= OnEngineCommandParsed;
            _broker.RemoteConnectionFailed -= OnRemoteConnectionFailed;
            _broker.TransportConnected -= OnTransportConnected;
            _broker.Dispose();
        }

        public bool SetRemoteDebuggingActive(bool active)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (active)
            {
                if (_session.RemoteDebuggingActive)
                {
                    return true;
                }

                if (!_broker.Start())
                {
                    _broker.ShutdownTransport();
                    return false;
                }

                _session.Activate();
                ResetSessionCaches();
                return true;
            }

            DeactivateRemoteDebugging();
            return true;
        }

        private void QueueSync(BreakpointKey key, bool forceResend = false)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!key.IsValid)
            {
                return;
            }

            var alreadyForced = _pendingSyncs.TryGetValue(key, out var existingForce) && existingForce;
            _pendingSyncs[key] = forceResend || alreadyForced;
            ProcessPendingSyncs();
        }

        private void ProcessPendingSyncs()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_isProcessingSyncs)
            {
                return;
            }

            try
            {
                _isProcessingSyncs = true;

                while (_pendingSyncs.Count > 0)
                {
                    if (!_session.RemoteDebuggingActive || !_session.TransportReady || _dte == null)
                    {
                        break;
                    }

                    var next = _pendingSyncs.First();
                    _pendingSyncs.Remove(next.Key);
                    SyncBreakpoint(next.Key, next.Value);
                }
            }
            finally
            {
                _isProcessingSyncs = false;
            }
        }

        private void SyncBreakpoint(BreakpointKey key, bool forceResend)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!_session.RemoteDebuggingActive || !_session.TransportReady || _dte == null)
            {
                _pendingSyncs[key] = forceResend || (_pendingSyncs.TryGetValue(key, out var existingForce) && existingForce);
                return;
            }

            var hasBreakpoint = TryFindBreakpoint(key, out var bp);

            if (hasBreakpoint)
            {
                var payload = MapBreakpoint(bp, GetEngineEnabledOverride(key));
                payload["file"] = key.File;
                payload["line"] = key.Line;

                var signature = ComputePayloadSignature(payload);
                if (!forceResend
                    && _lastSentPayloads.TryGetValue(key, out var lastSignature)
                    && string.Equals(lastSignature, signature, StringComparison.Ordinal))
                {
                    return;
                }

                _broker.SendToRemote(Protocol.Command.SetBreakpoint, payload);
                _issuedBreakpoints.Add(key);
                _lastSentPayloads[key] = signature;
                InitializeBreakpointSendCompletionCheck();
            }
            else if (_issuedBreakpoints.Remove(key))
            {
                _engineEnabledOverrides.Remove(key);
                SendRemoveBreakpoint(key);
            }
        }

        private bool TryFindBreakpoint(BreakpointKey key, out EnvDTE.Breakpoint match)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            match = null;

            if (_dte == null)
            {
                return false;
            }

            var bps = _dte.Debugger.Breakpoints;
            if (bps == null)
            {
                return false;
            }

            foreach (EnvDTE.Breakpoint bp in bps)
            {
                string file;
                try
                {
                    file = bp.File;
                }
                catch
                {
                    file = string.Empty;
                }

                int line;
                try
                {
                    line = bp.FileLine;
                }
                catch
                {
                    line = 0;
                }

                var candidate = new BreakpointKey(file ?? string.Empty, line);
                if (!candidate.IsValid)
                {
                    continue;
                }

                if (candidate.Equals(key))
                {
                    match = bp;
                    return true;
                }
            }

            return false;
        }

        private void SendRemoveBreakpoint(BreakpointKey key)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!key.IsValid)
            {
                return;
            }

            var payload = new Dictionary<string, object>
            {
                { "file", key.File },
                { "line", key.Line }
            };

            _broker.SendToRemote(Protocol.Command.RemoveBreakpoint, payload);
            _lastSentPayloads.Remove(key);
        }

        private void InitializeBreakpointSendCompletionCheck()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_readyCommandSent)
            {
                return;
            }

            _setBreakpointCount++;

            var totalBreakpoints = GetCurrentBreakpointCount();
            if (totalBreakpoints > 0 && _setBreakpointCount >= totalBreakpoints)
            {
                TrySendReadyCommand();
            }
        }

        private int GetCurrentBreakpointCount()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var breakpoints = _dte?.Debugger?.Breakpoints;
                return breakpoints?.Count ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        private void TrySendReadyCommand()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_readyCommandSent)
            {
                return;
            }

            if (!_session.RemoteDebuggingActive || !_session.TransportReady)
            {
                return;
            }

            _readyCommandSent = true;
            _broker.SendToRemote(Protocol.Command.Ready, null);
            RemoteEndpointSettings.MarkSessionEstablished();
        }

        private bool? GetEngineEnabledOverride(BreakpointKey key)
        {
            if (_engineEnabledOverrides.TryGetValue(key, out var value))
            {
                return value;
            }

            return null;
        }
    }
}
