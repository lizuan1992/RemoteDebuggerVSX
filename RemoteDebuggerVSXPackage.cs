using System;
using System.Runtime.InteropServices;
using System.Threading;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using RemoteDebuggerVSX.Breakpoint;
using RemoteDebuggerVSX.Debugging;
using Task = System.Threading.Tasks.Task;

namespace RemoteDebuggerVSX
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(RemoteDebuggerVSXPackage.PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideAutoLoad(Microsoft.VisualStudio.VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(Microsoft.VisualStudio.VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class RemoteDebuggerVSXPackage : AsyncPackage
    {
        public const string PackageGuidString = "ab854986-e818-443c-ab3b-6c5d3cd30dd6";

        private BreakpointForwarder _breakpointForwarder;
        private DebugSessionState _debugSessionState;
        private DebugStartInterceptor _debugStartInterceptor;
        private RemoteDebugController _debugController;

        internal DebugSessionState DebugSessionState
        {
            get { return _debugSessionState; }
        }

        internal BreakpointForwarder BreakpointForwarder
        {
            get { return _breakpointForwarder; }
        }

        internal RemoteDebugController DebugController
        {
            get { return _debugController; }
        }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            await StartDebugger.InitializeAsync(this);
            await StopDebugger.InitializeAsync(this);

            _debugSessionState = new DebugSessionState(this);
            await _debugSessionState.InitializeAsync();

            var dteObj = await GetServiceAsync(typeof(EnvDTE.DTE));
            var dte = dteObj as DTE2;

            var launcher = new CustomDebuggerLauncher(this);
            var debugStartPolicy = new DebugStartPolicy();
            var sessionService = new RemoteSessionService();

            _debugStartInterceptor = new DebugStartInterceptor(this);
            await _debugStartInterceptor.InitializeAsync();

            _debugStartInterceptor.ShouldIntercept += () =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                if (dte == null)
                {
                    return false;
                }

                return debugStartPolicy.ShouldIntercept(dte);
            };

            _breakpointForwarder = new BreakpointForwarder(this, sessionService);
            await _breakpointForwarder.InitializeAsync();

            _debugController = new RemoteDebugController(launcher, _breakpointForwarder, sessionService);

            _debugStartInterceptor.Intercepted += () =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return _debugController?.TryStartDebugging(allowSilentlySkipWhenConnected: true) ?? true;
            };
        }

        protected override void Dispose(bool disposing)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (disposing)
            {
                _breakpointForwarder?.Dispose();
                _debugStartInterceptor?.Dispose();
                _debugSessionState?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
