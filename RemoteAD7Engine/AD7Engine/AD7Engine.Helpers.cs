using System;
using System.Globalization;
using System.IO;

namespace RemoteAD7Engine
{
    public partial class AD7Engine
    {
        internal static string NormalizeBreakpointPath(String file)
        {
            if (string.IsNullOrWhiteSpace(file))
            {
                return string.Empty;
            }

            var normalized = file.Replace('/', '\\').Trim();
            try
            {
                if (Path.IsPathRooted(normalized))
                {
                    normalized = Path.GetFullPath(normalized);
                }
                else
                {
                    // Get the first folder name from normalized
                    var firstFolder = GetFirstFolder(normalized);
                    var currentDirectory = Environment.CurrentDirectory;

                    var pos = currentDirectory.IndexOf(firstFolder + '\\');
                    if (pos != -1)
                    {
                        normalized = Path.GetFullPath(Path.Combine(currentDirectory.Substring(0, pos), normalized));
                    }
                    else
                    {
                        pos = currentDirectory.IndexOf(firstFolder);
                        if (pos != -1)
                        {
                            normalized = Path.GetFullPath(Path.Combine(currentDirectory.Substring(0, pos), normalized));
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[RemoteAD7Engine] Warning: First folder '{firstFolder}' of path '{normalized}' not found in current directory '{Environment.CurrentDirectory}'. Using default normalization.");
                        }
                    }
                }
            }
            catch
            {
            }

            return normalized;
        }

        private static String GetFirstFolder(String path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var parts = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : null;
        }

        private static bool TryParseEndpoint(String endpoint, out String host, out int port)
        {
            host = null;
            port = 0;
            if (string.IsNullOrWhiteSpace(endpoint))
                return false;

            var trimmed = endpoint.Trim();
            var sepIndex = trimmed.LastIndexOf(':');
            if (sepIndex <= 0 || sepIndex >= trimmed.Length - 1)
                return false;

            var portText = trimmed.Substring(sepIndex + 1);
            if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) || port <= 0 || port > 65535)
            {
                port = 0;
                return false;
            }

            host = trimmed.Substring(0, sepIndex).Trim();
            if (string.IsNullOrEmpty(host))
            {
                host = null;
                port = 0;
                return false;
            }

