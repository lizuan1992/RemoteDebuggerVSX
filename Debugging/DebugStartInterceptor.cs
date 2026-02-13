using System;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace RemoteDebuggerVSX.Debugging
{
    internal sealed class DebugStartInterceptor : IDisposable
    {
        private const string VsStd97CommandSetGuid = "{5EFC7975-14BC-11CF-9B2B-00AA00573819}";
        private const int VsStd97CmdIdStart = 295;

        private readonly AsyncPackage _package;
        private DTE2 _dte;
        private CommandEvents _debugStartEvents;

        public event Func<bool> ShouldIntercept;
        public event Func<bool> Intercepted;

        public DebugStartInterceptor(AsyncPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
        }

        public async Task InitializeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

            var dteObj = await _package.GetServiceAsync(typeof(DTE));
            _dte = dteObj as DTE2;
            if (_dte == null)
            {
                return;
            }

            _debugStartEvents = _dte.Events.get_CommandEvents(VsStd97CommandSetGuid, VsStd97CmdIdStart);
            _debugStartEvents.BeforeExecute += OnBeforeExecute;
        }

        private void OnBeforeExecute(string commandSetGuid, int commandId, object customIn, object customOut, ref bool cancelDefault)
        {
            // If VS is already in debug mode, do not intercept
            if (_dte?.Debugger?.CurrentMode == EnvDTE.dbgDebugMode.dbgBreakMode || _dte?.Debugger?.CurrentMode == EnvDTE.dbgDebugMode.dbgRunMode)
            {
                return;
            }

            var intercept = false;
            try
            {
                intercept = ShouldIntercept?.Invoke() == true;
            }
            catch
            {
                intercept = false;
            }

            if (!intercept)
            {
                return;
            }

            cancelDefault = true;

            try
            {
                Intercepted?.Invoke();
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_debugStartEvents != null)
            {
                _debugStartEvents.BeforeExecute -= OnBeforeExecute;
            }

            _debugStartEvents = null;
        }
    }
}
