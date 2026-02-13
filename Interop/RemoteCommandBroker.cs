using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;
using RemoteDebuggerVSX.Breakpoint;
using RemoteDebuggerVSX.CmdTransfer;
using RemoteDebuggerVSX.Debugging;
using RemoteDebuggerVSX.Logging;
using System;
using System.Collections.Generic;
using System.Threading;

namespace RemoteDebuggerVSX.Interop
{
    internal sealed class RemoteCommandBroker : IDisposable
    {
        private static int _seq;

        private readonly AsyncPackage _package;
        private readonly TcpJsonCommandSender _tcpSender;
        private readonly EngineTransportBridge _engineTransportBridge;
        private readonly object _sendLock = new object();

        private bool _transportConnected;
        private int _connectionFailureNotified;
        private long _lastSeq = -1;

        public event Action<string, Dictionary<string, object>> EngineCommandParsed;
        public event Action<string> RemoteConnectionFailed;
        public event Action TransportConnected;


        public RemoteCommandBroker(AsyncPackage package)
        {
            _package = package;

            _tcpSender = new TcpJsonCommandSender();
            _tcpSender.LineReceived += OnRemoteLine;
            _tcpSender.Disconnected += OnRemoteDisconnected;
            _tcpSender.ConnectionFailed += OnRemoteConnectionFailed;

            _engineTransportBridge = new EngineTransportBridge(
                (cmd, payload) =>
                {
                    SendToRemote(cmd, payload);
                    return true;
                },
                48000);

            _engineTransportBridge.EngineCommandParsed += (cmd, payload) =>
            {
                try
                {
                    EngineCommandParsed?.Invoke(cmd, payload);
                }
                catch (Exception ex)
                {
                    VsxLog.Debug("Broker", ex);
                }
            };
        }

        public bool Start()
        {
            if (_transportConnected)
            {
                return true;
            }

            if (!_tcpSender.Connect())
            {
                return false;
            }

            _transportConnected = true;
            _engineTransportBridge.EnsureListening();

            try
            {
                TransportConnected?.Invoke();
            }
            catch (Exception ex)
            {
                VsxLog.Debug("Broker", "TransportConnected handler failed.", ex, TimeSpan.FromSeconds(5));
            }

            return true;
        }

        public void ShutdownTransport()
        {
            ResetTransportState(closeRemoteConnection: true, markTransportDisconnected: true);
        }

        public void SendEngineEvent(string eventName, Dictionary<string, object> payload)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                return;
            }

            var envelope = new Dictionary<string, object>
            {
                { "type", Protocol.Type.Event },
                { "event", eventName }
            };

            if (payload != null)
            {
                foreach (var kv in payload)
                {
                    envelope[kv.Key] = kv.Value;
                }
            }

