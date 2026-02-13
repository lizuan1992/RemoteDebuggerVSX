using System.Collections.Generic;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;
using System.Linq;

namespace RemoteAD7Engine
{
    internal sealed class AD7Expression : IDebugExpression2
    {
        private readonly AD7Engine _engine;
        private readonly AD7StackFrame _frame;
        private readonly string _expression;

        public AD7Expression(AD7Engine engine, AD7StackFrame frame, string expression)
        {
            _engine = engine;
            _frame = frame;
            _expression = expression;
        }

        public int Abort()
        {
            return VSConstants.E_NOTIMPL;
        }

        public int EvaluateAsync(enum_EVALFLAGS dwFlags, IDebugEventCallback2 pExprCallback)
        {
            return VSConstants.E_NOTIMPL;
        }

        public int EvaluateSync(
            enum_EVALFLAGS dwFlags,
            uint dwTimeout,
            IDebugEventCallback2 pExprCallback,
            out IDebugProperty2 ppResult)
        {
            var state = _engine.State;
            var threadId = _frame.Thread.ThreadID;
            var frameId = _frame.FrameId;

            // Parse the expression for dot-separated access
            var parts = _expression.Split('.');
            long foundAddr = -1;

            // Check for existing data in Variables
            if (state.TryGetRuntimeVariablesAddr(threadId, frameId, out var runtimeData) && runtimeData != null)
            {
                foundAddr = FindVariableByPath(runtimeData, parts);
                if (foundAddr != -1)
                {
                    var prop = new AD7Property(foundAddr, _engine, _frame.Thread, _frame, _expression);
                    ppResult = prop;
                    return VSConstants.S_OK;
                }
            }

            // Hierarchical request: request each level if not found
            for (int i = 1; i <= parts.Length; i++)
            {
                var prefix = string.Join(".", parts.Take(i));
                if (state.TryGetRuntimeVariablesAddr(threadId, frameId, out runtimeData) && runtimeData != null)
                {
                    var prefixParts = prefix.Split('.');
                    foundAddr = FindVariableByPath(runtimeData, prefixParts);
                    if (foundAddr != -1)
                    {
                        continue; // Already have this level, proceed to next
                    }
                }

                // Request this prefix
                _engine.SendDebugCommand("get_evaluation", new Dictionary<string, object>
                {
                    { "threadId", threadId },
                    { "expression", prefix },
                    { "frameId", frameId }
                }, true);

                // Check the result of the operation
                if (!state.SyncManager.OperationResult)
                {
                    ppResult = null;
                    return VSConstants.S_FALSE;
                }
            }

            // Final check for the full expression
            if (state.TryGetRuntimeVariablesAddr(threadId, frameId, out runtimeData) && runtimeData != null)
            {
                foundAddr = FindVariableByPath(runtimeData, parts);
                if (foundAddr != -1)
                {
                    var prop = new AD7Property(foundAddr, _engine, _frame.Thread, _frame, _expression);
                    ppResult = prop;
                    return VSConstants.S_OK;
                }
            }

            ppResult = null;
            return VSConstants.S_FALSE;
        }

        private long FindVariableByPath(List<long> runtimeData, string[] parts)
        {
            if (parts.Length == 0) return -1;

            // Find the root variable
            foreach (var addr in runtimeData)
            {
                if (RemoteState.MemoryVariables.TryGetValue(addr, out var pv) && pv.Name == parts[0])
                {
                    return FindVariableRecursive(pv, parts, 1);
                }
            }

            return -1;
        }

        private long FindVariableRecursive(ProtocolVariable pv, string[] parts, int index)
        {
            if (index >= parts.Length) return pv.Addr;

            // If this variable is expandable but not yet expanded, expand it
            if (pv.Size != 0 && pv.Elements == null)
            {
                _engine.SendDebugCommand("get_property", new Dictionary<string, object>
                {
                    { "threadId", _frame.Thread.ThreadID },
                    { "frameId", _frame.FrameId },
                    { "addr", pv.Addr },
                    { "typeId", pv.TypeId },
                    { "start", 0 },
                    { "count", 0 }
                }, true);
            }

            if (pv.Elements != null)
            {
                foreach (var childAddr in pv.Elements)
                {
                    if (RemoteState.MemoryVariables.TryGetValue(childAddr, out var childPv) && childPv.Name == parts[index])
                    {
                        return FindVariableRecursive(childPv, parts, index + 1);
                    }
                }
            }

            return -1;
        }
    }
}
