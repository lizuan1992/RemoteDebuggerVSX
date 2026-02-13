using System;
using System.Collections.Generic;

namespace RemoteDebuggerVSX.Logging
{
    internal static class VsxLog
    {
        private const string DefaultCategory = "General";

        private static readonly object _gate = new object();
        private static readonly Dictionary<string, DateTime> _lastByKey = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static readonly TimeSpan _defaultInterval = TimeSpan.FromSeconds(2);

        public static void Debug(string category, string message)
        {
            Debug(category, message, _defaultInterval);
        }

        public static void Debug(string category, Exception ex)
        {
            if (ex == null)
            {
                return;
            }

            Debug(category, ex.ToString());
        }

        public static void Debug(string category, string message, Exception ex, TimeSpan minInterval)
        {
            if (ex == null)
            {
                Debug(category, message, minInterval);
                return;
            }

            var text = string.IsNullOrEmpty(message)
                ? ex.ToString()
                : string.Concat(message, " | ", ex);

            Debug(category, text, minInterval);
        }

        public static void Debug(string category, string message, TimeSpan minInterval)
        {
            try
            {
                if (string.IsNullOrEmpty(category))
                {
                    category = DefaultCategory;
                }

                var text = message ?? string.Empty;
                var key = string.Concat(category, "|", text);

                var now = DateTime.UtcNow;
                lock (_gate)
                {
                    if (_lastByKey.TryGetValue(key, out var last) && (now - last) < minInterval)
                    {
                        return;
                    }

                    _lastByKey[key] = now;
                }

                System.Diagnostics.Debug.WriteLine($"[RemoteDebuggerVSX][{category}] {text}");
            }
            catch
            {
            }
        }
    }
}
