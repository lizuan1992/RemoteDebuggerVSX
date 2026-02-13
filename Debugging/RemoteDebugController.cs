using System;
using Microsoft.VisualStudio.Shell;
using RemoteDebuggerVSX.Breakpoint;

namespace RemoteDebuggerVSX.Debugging
{
    internal sealed class RemoteDebugController
    {
        private readonly CustomDebuggerLauncher _launcher;
        private readonly BreakpointForwarder _breakpointForwarder;
        private readonly RemoteSessionService _session;

        public RemoteDebugController(CustomDebuggerLauncher launcher, BreakpointForwarder breakpointForwarder, RemoteSessionService session)
        {
            _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
            _breakpointForwarder = breakpointForwarder ?? throw new ArgumentNullException(nameof(breakpointForwarder));
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public bool TryStartDebugging(bool allowSilentlySkipWhenConnected)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            RestartRemoteSessionIfNeeded();

            if (!_launcher.TryPromptEndpointIfNeeded(allowSilentlySkipWhenConnected))
            {
                return false;
            }

            if (!_breakpointForwarder.SetRemoteDebuggingActive(true))
            {
                return false;
            }

            var launched = _launcher.TryLaunch();
            if (!launched)
            {
                _breakpointForwarder.SetRemoteDebuggingActive(false);
                return false;
            }

            return true;
        }

        private void RestartRemoteSessionIfNeeded()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!_session.RemoteDebuggingActive)
            {
                return;
            }

            _breakpointForwarder.SetRemoteDebuggingActive(false);
        }
    }
}
