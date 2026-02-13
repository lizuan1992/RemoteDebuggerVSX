// This file implements protocol handling according to RemoteProtocol.md documentation.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RemoteAD7Engine
{
    public partial class AD7Engine
    {
        private const string ProtocolWarningPrefix = ProtocolPrefix;
        private const string DroppingMessage = "Dropping message:";

        private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

        private long _lastSeq = -1;

        private void LogProtocolWarning(string message)
        {
            try
            {
                Debug.WriteLine(message);

                foreach (var thread in _state.ThreadsInfo)
                {
                    Callback.Send(new AD7OutputStringEvent(string.Concat(message, "\n")), AD7OutputStringEvent.IID, this, thread.Value.Ad7Thread);
                    return;
                }

            }
            catch
            {
            }
        }

        private void HandleVsxLine(string line)
        {
            // Process a single JSON line from VSX
            using (var temp = new DoneGuard(_state))
            {
                temp.Command = "<unknown>";

                try
                {
                    var settings = new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.None
                    };

                    if (!(JsonConvert.DeserializeObject<Dictionary<string, object>>(line, settings) is Dictionary<string, object> obj))
                    {
                        LogProtocolWarning(line);
                        temp.Command = null;
                        return;
                    }

                    // Check for duplicate seq
                    if (obj.TryGetValue("requestSeq", out var seqObj) && (seqObj is long || seqObj is int))
                    {
                        var currentSeq = seqObj is long ? (long)seqObj : (int)seqObj;
                        if (currentSeq == _lastSeq)
                        {
#if DEBUG
                            throw new InvalidOperationException($"Duplicate sequence number detected: {currentSeq}. Line: {line}");
#else
                            Debug.WriteLine($"[AD7Engine] Duplicate sequence number detected: {currentSeq}. Line: {line}");
#endif
                        }
                        _lastSeq = currentSeq;
                    }

                    var msg = ToFlatStringMap(obj);

                    if (!msg.TryGetValue("type", out var type) | string.IsNullOrEmpty(type))
                    {
                        LogProtocolWarning($"{ProtocolWarningPrefix} {DroppingMessage} missing type");
                        temp.Command = null;
                        return;
                    }

                    if (!IsSupportedMessageType(type))
                    {
                        LogProtocolWarning($"{ProtocolWarningPrefix} {DroppingMessage} invalid type '{type}'");
                        temp.Command = null;
                        return;
                    }

                    if (EqualsOrdinalIgnoreCase(type, MessageTypeEvent))
                    {
                        if (!msg.TryGetValue("event", out var evtName) | string.IsNullOrEmpty(evtName))
                        {
                            LogProtocolWarning($"{ProtocolWarningPrefix} Dropping event: missing event name");
                            temp.Command = null;
                            return;
                        }

                        HandleEventMessage(evtName, msg);
                        return;
                    }

                    if (EqualsOrdinalIgnoreCase(type, MessageTypeResponse))
                    {
                        if (!msg.TryGetValue("command", out var cmdName) | string.IsNullOrEmpty(cmdName))
                        {
                            LogProtocolWarning($"{ProtocolWarningPrefix} Dropping response: missing command");
                            temp.Command = null;
                            return;
                        }

                        temp.Command = cmdName;

                        if (!msg.TryGetValue("success", out var successStr) | string.IsNullOrEmpty(successStr))
                        {
                            LogProtocolWarning($"{ProtocolWarningPrefix} Dropping response: missing success");
                            return;
                        }

                        if (!msg.TryGetValue("requestSeq", out var requestSeq) | string.IsNullOrEmpty(requestSeq))
                        {
                            LogProtocolWarning($"{ProtocolWarningPrefix} Dropping response: missing/invalid requestSeq");
                            return;
                        }

                        if (string.Equals(successStr, "false", StringComparison.OrdinalIgnoreCase)
                            && (!msg.TryGetValue("message", out var errMsg) | string.IsNullOrEmpty(errMsg)))
                        {
                            LogProtocolWarning($"{ProtocolWarningPrefix} Invalid failure response: success=false but message is missing/empty; injecting placeholder message.");
                            msg["message"] = "empty";
                        }

                        if (string.Equals(successStr, "true", StringComparison.OrdinalIgnoreCase)
                            && msg.TryGetValue("message", out var unexpectedMsg)
                            && !string.IsNullOrEmpty(unexpectedMsg))
                        {
                            LogProtocolWarning($"{ProtocolWarningPrefix} Success response included unexpected message; dropping message field.");
                            msg.Remove("message");
                        }

                        HandleResponseMessage(cmdName, msg, obj);
                    }
                }
                catch (Exception ex)
                {
                    temp.Command = null;
                    LogProtocolWarning($"{ProtocolWarningPrefix} Error processing line: {ex.Message}");
                    LogProtocolWarning(line);
                }
            }
        }

        private static Dictionary<string, string> ToFlatStringMap(Dictionary<string, object> obj)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in obj)
            {
                if (kv.Value == null)
                {
                    dict[kv.Key] = null;
                    continue;
                }

                if (kv.Value is string s)
                {
                    dict[kv.Key] = s;
                    continue;
                }

                if (kv.Value is int || kv.Value is long || kv.Value is double || kv.Value is float || kv.Value is decimal)
                {
                    dict[kv.Key] = Convert.ToString(kv.Value, InvariantCulture);
                    continue;
                }

                if (kv.Value is bool b)
                {
                    dict[kv.Key] = b ? "true" : "false";
                    continue;
                }

                if (kv.Value is Dictionary<string, object>)
                {
                    dict[kv.Key] = "{...}";
                    continue;
                }

                if (kv.Value is IList)
                {
                    dict[kv.Key] = "[...]";
                    continue;
                }

                dict[kv.Key] = kv.Value.ToString();
            }

            return dict;
        }

        private void HandleThreadRemoved(int threadId)
        {
            _state.RemoveAd7Thread(threadId);
            _state.RemoveThread(threadId);
        }

        private int GetRequiredThreadId(Dictionary<string, string> msg)
        {
            if (msg != null && msg.TryGetValue("threadId", out var tidStr) && int.TryParse(tidStr, out var tid))
            {
                return tid;
            }

            throw new InvalidOperationException($"{ProtocolPrefix} Missing or invalid threadId in message");
        }

        private bool FailUnsupportedSchema(string command, string message)
        {
            LogProtocolWarning(message);
            return false;
        }

        private static bool IsSuccess(Dictionary<string, string> msg)
        {
            return msg != null
                && msg.TryGetValue("success", out var s)
                && string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetRawList(Dictionary<string, object> raw, string key, out IList list)
        {
            list = null;
            if (raw == null)
            {
                return false;
            }

            if (!raw.TryGetValue(key, out var obj))
            {
                return false;
            }

            list = obj as IList;
            return list != null;
        }

        private static bool TryGetRawObject(IList list, int index, out Dictionary<string, object> obj)
        {
            obj = null;
            if (list == null || index < 0 || index >= list.Count)
            {
                return false;
            }

            var item = list[index];
            if (item is Dictionary<string, object> dict)
            {
                obj = dict;
                return true;
            }
            else if (item is JObject jObj)
            {
                obj = jObj.ToObject<Dictionary<string, object>>();
                return true;
            }

            return false;
        }

        private static bool HasRequiredString(Dictionary<string, object> obj, string key)
        {
            return obj != null && obj.TryGetValue(key, out var value) && value != null;
        }

        private static bool HasRequiredInt(Dictionary<string, object> obj, string key, out int value)
        {
            value = 0;
            return obj != null && obj.TryGetValue(key, out var val) && val != null && int.TryParse(val.ToString(), out value);
        }

        private static bool HasOptionalIntIfPresent(Dictionary<string, object> obj, string key)
        {
            if (obj == null)
            {
                return true;
            }

            if (!obj.TryGetValue(key, out var value) || value == null)
            {
                return true;
            }

            return int.TryParse(value.ToString(), out _);
        }

        private static bool HasOptionalBoolIfPresent(Dictionary<string, object> obj, string key)
        {
            if (obj == null)
            {
                return true;
            }

            if (!obj.TryGetValue(key, out var value) || value == null)
            {
                return true;
            }

            return bool.TryParse(value.ToString(), out _);
        }

        private static bool HasRequiredBool(Dictionary<string, object> obj, string key)
        {
            return obj != null && obj.TryGetValue(key, out var value) && value != null && bool.TryParse(value.ToString(), out _);
        }

        private static bool HasRequiredLong(Dictionary<string, object> obj, string key, out long value)
        {
            value = 0;
            return obj != null && obj.TryGetValue(key, out var val) && val != null && long.TryParse(val.ToString(), out value);
        }

        private static bool EqualsOrdinalIgnoreCase(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSupportedMessageType(string type)
        {
            return EqualsOrdinalIgnoreCase(type, MessageTypeEvent)
                || EqualsOrdinalIgnoreCase(type, MessageTypeResponse)
                || EqualsOrdinalIgnoreCase(type, MessageTypeRequest);
        }

        private static bool MatchesCommand(string command, string expected)
        {
            return EqualsOrdinalIgnoreCase(command, expected);
        }

        private static bool TryGetIntValue(Dictionary<string, string> msg, string key, out int value)
        {
            value = 0;
            return msg != null && msg.TryGetValue(key, out var raw) && int.TryParse(raw, out value);
        }

        private static bool TryGetLongValue(Dictionary<string, string> msg, string key, out long value)
        {
            value = 0;
            return msg != null && msg.TryGetValue(key, out var raw) && long.TryParse(raw, out value);
        }
    }
}
