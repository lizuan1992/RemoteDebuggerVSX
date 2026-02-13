using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using RemoteDebuggerVSX.Commands;
using RemoteDebuggerVSX.Debugging;
using Task = System.Threading.Tasks.Task;

namespace RemoteDebuggerVSX
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class StopDebugger : DebugCommandBase
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 4129;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("98d1d048-98f2-46d6-8bb6-0747c535123e");

        /// <summary>
        /// Initializes a new instance of the <see cref="StopDebugger"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private StopDebugger(AsyncPackage package, OleMenuCommandService commandService)
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

            // Design: Stop disabled
            // Running/Break: Stop enabled
            cmd.Enabled = mode != DebugSessionMode.Design;

            // Keep a stable, explicit caption (avoid confusion with built-in VS commands).
            cmd.Text = "Debugger Stop";
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static StopDebugger Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in StopDebugger's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService == null)
            {
                return;
            }

            Instance = new StopDebugger(package, commandService);
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // Execute the same command as the native toolbar Stop button.
                ExecuteVsCommand("Debug.StopDebugging");
            }
            catch (Exception ex)
            {
                VsShellUtilities.ShowMessageBox(
                    OwnerPackage,
                    ex.ToString(),
                    "StopDebugger",
                    OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }
    }
}
