using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace RemoteAD7Engine
{
    public partial class AD7Engine
    {
        private void HandleEventMessage(string evtName, Dictionary<string, string> msg)
        {
            switch (evtName.ToLowerInvariant())
            {
                case "stopped":
                    HandleStoppedEvent(msg);
                    break;
                case "continued":
                    HandleContinuedEvent(msg);
                    break;
                case "program_exited":
                    HandleProgramExitedEvent();
                    break;
                case "output":
                    HandleOutputEvent(msg);
                    break;
                case "thread_started":
                    HandleThreadStartedEvent(msg);
                    break;
                case "thread_exited":
                    HandleThreadExitedEvent(msg);
                    break;
            }
        }

        private void HandleStoppedEvent(Dictionary<string, string> msg)
        {
            _state.ResetVariableTree();

            int threadId;
            try
            {
                threadId = GetRequiredThreadId(msg);
            }
            catch (Exception ex)
            {
                LogProtocolWarning(ex.Message);
                return;
            }

            var ti = _state.GetOrCreateThreadInfo(threadId);
            ti.IsStopped = true;
            msg.TryGetValue("reason", out var reason);
            ti.StopReason = reason;

            if (ti.Ad7Thread == null)
            {
                Callback?.ThreadStarted(_state.GetOrCreateAd7Thread(threadId));
            }

            msg.TryGetValue("file", out var file);
            msg.TryGetValue("line", out var lineStr);
            int.TryParse(lineStr, out var line);

            ti.CurrFile = NormalizeBreakpointPath(file);
            ti.CurrLine = line;

            var activeThread = _state.TryGetAd7Thread(threadId);

            if (string.Equals(reason, "step", StringComparison.OrdinalIgnoreCase))
            {
                Callback.StepCompleted(activeThread);
                return;
            }

            if (string.Equals(reason, "breakpoint", StringComparison.OrdinalIgnoreCase))
            {
                var key = MakeLocationKey(ti.CurrFile, line);
                if (key != null && _pendingBreakpointsByLocation.TryGetValue(key, out var pending))
                {
                    Callback.BreakpointHit(pending, activeThread);
                    return;
                }
                else
                    Debug.WriteLine($"[RemoteAD7Engine] Breakpoint hit but no pending breakpoint found for {file}:{line}");
            }

            if (string.Equals(reason, "exception", StringComparison.OrdinalIgnoreCase))
            {
                msg.TryGetValue("exceptionName", out var exName);
                msg.TryGetValue("exceptionMessage", out var exMsg);

                Callback.Send(new AD7ExceptionEvent(exName, exMsg), AD7ExceptionEvent.IID, this, activeThread);
                return;
            }

            //Callback.Send(new AD7BreakEvent(), AD7BreakEvent.IID, this, activeThread);
            Callback.StepCompleted(activeThread);
        }

        private void HandleContinuedEvent(Dictionary<string, string> msg)
        {
            if (TryGetIntValue(msg, "threadId", out var tid))
            {
                var ti = _state.GetOrCreateThreadInfo(tid);
                ti.IsStopped = false;
                _state.ResetVariableTreeForThread(tid);

                if (ti.Ad7Thread != null)
                {
                    Callback?.ThreadEnded(ti.Ad7Thread);
                    ti.Ad7Thread = null;
                }

                // If no threads have AD7Thread, clear memory variables
                if (_state.ThreadsInfo.Values.All(t => t.Ad7Thread == null))
                {
                    RemoteState.MemoryVariables.Clear();
                }
            }
        }

        private void HandleProgramExitedEvent()
        {
            if (_localDestroyed)
            {
                return;
            }

            foreach (var ti in _state.ThreadsInfo.Values)
            {
                ti.IsStopped = false;
            }

            _localDestroyed = true;
            Callback?.ProgramDestroyed(this);

            ResetEngineCachesAfterExit(disposeTransport: false);
        }

        private void HandleOutputEvent(Dictionary<string, string> msg)
        {
            msg.TryGetValue("category", out var category);
            msg.TryGetValue("output", out var output);

            var text = output ?? string.Empty;
            if (!string.IsNullOrEmpty(category))
            {
                text = string.Concat("[", category, "] ", text);
            }

            Callback.Send(new AD7OutputStringEvent(text), AD7OutputStringEvent.IID, this, null);
        }

        private void HandleThreadStartedEvent(Dictionary<string, string> msg)
        {
            int threadId;
            try
            {
                threadId = GetRequiredThreadId(msg);
            }
            catch (Exception ex)
            {
                LogProtocolWarning(ex.Message);
                return;
            }

            var ti = _state.GetOrCreateThreadInfo(threadId);
            if (ti.Ad7Thread == null)
            {
                Callback?.ThreadStarted(_state.GetOrCreateAd7Thread(threadId));
            }
        }

        private void HandleThreadExitedEvent(Dictionary<string, string> msg)
        {
            int threadId;
            try
            {
                threadId = GetRequiredThreadId(msg);
            }
            catch (Exception ex)
            {
                LogProtocolWarning(ex.Message);
                return;
            }

            HandleThreadRemoved(threadId);
            Callback?.ThreadEnded(_state.TryGetAd7Thread(threadId));
        }
    }
}
