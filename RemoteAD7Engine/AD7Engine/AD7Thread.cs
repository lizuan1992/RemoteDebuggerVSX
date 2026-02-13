using System;
using System.Collections.Generic;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;

namespace RemoteAD7Engine
{
    internal sealed class AD7Thread : IDebugThread2
    {
        private readonly AD7Engine _engine;
        private readonly int _threadId;

        private string _threadName = "Remote Thread";
        private string _lastFile;
        private int _lastLine;

        public int ThreadID
        {
            get { return _threadId; }
        }

        public AD7Thread(AD7Engine engine, int threadId)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _threadId = threadId;

            var state = engine.State;
            _threadName = state.GetOrCreateThreadInfo(_threadId).Name ?? _threadName;
        }

        public int CanSetNextStatement(IDebugStackFrame2 pStackFrame, IDebugCodeContext2 pCodeContext)
        {
            return VSConstants.S_FALSE;
        }

        public int EnumFrameInfo(enum_FRAMEINFO_FLAGS dwFieldSpec, uint nRadix, out IEnumDebugFrameInfo2 ppEnum)
        {
            var ti = _engine.State.GetOrCreateThreadInfo(_threadId);
            if (ti.CurrFile != _lastFile || ti.CurrLine != _lastLine)
            {
                ti.Frames.Clear();
                _lastFile = ti.CurrFile;
                _lastLine = ti.CurrLine;

                if (ti.IsStopped)
                {
                    _engine.SendDebugCommand("get_stack", new Dictionary<string, object> { { "threadId", _threadId } }, true);
                }
                else
                {
                    ppEnum = null;
                    return VSConstants.S_FALSE;
                }
            }

            // Rebuild frames after stack completes to reflect latest data
            var state = _engine.State;
            var list = new List<AD7StackFrame>();

            foreach (var fs in state.GetOrCreateThreadInfo(_threadId).Frames.Values)
            {
                // Remote sends 1-based line numbers; AD7DocumentContext expects 0-based
                var docContext = new AD7DocumentContext(fs.File, fs.Line, -1);
                var frame = new AD7StackFrame(_engine, this, fs.Id, string.IsNullOrEmpty(fs.Name) ? string.Empty : fs.Name, docContext);
                list.Add(frame);
            }

            var framesLocal = list.ToArray();

            if (framesLocal != null && framesLocal.Length > 0)
            {
                var info = new FRAMEINFO[framesLocal.Length];
                for (var i = 0; i < info.Length; i++)
                {
                    info[i] = framesLocal[i].GetFrameInfo(dwFieldSpec);
                }

                ppEnum = new AD7FrameInfoEnum(info);
                return VSConstants.S_OK;
            }

            ppEnum = null;
            return VSConstants.S_FALSE;
        }

        public int GetLogicalThread(IDebugStackFrame2 pStackFrame, out IDebugLogicalThread2 ppLogicalThread)
        {
            ppLogicalThread = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetName(out string pbstrName)
        {
            pbstrName = _threadName;
            return VSConstants.S_OK;
        }

        public int GetProgram(out IDebugProgram2 ppProgram)
        {
            ppProgram = _engine;
            return VSConstants.S_OK;
        }

        public int GetThreadId(out uint pdwThreadId)
        {
            pdwThreadId = (uint)_threadId;
            return VSConstants.S_OK;
        }

        public int GetThreadProperties(enum_THREADPROPERTY_FIELDS dwFields, THREADPROPERTIES[] ptp)
        {
            if (ptp == null || ptp.Length == 0)
            {
                return VSConstants.E_INVALIDARG;
            }

            var tp = ptp[0];
            tp.dwFields = 0;

            if ((dwFields & enum_THREADPROPERTY_FIELDS.TPF_ID) != 0)
            {
                tp.dwFields |= enum_THREADPROPERTY_FIELDS.TPF_ID;
                tp.dwThreadId = (uint)_threadId;
            }

            if ((dwFields & enum_THREADPROPERTY_FIELDS.TPF_NAME) != 0)
            {
                tp.dwFields |= enum_THREADPROPERTY_FIELDS.TPF_NAME;
                tp.bstrName = _threadName;
            }

            ptp[0] = tp;
            return VSConstants.S_OK;
        }

        public int Resume(out uint pdwSuspendCount)
        {
            pdwSuspendCount = 0;
            return VSConstants.S_FALSE;
        }

        public int SetNextStatement(IDebugStackFrame2 pStackFrame, IDebugCodeContext2 pCodeContext)
        {
            return VSConstants.S_FALSE;
        }

        public int SetThreadName(string pszName)
        {
            if (!string.IsNullOrWhiteSpace(pszName))
            {
                _threadName = pszName;
            }

            return VSConstants.S_OK;
        }

        public int Suspend(out uint pdwSuspendCount)
        {
            pdwSuspendCount = 0;
            return VSConstants.S_FALSE;
        }
    }
}
