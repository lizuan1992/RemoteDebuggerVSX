using System;
using System.IO;
using System.Xml;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using RemoteDebuggerVSX.Logging;

namespace RemoteDebuggerVSX.Debugging
{
    internal static class StartupProjectInspector
    {
        private const string CppProjectKind = "{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}";
        private const int VcConfigurationTypeUtility = 10;

        public static Project TryGetStartupProject(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (dte?.Solution == null)
            {
                return null;
            }

            try
            {
                var projects = dte.Solution.SolutionBuild.StartupProjects as Array;
                if (projects == null || projects.Length == 0)
                {
                    return null;
                }

                var uniqueName = projects.GetValue(0) as string;
                if (string.IsNullOrEmpty(uniqueName))
                {
                    return null;
                }

                foreach (Project p in dte.Solution.Projects)
                {
                    var found = FindProjectRecursive(p, uniqueName);
                    if (found != null)
                    {
                        return found;
                    }
                }

                return null;
            }
            catch
            {
                VsxLog.Debug("StartupProject", "TryGetStartupProject failed.");
                return null;
            }
        }

        private static Project FindProjectRecursive(Project project, string uniqueName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (project == null)
            {
                return null;
            }

            try
            {
                if (string.Equals(project.UniqueName ?? string.Empty, uniqueName, StringComparison.OrdinalIgnoreCase))
                {
                    return project;
                }
            }
            catch
            {
                VsxLog.Debug("StartupProject", "Failed reading Project.UniqueName.");
            }

            try
            {
                var items = project.ProjectItems;
                if (items == null)
                {
                    return null;
                }

                foreach (ProjectItem item in items)
                {
                    Project sub;
                    try
                    {
                        sub = item.SubProject;
                    }
                    catch
                    {
                        sub = null;
                    }

                    if (sub == null)
                    {
                        continue;
                    }

                    var found = FindProjectRecursive(sub, uniqueName);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }
            catch
            {
                VsxLog.Debug("StartupProject", "FindProjectRecursive failed enumerating ProjectItems.");
            }

            return null;
        }

        public static bool IsCppProject(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (project == null)
            {
                return false;
            }

            try
            {
                var kind = project.Kind ?? string.Empty;
                return string.Equals(kind, CppProjectKind, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                VsxLog.Debug("StartupProject", "IsCppProject failed.");
                return false;
            }
        }

        public static string TryGetActiveConfigurationType(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var config = project?.ConfigurationManager?.ActiveConfiguration;
                if (config == null)
                {
                    return null;
                }

                var obj = config.Object;
                if (obj == null)
                {
                    return null;
                }

                var prop = obj.GetType().GetProperty("ConfigurationType");
                if (prop == null)
                {
                    return null;
                }

                var value = prop.GetValue(obj);
                return value?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string TryGetConfigPropertyString(Configuration config, string propertyName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (config == null || string.IsNullOrEmpty(propertyName))
            {
                return null;
            }

            try
            {
                var props = config.Properties;
                if (props == null)
                {
                    return null;
                }

                var p = props.Item(propertyName);
                if (p == null)
                {
                    return null;
                }

                return p.Value?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static bool IsUtilityToken(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            if (text.IndexOf("Utility", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (text.IndexOf("typeUtility", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return int.TryParse(text, out var numeric) && numeric == VcConfigurationTypeUtility;
        }

        private static int? TryGetUtilityTypeNumber(object value)
        {
            if (value is int i)
            {
                return i;
            }

            if (value is short s)
            {
                return s;
            }

            if (value is byte b)
            {
                return b;
            }

            if (value != null && int.TryParse(value.ToString(), out var parsed))
            {
                return parsed;
            }

            return null;
        }

        private static string TryGetVcxprojConfigurationType(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var fullName = project?.FullName;
                if (string.IsNullOrWhiteSpace(fullName) || !File.Exists(fullName))
                {
                    return null;
                }

                if (!string.Equals(Path.GetExtension(fullName), ".vcxproj", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var cfg = project?.ConfigurationManager?.ActiveConfiguration;
                var cfgName = cfg?.ConfigurationName;
                var platform = cfg?.PlatformName;

                if (string.IsNullOrWhiteSpace(cfgName) || string.IsNullOrWhiteSpace(platform))
                {
                    return null;
                }

                var condition = string.Concat("'$(Configuration)|$(Platform)'=='", cfgName, "|", platform, "'");

                var doc = new XmlDocument();
                doc.Load(fullName);

                var nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003");

                var xpath = string.Concat(
                    "//msb:PropertyGroup[@Condition=\"",
                    condition.Replace("\"", "&quot;"),
                    "\"]/msb:ConfigurationType");

                var node = doc.SelectSingleNode(xpath, nsmgr);
                var text = node?.InnerText;
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }
            catch
            {
                return null;
            }
        }

        private static bool LooksLikeCppUtilityConfig(Project project, Configuration config)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var configType = TryGetConfigPropertyString(config, "ConfigurationType");
                if (IsUtilityToken(configType))
                {
                    return true;
                }

                var configTypeName = TryGetConfigPropertyString(config, "ConfigurationTypeName");
                if (IsUtilityToken(configTypeName))
                {
                    return true;
                }

                var vcxprojCt = TryGetVcxprojConfigurationType(project);
                if (IsUtilityToken(vcxprojCt))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsUtilityConfiguration(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var config = project?.ConfigurationManager?.ActiveConfiguration;
                var obj = config?.Object;

                if (obj != null)
                {
                    var prop = obj.GetType().GetProperty("ConfigurationType");
                    var value = prop?.GetValue(obj);

                    var n = TryGetUtilityTypeNumber(value);
                    if (n.HasValue)
                    {
                        return n.Value == VcConfigurationTypeUtility;
                    }

                    if (IsUtilityToken(value?.ToString()))
                    {
                        return true;
                    }
                }
                else
                {
                    if (IsCppProject(project) && config != null && LooksLikeCppUtilityConfig(project, config))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            var configType = TryGetActiveConfigurationType(project) ?? string.Empty;
            return IsUtilityToken(configType);
        }
    }
}
