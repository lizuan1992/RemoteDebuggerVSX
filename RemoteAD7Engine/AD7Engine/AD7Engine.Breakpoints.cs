using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace RemoteAD7Engine
{
    public partial class AD7Engine
    {
        internal void NotifyVsxBreakpointChanged(string changeType, AD7PendingBreakPoint pending, bool? enabled = null)
        {
            try
            {
                if (pending == null)
                    return;

                var normalizedChangeType = string.Equals(changeType, "removed", StringComparison.OrdinalIgnoreCase)
                    ? "removed"
                    : (changeType ?? string.Empty);

                var payload = new Dictionary<string, object>
                {
                    { "changeType", normalizedChangeType }
                };

                string file = null;
                var line = 0;

                try
                {
                    if (pending.GetBreakpointRequest(out var req) == VSConstants.S_OK && req != null)
                    {
                        var requestInfo = new BP_REQUEST_INFO[1];
                        req.GetRequestInfo(enum_BPREQI_FIELDS.BPREQI_BPLOCATION, requestInfo);
                        var location = requestInfo[0].bpLocation;
                        var docPosition = (IDebugDocumentPosition2)Marshal.GetObjectForIUnknown(location.unionmember2);

                        docPosition.GetFileName(out var documentName);

                        var startPosition = new TEXT_POSITION[1];
                        var endPosition = new TEXT_POSITION[1];
                        docPosition.GetRange(startPosition, endPosition);

                        file = documentName;
                        line = ((int)startPosition[0].dwLine) + 1;
                    }
                }
                catch
                {
                }

                if ((string.IsNullOrEmpty(file) || line <= 0) && pending.TryGetLocation(out var fallbackFile, out var fallbackLine))
                {
                    file = fallbackFile;
                    line = fallbackLine;
                }

                payload["file"] = string.IsNullOrEmpty(file) ? string.Empty : file;
                payload["line"] = line > 0 ? line : 0;

                if (enabled != null)
                    payload["enabled"] = enabled.Value;

                SendToVsx("breakpoint_changed", payload);
            }
            catch
            {
            }
        }

        private void ClearPendingBreakpoints()
        {
            _pendingBreakpointsByLocation.Clear();
        }

        internal static string MakeLocationKey(string file, int? line)
        {
            if (line == null || line <= 0)
                return null;

            return string.Concat(file, ":", line.Value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
