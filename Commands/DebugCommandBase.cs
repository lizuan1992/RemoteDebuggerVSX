using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace RemoteDebuggerVSX.Commands
{
    internal abstract class DebugCommandBase
    {
        protected AsyncPackage OwnerPackage { get; }

        protected DebugCommandBase(AsyncPackage package)
        {
            OwnerPackage = package ?? throw new ArgumentNullException(nameof(package));
        }

        protected void HookUpdateOnModeChange()
        {
            if (OwnerPackage is RemoteDebuggerVSXPackage pkg && pkg.DebugSessionState != null)
            {
                pkg.DebugSessionState.ModeChanged += OnModeChanged;
            }
        }

        protected abstract void OnModeChanged();

        protected static T TryGetService<T>(Type serviceType) where T : class
        {
            try
            {
                return Package.GetGlobalService(serviceType) as T;
            }
            catch
            {
                return null;
            }
        }

        protected static void RefreshCommandUI()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var uiShell = TryGetService<IVsUIShell>(typeof(SVsUIShell));
            uiShell?.UpdateCommandUI(0);
        }

        protected static void ExecuteVsCommand(string command)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var dte = TryGetService<EnvDTE80.DTE2>(typeof(SDTE));
                dte?.ExecuteCommand(command);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to execute VS command '{command}': {e}");
            }
        }
    }
}
