using System;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using RemoteDebuggerVSX.Logging;

namespace RemoteDebuggerVSX.Debugging
{
    internal enum DebugSessionMode
    {
        Design = 0,
        Running = 1,
        Break = 2
    }

    internal sealed class DebugSessionState : IDisposable
    {
        private readonly AsyncPackage _package;
        private DTE2 _dte;
        private DebuggerEvents _debuggerEvents;

        public DebugSessionMode Mode { get; private set; } = DebugSessionMode.Design;

        public event Action ModeChanged;

        public DebugSessionState(AsyncPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
        }

        public async Task InitializeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

            var dteObj = await _package.GetServiceAsync(typeof(EnvDTE.DTE));
            _dte = dteObj as DTE2;
            if (_dte == null)
            {
                return;
            }

            _debuggerEvents = _dte.Events.DebuggerEvents;
            _debuggerEvents.OnEnterDesignMode += OnEnterDesignMode;
            _debuggerEvents.OnEnterRunMode += OnEnterRunMode;
            _debuggerEvents.OnEnterBreakMode += OnEnterBreakMode;

            RefreshFromDte();
        }

        private void RefreshFromDte()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var debugger = _dte?.Debugger;
                SetMode(debugger == null ? DebugSessionMode.Design : ToSessionMode(debugger.CurrentMode));
            }
            catch
            {
                VsxLog.Debug("Session", "Failed to read DTE.Debugger.CurrentMode; defaulting to Design.");
                SetMode(DebugSessionMode.Design);
            }
        }

        private static DebugSessionMode ToSessionMode(dbgDebugMode mode)
        {
            switch (mode)
            {
                case dbgDebugMode.dbgBreakMode:
                    return DebugSessionMode.Break;
                case dbgDebugMode.dbgRunMode:
                    return DebugSessionMode.Running;
                default:
                    return DebugSessionMode.Design;
            }
        }

        private void SetMode(DebugSessionMode mode)
        {
            if (Mode == mode)
            {
                return;
            }

            Mode = mode;

            try
            {
                ModeChanged?.Invoke();
            }
            catch (Exception ex)
            {
                VsxLog.Debug("Session", "ModeChanged handler failed.", ex, TimeSpan.FromSeconds(5));
            }
        }

        private void OnEnterDesignMode(dbgEventReason reason)
        {
            SetMode(DebugSessionMode.Design);
        }

        private void OnEnterRunMode(dbgEventReason reason)
        {
            SetMode(DebugSessionMode.Running);
        }

        private void OnEnterBreakMode(dbgEventReason reason, ref dbgExecutionAction executionAction)
        {
            SetMode(DebugSessionMode.Break);
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_debuggerEvents == null)
            {
                return;
            }

            _debuggerEvents.OnEnterDesignMode -= OnEnterDesignMode;
            _debuggerEvents.OnEnterRunMode -= OnEnterRunMode;
            _debuggerEvents.OnEnterBreakMode -= OnEnterBreakMode;

            _debuggerEvents = null;
            _dte = null;
        }
    }
}
