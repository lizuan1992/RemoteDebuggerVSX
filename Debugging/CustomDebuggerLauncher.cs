using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using RemoteAD7Engine;
using RemoteDebuggerVSX.Interop;
using RemoteDebuggerVSX.UI;

namespace RemoteDebuggerVSX.Debugging
{
    internal sealed class CustomDebuggerLauncher
    {
        private readonly AsyncPackage _package;

        public CustomDebuggerLauncher(AsyncPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
        }

        public bool TryPromptEndpointIfNeeded(bool allowSilentlySkipWhenConnected)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (allowSilentlySkipWhenConnected && RemoteEndpointSettings.EverEstablished)
            {
                return true;
            }

            using (var dlg = new RemoteEndpointDialog(RemoteEndpointSettings.Host, RemoteEndpointSettings.Port))
            {
                var result = dlg.ShowDialog();
                if (result != System.Windows.Forms.DialogResult.OK)
                {
                    return false;
                }

                return RemoteEndpointSettings.TrySet(dlg.Host, dlg.PortText);
            }
        }

        public bool TryLaunch()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var vsDebuggerObj = Package.GetGlobalService(typeof(SVsShellDebugger));
            if (!(vsDebuggerObj is IVsDebugger4 vsDebugger4))
            {
                return false;
            }

            var debugTargets = new VsDebugTargetInfo4[1];
            debugTargets[0].dlo = (uint)DEBUG_LAUNCH_OPERATION.DLO_CreateProcess;
            debugTargets[0].bstrExe = string.Concat(RemoteEndpointSettings.DefaultHost, ":", EngineTransportBridge.ActivePort);
            debugTargets[0].guidLaunchDebugEngine = new Guid(EngineConstants.EngineGUID);

            var processInfo = new VsDebugTargetProcessInfo[debugTargets.Length];
            vsDebugger4.LaunchDebugTargets4(1, debugTargets, processInfo);
            return true;
        }
    }
}
