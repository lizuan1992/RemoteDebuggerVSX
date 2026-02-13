using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using RemoteDebuggerVSX.Logging;

namespace RemoteDebuggerVSX.Utilities
{
    internal static class FireAndForget
    {
        public static void Run(AsyncPackage package, Func<Task> asyncAction, string category = "Async")
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            if (asyncAction == null)
            {
                throw new ArgumentNullException(nameof(asyncAction));
            }

            _ = package.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await asyncAction().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    VsxLog.Debug(category, ex);
                }
            });
        }
    }
}
