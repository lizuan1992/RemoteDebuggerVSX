using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using RemoteDebuggerVSX.Commands;
using RemoteDebuggerVSX.Debugging;
using Task = System.Threading.Tasks.Task;
using EnvDTE;
using EnvDTE80;

namespace RemoteDebuggerVSX
{
    internal sealed class StartDebugger : DebugCommandBase
    {
        public const int CommandId = 0x0100;

        public static readonly Guid CommandSet = new Guid("98d1d048-98f2-46d6-8bb6-0747c535123e");

        private DTE2 _dte;

        private StartDebugger(AsyncPackage package, OleMenuCommandService commandService)
            : base(package)
        {
            if (commandService == null)
            {
                throw new ArgumentNullException(nameof(commandService));
            }

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuCommand = new OleMenuCommand(Execute, menuCommandID);
            menuCommand.BeforeQueryStatus += OnBeforeQueryStatus;
            commandService.AddCommand(menuCommand);

            HookUpdateOnModeChange();
        }

        protected override void OnModeChanged()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            RefreshCommandUI();
        }

        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var cmd = sender as OleMenuCommand;
            if (cmd == null)
            {
                return;
            }

            var pkg = OwnerPackage as RemoteDebuggerVSXPackage;
            var mode = pkg?.DebugSessionState?.Mode ?? DebugSessionMode.Design;

            cmd.Enabled = true;
            switch (mode)
            {
                case DebugSessionMode.Running:
                    cmd.Text = "Debugger Pause";
                    break;
                case DebugSessionMode.Break:
                    cmd.Text = "Debugger Continue";
                    break;
                default:
                    cmd.Text = "Debugger Start";
                    break;
            }
        }

        public static StartDebugger Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService == null)
            {
                return;
            }

            Instance = new StartDebugger(package, commandService);

            var dteObj = await package.GetServiceAsync(typeof(DTE));
            Instance._dte = dteObj as DTE2;
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var pkg = OwnerPackage as RemoteDebuggerVSXPackage;
                var mode = pkg?.DebugSessionState?.Mode ?? DebugSessionMode.Design;

                if (mode == DebugSessionMode.Running)
                {
                    _dte?.Debugger?.Break();
                    return;
                }

                if (mode == DebugSessionMode.Break)
                {
                    _dte?.Debugger?.Go();
                    return;
                }

                var vsDebuggerObj2 = Package.GetGlobalService(typeof(SVsShellDebugger));
                if (vsDebuggerObj2 is IVsDebugger4)
                {
                    pkg?.DebugController?.TryStartDebugging(allowSilentlySkipWhenConnected: true);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in StartDebugger.Execute: {ex.Message}");
            }
        }
    }
}
