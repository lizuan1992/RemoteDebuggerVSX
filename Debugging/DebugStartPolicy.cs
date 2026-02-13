using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using RemoteDebuggerVSX.Logging;

namespace RemoteDebuggerVSX.Debugging
{
    internal sealed class DebugStartPolicy
    {
        public bool ShouldIntercept(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var startupProject = StartupProjectInspector.TryGetStartupProject(dte);
            if (!StartupProjectInspector.IsCppProject(startupProject))
            {
                return false;
            }

            return StartupProjectInspector.IsUtilityConfiguration(startupProject);
        }

        public string Describe(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var p = StartupProjectInspector.TryGetStartupProject(dte);

                var kind = string.Empty;
                try
                {
                    kind = p?.Kind ?? string.Empty;
                }
                catch
                {
                    kind = string.Empty;
                }

                var cfgName = string.Empty;
                var platName = string.Empty;

                try
                {
                    var cfg = p?.ConfigurationManager?.ActiveConfiguration;
                    cfgName = cfg?.ConfigurationName ?? string.Empty;
                    platName = cfg?.PlatformName ?? string.Empty;
                }
                catch
                {
                }

                var isCpp = StartupProjectInspector.IsCppProject(p);
                var isUtility = isCpp && StartupProjectInspector.IsUtilityConfiguration(p);
                var cfgType = isCpp ? (StartupProjectInspector.TryGetActiveConfigurationType(p) ?? string.Empty) : string.Empty;

                return $"startupProject='{p?.UniqueName ?? ""}' kind='{kind}' cfg='{cfgName}' platform='{platName}' cfgType='{cfgType}' isCpp={isCpp} isUtility={isUtility}";
            }
            catch
            {
                VsxLog.Debug("Intercept", "DebugStartPolicy.Describe failed.");
                return "<describe failed>";
            }
        }
    }
}