            try
            {
                var json = JsonConvert.SerializeObject(envelope);
                if (!_engineTransportBridge.TrySendLine(json))
                {
                    VsxLog.Debug("EngineLink", "No engine connection; dropping VSX->engine event.", TimeSpan.FromSeconds(5));
                }
            }
            catch (Exception ex)
            {
                VsxLog.Debug("EngineLink", "Failed to send VSX->engine event.", ex, TimeSpan.FromSeconds(5));
            }
        }

        public void SendToRemote(string command, Dictionary<string, object> payload)
        {
            if (string.IsNullOrEmpty(command) ||
                command == BreakpointForwarder.EngineCommandBreakpointChanged ||
                command == BreakpointForwarder.EngineEventTransportDisconnected)
            {
                return;
            }

            var isStopCommand = string.Equals(command, Protocol.Command.Stop, StringComparison.OrdinalIgnoreCase);

            if (!_transportConnected)
            {
                VsxLog.Debug("Broker", $"Transport not connected; dropping command '{command}'", TimeSpan.FromSeconds(5));
                return;
            }

            var args = payload == null ? new Dictionary<string, object>() : new Dictionary<string, object>(payload);
            var req = new Dictionary<string, object>
            {
                { "seq", Interlocked.Increment(ref _seq) },
                { "type", Protocol.Type.Request },
                { "command", command }
            };

            foreach (var kv in args)
            {
                if (!req.ContainsKey(kv.Key))
                {
                    req[kv.Key] = kv.Value;
                }
            }

            SendSerializedRequest(req);

            if (isStopCommand)
            {
                ResetTransportState(closeRemoteConnection: true, markTransportDisconnected: true);
            }
        }

        private void SendSerializedRequest(Dictionary<string, object> request)
        {
            if (request == null)
            {
                return;
            }

            var json = JsonConvert.SerializeObject(request);
            lock (_sendLock)
            {
                _tcpSender.SendRawLine(json);
            }
        }

        private static bool LooksLikeJsonObjectOrArray(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            var i = 0;
            while (i < line.Length && char.IsWhiteSpace(line[i]))
            {
                i++;
            }

            if (i >= line.Length)
            {
                return false;
            }

            var c = line[i];
            return c == '{' || c == '[';
        }

        private void OnRemoteLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || !LooksLikeJsonObjectOrArray(line))
            {
                return;
            }

            try
            {
                // Check for duplicate seq using string parsing
                var seqIndex = line.IndexOf("\"requestSeq\"");
                if (seqIndex >= 0)
                {
                    var colonIndex = line.IndexOf(':', seqIndex);
                    if (colonIndex >= 0)
                    {
                        var seqPart = line.Substring(colonIndex + 1); // Skip ":"
                        var endIndex = seqPart.IndexOf(',');
                        if (endIndex < 0) endIndex = seqPart.IndexOf('}');
                        if (endIndex > 0)
                        {
                            var seqStr = seqPart.Substring(0, endIndex).Trim();
                            if (long.TryParse(seqStr, out var currentSeq))
                            {
                                if (currentSeq == _lastSeq)
                                {
#if DEBUG
                                    throw new InvalidOperationException($"Duplicate sequence number detected: {currentSeq}. Line: {line}");
#else
                                    VsxLog.Debug("Broker", $"Duplicate sequence number detected: {currentSeq}. Line: {line}", TimeSpan.FromSeconds(5));
#endif
                                }
                                _lastSeq = currentSeq;
                            }
                        }
                    }
                }

                if (!_engineTransportBridge.TrySendLine(line))
                {
                    VsxLog.Debug("EngineLink", "Engine not connected; dropping remote line.", TimeSpan.FromSeconds(5));
                }
            }
            catch (Exception ex)
            {
                VsxLog.Debug("Broker", $"Failed to process line: {line}", ex, TimeSpan.FromSeconds(10));
            }
        }

        private void OnRemoteConnectionFailed(string message)
        {
            var shouldNotify = Interlocked.CompareExchange(ref _connectionFailureNotified, 1, 0) == 0;

            ResetTransportState(closeRemoteConnection: true, markTransportDisconnected: false, preserveFailureNotificationFlag: true);

            if (shouldNotify)
            {
                try
                {
                    RemoteConnectionFailed?.Invoke(message);
                }
                catch (Exception ex)
                {
                    VsxLog.Debug("Broker", "RemoteConnectionFailed handler failed.", ex, TimeSpan.FromSeconds(10));
                }
            }
        }

        private void OnRemoteDisconnected()
        {
            try
            {
                SendEngineEvent("transport_disconnected", null);
            }
            catch (Exception ex)
            {
                VsxLog.Debug("Transport", "Failed to notify engine of transport_disconnected.", ex, TimeSpan.FromSeconds(10));
            }

            ResetTransportState(closeRemoteConnection: false, markTransportDisconnected: true);

            _ = _package.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
                    var dteObj = await _package.GetServiceAsync(typeof(DTE));
                    if (dteObj is DTE dte)
                    {
                        try
                        {
                            dte.Debugger.Stop(true);
                        }
                        catch
                        {
                            try
                            {
                                dte.ExecuteCommand("Debug.StopDebugging");
                            }
                            catch
                            {
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    VsxLog.Debug("Broker", "Failed to stop debugging after transport disconnect.", ex, TimeSpan.FromSeconds(10));
                }
            });
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            ResetTransportState(closeRemoteConnection: true, markTransportDisconnected: true);

            _engineTransportBridge?.Dispose();

            _tcpSender.LineReceived -= OnRemoteLine;
            _tcpSender.Disconnected -= OnRemoteDisconnected;
            _tcpSender.ConnectionFailed -= OnRemoteConnectionFailed;
            _tcpSender.Dispose();
        }

        private void ResetTransportState(bool closeRemoteConnection, bool markTransportDisconnected, bool preserveFailureNotificationFlag = false)
        {
            Interlocked.Exchange(ref _seq, 0);
            _transportConnected = false;

            if (!preserveFailureNotificationFlag)
            {
                Interlocked.Exchange(ref _connectionFailureNotified, 0);
            }

            if (closeRemoteConnection)
            {
                try
                {
                    _tcpSender.Close(suppressFailureNotifications: true);
                }
                catch (Exception ex)
                {
                    VsxLog.Debug("Transport", "Reset close failed.", ex, TimeSpan.FromSeconds(5));
                }
            }

            try
            {
                _engineTransportBridge?.DisconnectEngine();
            }
            catch (Exception ex)
            {
                VsxLog.Debug("EngineLink", "Failed to disconnect engine client.", ex, TimeSpan.FromSeconds(5));
            }

            if (markTransportDisconnected)
            {
                try
                {
                    RemoteEndpointSettings.MarkTransportDisconnected();
                }
                catch (Exception ex)
                {
                    VsxLog.Debug("Transport", ex);
                }
            }
        }
    }
}
