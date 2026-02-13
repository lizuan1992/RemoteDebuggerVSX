// This file implements response handling according to RemoteProtocol.md documentation.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json.Linq;

namespace RemoteAD7Engine
{
    public partial class AD7Engine
    {
        internal const string ProtocolPrefix = "[Protocol]";
        private const string InvalidResponsePrefix = "Invalid {0} response:";

        private void HandleResponseMessage(string command, Dictionary<string, string> msg, Dictionary<string, object> raw)
        {
            if (MatchesCommand(command, "get_evaluation"))
            {
                HandleGetEvaluationResponse(msg, raw);
            }
            else if (MatchesCommand(command, "get_stack"))
            {
                HandleGetStackResponse(msg, raw);
            }
            else if (MatchesCommand(command, "get_scope"))
            {
                HandleGetScopeResponse(msg, raw);
            }
            else if (MatchesCommand(command, "get_property"))
            {
                HandleGetPropertyResponse(msg, raw);
            }
            else if (MatchesCommand(command, "set_variable"))
            {
                HandleSetVariableResponse(msg, raw);
            }
            else if (MatchesCommand(command, "get_threads"))
            {
                HandleGetThreadsResponse(msg, raw);
            }
            else if (MatchesCommand(command, "start"))
            {
                // Send debug output event to inform user that start command responded successfully
                var outputEvent = new AD7OutputStringEvent("The start command responded successfully\n");
                this.Callback.Send(outputEvent, AD7OutputStringEvent.IID, this, null);
            }
            else if (MatchesCommand(command, "pause"))
            {
                HandlePauseResponse(msg);
            }
        }