            return true;
        }

        private static bool IsInsideBlockComment(String[] lines, int oneBasedLine)
        {
            try
            {
                var inBlock = false;

                var max = Math.Min(lines.Length, Math.Max(0, oneBasedLine - 1));
                for (var i = 0; i < max; i++)
                {
                    var line = lines[i] ?? string.Empty;

                    for (var j = 0; j < line.Length; j++)
                    {
                        if (!inBlock)
                        {
                            if (j + 1 < line.Length && line[j] == '/' && line[j + 1] == '*')
                            {
                                inBlock = true;
                                j++;
                                continue;
                            }

                            if (j + 1 < line.Length && line[j] == '/' && line[j + 1] == '/')
                            {
                                break;
                            }
                        }
                        else
                        {
                            if (j + 1 < line.Length && line[j] == '*' && line[j + 1] == '/')
                            {
                                inBlock = false;
                                j++;
                            }
                        }
                    }
                }

                return inBlock;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsInsideMacroContinuation(String[] lines, int oneBasedLine)
        {
            try
            {
                var max = Math.Min(lines.Length, Math.Max(0, oneBasedLine - 1));
                for (var i = max - 1; i >= 0; i--)
                {
                    var line = (lines[i] ?? string.Empty).TrimEnd();
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    if (line.EndsWith("\\", StringComparison.Ordinal))
                    {
                        return true;
                    }

                    return false;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsLikelyExecutableSourceLine(String file, int line)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(file) || line <= 0)
                    return false;

                if (!File.Exists(file))
                    return true; // best-effort: don't disable if we can't inspect source

                var lines = File.ReadAllLines(file);
                if (line > lines.Length)
                    return false;

                if (IsInsideBlockComment(lines, line) || IsInsideMacroContinuation(lines, line))
                    return false;

                var raw = lines[line - 1] ?? string.Empty;
                var text = raw.Trim();

                if (string.IsNullOrEmpty(text))
                    return false;

                if (text.StartsWith("//", StringComparison.Ordinal) || text.StartsWith("#", StringComparison.Ordinal))
                    return false;

                // Best-effort block comment support (single-line or comment boundary line).
                if (text.StartsWith("/*", StringComparison.Ordinal)
                    || text.EndsWith("*/", StringComparison.Ordinal)
                    || (text.Contains("/*") && !text.Contains("*/")))

                    return false;

                // C/C++ macro line continuation or escaped newline.
                if (text.EndsWith("\\", StringComparison.Ordinal))
                    return false;

                if (text.StartsWith("[", StringComparison.Ordinal) && text.EndsWith("]", StringComparison.Ordinal))
                    return false;

                if (text.EndsWith(":", StringComparison.Ordinal))
                {
                    var prefix = text.Substring(0, text.Length - 1).Trim();
                    if (string.Equals(prefix, "public", StringComparison.Ordinal)
                        || string.Equals(prefix, "private", StringComparison.Ordinal)
                        || string.Equals(prefix, "protected", StringComparison.Ordinal)
                        || text.StartsWith("case ", StringComparison.Ordinal)
                        || string.Equals(text, "default:", StringComparison.Ordinal))
                        return false;
                }

                if (text == ";" || text == "};" || text == "});")
                    return false;

                // Stand-alone control keywords that are not executable by themselves.
                if ((string.Equals(text, "else", StringComparison.Ordinal)
                     || string.Equals(text, "finally", StringComparison.Ordinal)
                     || string.Equals(text, "do", StringComparison.Ordinal))
                    && !text.Contains("{"))
                    return false;

                if (text.StartsWith("catch", StringComparison.Ordinal) && !text.Contains("{"))
                    return false;

                if (text.StartsWith("using ", StringComparison.Ordinal)
                    || text.StartsWith("namespace ", StringComparison.Ordinal)
                    || text.StartsWith("class ", StringComparison.Ordinal)
                    || text.StartsWith("struct ", StringComparison.Ordinal)
                    || text.StartsWith("interface ", StringComparison.Ordinal)
                    || text.StartsWith("enum ", StringComparison.Ordinal))
                    return false;

                // Additional check: if line contains arithmetic operators, likely executable
                if (ContainsArithmeticOperator(text))
                    return true;

                return true;
            }
            catch
            {
                // best-effort: avoid accidentally disabling valid breakpoints
                return true;
            }
        }

        private sealed class DoneGuard : IDisposable
        {
            private readonly RemoteState _state;
            public string Command { get; set; }

            public DoneGuard(RemoteState state)
            {
                _state = state;
            }

            public void Dispose()
            {
                _state.SyncManager.Set(Command);
            }
        }

        private static bool ContainsArithmeticOperator(string text)
        {
            // Check for arithmetic operators outside of strings and comments
            // Simple heuristic: look for +, -, *, /, % not inside quotes or after //
            var inString = false;
            var inComment = false;
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == '"' && (i == 0 || text[i - 1] != '\\'))
                {
                    inString = !inString;
                }
                else if (!inString && ch == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    inComment = true;
                    break;
                }
                else if (!inString && !inComment && (ch == '+' || ch == '-' || ch == '*' || ch == '/' || ch == '%'))
                {
                    // Ensure it's not part of ++, --, +=, etc.
                    if (ch == '+' && i + 1 < text.Length && text[i + 1] == '+') continue;
                    if (ch == '-' && i + 1 < text.Length && text[i + 1] == '-') continue;
                    if (i + 1 < text.Length && text[i + 1] == '=') continue;
                    return true;
                }
            }
            return false;
        }
    }
}
