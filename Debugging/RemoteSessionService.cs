using Microsoft.VisualStudio.Shell;

namespace RemoteDebuggerVSX.Debugging
{
    internal sealed class RemoteSessionService
    {
        private bool _remoteDebuggingActive;
        private bool _transportReady;

        public bool RemoteDebuggingActive => _remoteDebuggingActive;
        public bool TransportReady => _transportReady;

        public void Activate()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SetState(true, _transportReady);
        }

        public void Deactivate()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SetState(false, false);
        }

        public void SetTransportReady(bool ready)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SetState(_remoteDebuggingActive, ready);
        }

        private void SetState(bool active, bool ready)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _remoteDebuggingActive = active;
            _transportReady = ready;
        }
    }
}