        private void HandlePauseResponse(Dictionary<string, string> msg)
        {
            if (msg.TryGetValue("success", out var s) && string.Equals(s, "false", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine("[RemoteAD7Engine] pause rejected by remote;");
                LogProtocolWarning($"{ProtocolPrefix} pause response success=false; treating pause as unsupported for this session");
            }
        }

        private void HandleGetThreadsResponse(Dictionary<string, string> msg, Dictionary<string, object> raw)
        {
            IList threads = null;
            var success = IsSuccess(msg);
            if (success && !TryGetRawList(raw, "threads", out threads))
            {
                FailUnsupportedSchema("get_threads", $"{ProtocolPrefix} Invalid get_threads response: missing threads[];");
                return;
            }

            if (success && threads != null)
            {
                var seen = new HashSet<int>();

                for (var i = 0; i < threads.Count; i++)
                {
                    if (!TryGetRawObject(threads, i, out var threadObj))
                    {
                        FailUnsupportedSchema("get_threads", $"{ProtocolPrefix} Invalid get_threads response: thread entry is not an object;");
                        return;
                    }

                    if (!ValidateThreadObject(threadObj, "get_threads", out var tidVal, out var nameVal))
                    {
                        return;
                    }

                    _state.SetThreadName(tidVal, nameVal);
                    seen.Add(tidVal);
                }

                // Remove any threads not in the latest list
                var toRemove = new List<int>();
                foreach (var existing in _state.ThreadsInfo.Keys)
                {
                    if (!seen.Contains(existing))
                    {
                        toRemove.Add(existing);
                    }
                }
                foreach (var tid in toRemove)
                {
                    HandleThreadRemoved(tid);
                }
            }
        }

        private void HandleGetStackResponse(Dictionary<string, string> msg, Dictionary<string, object> raw)
        {
            int tid;
            try
            {
                tid = GetRequiredThreadId(msg);
            }
            catch (Exception ex)
            {
                LogProtocolWarning(ex.Message);
                return;
            }

            IList frames = null;
            var success = IsSuccess(msg);
            if (success
                && (!TryGetRawList(raw, "frames", out frames)
                    || !TryGetIntValue(msg, "threadId", out _)))
            {
                FailUnsupportedSchema("get_stack", $"{ProtocolPrefix} Invalid get_stack response: missing frames[];");
                return;
            }

            if (success && frames != null)
            {
                var frameList = new List<FrameInfo>();
                for (var i = 0; i < frames.Count; i++)
                {
                    if (!TryGetRawObject(frames, i, out var frameObj))
                    {
                        FailUnsupportedSchema("get_stack", $"{ProtocolPrefix} Invalid get_stack response: frame entry is not an object;");
                        return;
                    }

                    if (!ValidateFrameObject(frameObj, "get_stack", out var frame))
                    {
                        return;
                    }

                    frameList.Add(frame);
                }

                _state.UpdateFramesForThread(tid, frameList);
            }
        }

        private void HandleGetScopeResponse(Dictionary<string, string> msg, Dictionary<string, object> raw)
        {
            if (!TryGetThreadFrameIds(msg, out var tidStr, out var fidStr))
            {
                FailUnsupportedSchema("get_scope", $"{ProtocolPrefix} Invalid get_scope response: missing/invalid threadId/frameId;");
                return;
            }

            IList variables = null;
            var success = IsSuccess(msg);
            if (success && !TryGetRawList(raw, "variables", out variables))
            {
                FailUnsupportedSchema("get_scope", $"{ProtocolPrefix} Invalid get_scope response: missing variables[];");
                return;
            }

            if (success && variables != null)
            {
                // Build locals using latest schema.
                if (int.TryParse(tidStr, out var tid) && int.TryParse(fidStr, out var fid))
                {
                    var variableAddrs = new List<long>();
                    for (var i = 0; i < variables.Count; i++)
                    {
                        if (TryGetRawObject(variables, i, out var varObj))
                        {
                            var addr = ProtocolVariable.ParseAndStoreVariable(JObject.FromObject(varObj), "get_scope");
                            if (addr != 0)
                                variableAddrs.Add(addr);
                        }
                    }

                    _state.UpdateFrameVariablesAddr(tid, fid, variableAddrs);
                }
            }
        }

        private void HandleGetPropertyResponse(Dictionary<string, string> msg, Dictionary<string, object> raw)
        {
            if (!TryGetThreadFrameIds(msg, out var tidStr, out var fidStr))
            {
                FailUnsupportedSchema("get_property", $"{ProtocolPrefix} Invalid get_property response: missing/invalid threadId/frameId;");
                return;
            }

            msg.TryGetValue("addr", out var addrStr);
            msg.TryGetValue("typeId", out var typeStr);

            IList properties = null;
            var success = IsSuccess(msg);
            if (success
                && (!TryGetRawList(raw, "properties", out properties)
                    || string.IsNullOrEmpty(addrStr)
                    || !long.TryParse(addrStr, out _)
                    || string.IsNullOrEmpty(typeStr)
                    || !int.TryParse(typeStr, out _)))
            {
                FailUnsupportedSchema("get_property", $"{ProtocolPrefix} Invalid get_property response: requires threadId, frameId, varAddr, varTypeId and properties[];");
                return;
            }

            if (success && properties != null)
            {
                for (var i = 0; i < properties.Count; i++)
                {
                    if (!TryGetRawObject(properties, i, out var propObj))
                    {
                        FailUnsupportedSchema("get_property", $"{ProtocolPrefix} Invalid get_property response: property entry is not an object;");
                        return;
                    }
                }

                if (int.TryParse(tidStr, out var tid)
                    && int.TryParse(fidStr, out var fid)
                    && long.TryParse(addrStr, out var addrVal)
                    && int.TryParse(typeStr, out var typeVal))
                {
                    // Update parent variable size if provided
                    if (msg.TryGetValue("size", out var sizeStr))
                    {
                        if (int.TryParse(sizeStr, out var sizeVal) && RemoteState.MemoryVariables.TryGetValue(addrVal, out var parentPv))
                        {
                            parentPv.Size = sizeVal;
                        }
                    }

                    var childAddrs = new List<long>();

                    for (var i = 0; i < properties.Count; i++)
                    {
                        if (TryGetRawObject(properties, i, out var propObj))
                        {
                            var addr = ProtocolVariable.ParseAndStoreVariable(JObject.FromObject(propObj), "get_property");
                            if (addr != 0)
                                childAddrs.Add(addr);
                        }
                    }

                    _state.UpdateVariableChildrenAddr(addrVal, childAddrs);
                }
            }
        }

        private void HandleGetEvaluationResponse(Dictionary<string, string> msg, Dictionary<string, object> raw)
        {
            var threadIdParsed = TryGetIntValue(msg, "threadId", out var threadId);
            var frameIdParsed = TryGetIntValue(msg, "frameId", out var frameId);
            msg.TryGetValue("expression", out var expression);

            var success = IsSuccess(msg);
            object resultValue = null;
            if (success
                && (!raw.TryGetValue("result", out resultValue)
                    || (resultValue != null && !(resultValue is Dictionary<string, object>) && !(resultValue is JObject))
                    || !threadIdParsed
                    || !frameIdParsed
                    || string.IsNullOrEmpty(expression)))
            {
                _state.SyncManager.OperationResult = false;
                FailUnsupportedSchema("get_evaluation", $"{ProtocolPrefix} Invalid get_evaluation response: missing result/threadId/frameId/expression;");
                return;
            }

            if (success)
            {
                Dictionary<string, object> resultObj = null;
                if (resultValue is JObject jObj)
                {
                    resultObj = jObj.ToObject<Dictionary<string, object>>();
                }
                else
                {
                    resultObj = resultValue as Dictionary<string, object>;
                }

                if (resultObj == null)
                {
                    _state.SyncManager.OperationResult = false;
                    FailUnsupportedSchema("get_evaluation", $"{ProtocolPrefix} Invalid get_evaluation response: result is not an object;");
                    return;
                }

                var addr = ProtocolVariable.ParseAndStoreVariable(JObject.FromObject(resultObj), "get_evaluation");
                if (addr != 0)
                    _state.AddVariableToFrame(threadId, frameId, addr);
            }

            _state.SyncManager.OperationResult = success;
        }

        private void HandleSetVariableResponse(Dictionary<string, string> msg, Dictionary<string, object> raw)
        {
            var success = IsSuccess(msg);

            if (success
                && (!TryGetIntValue(msg, "threadId", out _)
                    || !TryGetIntValue(msg, "frameId", out _)
                    || !msg.TryGetValue("addr", out var varAddrStr)
                    || string.IsNullOrEmpty(varAddrStr)
                    || !msg.TryGetValue("typeId", out var varTypeIdStr)
                    || string.IsNullOrEmpty(varTypeIdStr)))
            {
                _state.SyncManager.OperationResult = false;
                FailUnsupportedSchema("set_variable", $"{ProtocolPrefix} Invalid set_variable response: requires threadId, frameId, varAddr, varTypeId;");
                return;
            }

            _state.SyncManager.OperationResult = success;
        }

        private static bool TryGetThreadFrameIds(Dictionary<string, string> msg, out string tidStr, out string fidStr)
        {
            msg.TryGetValue("threadId", out tidStr);
            msg.TryGetValue("frameId", out fidStr);
            return !string.IsNullOrEmpty(tidStr) && int.TryParse(tidStr, out _) && !string.IsNullOrEmpty(fidStr) && int.TryParse(fidStr, out _);
        }

        private bool ValidateFrameObject(Dictionary<string, object> frameObj, string command, out FrameInfo frame)
        {
            frame = null;

            if (!HasRequiredInt(frameObj, "id", out var idVal))
            {
                FailUnsupportedSchema(command, $"{ProtocolPrefix} Invalid {command} response: frame missing/invalid id;");
                return false;
            }

            if (!HasRequiredString(frameObj, "name"))
            {
                FailUnsupportedSchema(command, $"{ProtocolPrefix} Invalid {command} response: frame missing name;");
                return false;
            }

            if (!HasRequiredString(frameObj, "file"))
            {
                FailUnsupportedSchema(command, $"{ProtocolPrefix} Invalid {command} response: frame missing file;");
                return false;
            }

            if (!HasRequiredInt(frameObj, "line", out var lineParsed) || lineParsed <= 0)
            {
                FailUnsupportedSchema(command, $"{ProtocolPrefix} Invalid {command} response: frame missing/invalid line;");
                return false;
            }

            var name = frameObj["name"].ToString();
            var file = AD7Engine.NormalizeBreakpointPath(frameObj["file"].ToString());
            frame = new FrameInfo
            {
                Id = idVal,
                Name = name,
                File = file,
                Line = lineParsed
            };

            return true;
        }

        private bool ValidateThreadObject(Dictionary<string, object> threadObj, string command, out int tid, out string name)
        {
            tid = 0;
            name = null;

            if (!HasRequiredInt(threadObj, "id", out tid))
            {
                FailUnsupportedSchema(command, $"{ProtocolPrefix} Invalid {command} response: thread missing/invalid id;");
                return false;
            }

            if (!HasRequiredString(threadObj, "name"))
            {
                FailUnsupportedSchema(command, $"{ProtocolPrefix} Invalid {command} response: thread missing name;");
                return false;
            }

            name = threadObj["name"].ToString();
            return true;
        }
    }
}
