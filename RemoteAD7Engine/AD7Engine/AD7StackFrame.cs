using System;
using System.Collections.Generic;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;

namespace RemoteAD7Engine
{
    internal sealed class AD7StackFrame : IDebugStackFrame2, IDebugExpressionContext2
    {
        private static readonly Guid CppLanguageGuid = new Guid("3A12D0B7-1E22-11d3-B34E-00C04F68CD7C");

        private readonly AD7DocumentContext _documentContext;

        private string _functionName;

        private string _lastFile;
        private int _lastLine;

        public AD7Engine Engine { get; }
        public AD7Thread Thread { get; }

        internal int FrameId { get; }

        public int ParseText(
            string pszCode,
            enum_PARSEFLAGS dwFlags,
            uint nRadix,
            out IDebugExpression2 ppExpr,
            out string pbstrError,
            out uint pichError)
        {
            pbstrError = null;
            pichError = 0;
            ppExpr = new AD7Expression(Engine, this, pszCode);
            return VSConstants.S_OK;
        }

        public int EnumProperties(
            enum_DEBUGPROP_INFO_FLAGS dwFields,
            uint nRadix,
            ref Guid guidFilter,
            uint dwTimeout,
            out uint pcelt,
            out IEnumDebugPropertyInfo2 ppEnum)
        {
            var state = Engine?.State;
            var ti = state?.GetOrCreateThreadInfo(Thread.ThreadID);
            if (state == null || ti == null || !ti.IsStopped)
            {
                pcelt = 0;
                ppEnum = null;
                return VSConstants.S_FALSE;
            }

            var frameInfo = ti.Frames.TryGetValue(FrameId, out var fi) ? fi : null;
            if (frameInfo != null && (frameInfo.File != _lastFile || frameInfo.Line != _lastLine))
            {
                // Clear variables for this frame
                frameInfo.VariablesAddr.Clear();
                _lastFile = frameInfo.File;
                _lastLine = frameInfo.Line;
                // Refetch variables
                Engine.SendDebugCommand("get_scope", new Dictionary<string, object> { { "threadId", Thread.ThreadID }, { "frameId", FrameId } }, true);

                Engine.State.SyncManager.WaitingResponse();
            }

            int threadId = Thread.ThreadID;
            int frameId = FrameId;

            if (!state.TryGetRuntimeVariablesAddr(threadId, frameId, out var variablesAddr) || variablesAddr == null || variablesAddr.Count == 0)
            {
                // Already sent above if needed
                pcelt = 0;
                ppEnum = null;
                return VSConstants.S_FALSE;
            }

            var props = new List<DEBUG_PROPERTY_INFO>();

            foreach (var addr in variablesAddr)
            {
                if (RemoteState.MemoryVariables.TryGetValue(addr, out var pv))
                {
                    var prop = ConvertToProperty(pv, null);
                    var dpi = new DEBUG_PROPERTY_INFO[1];
                    ((IDebugProperty2)prop).GetPropertyInfo(dwFields, nRadix, dwTimeout, null, 0, dpi);
                    props.Add(dpi[0]);
                }
            }

            pcelt = (uint)props.Count;
            ppEnum = new AD7PropertyInfoEnum(props.ToArray());
            return VSConstants.S_OK;
        }

        public int GetCodeContext(out IDebugCodeContext2 ppCodeCxt)
        {
            ppCodeCxt = _documentContext;
            return VSConstants.S_OK;
        }

        public int GetDebugProperty(out IDebugProperty2 ppProperty)
        {
            ppProperty = new FrameRootProperty(this);
            return VSConstants.S_OK;
        }

        public int GetDocumentContext(out IDebugDocumentContext2 ppCxt)
        {
            ppCxt = _documentContext;
            return VSConstants.S_OK;
        }

        public int GetExpressionContext(out IDebugExpressionContext2 ppExprCxt)
        {
            ppExprCxt = this;
            return VSConstants.S_OK;
        }

        public int GetInfo(enum_FRAMEINFO_FLAGS dwFieldSpec, uint nRadix, FRAMEINFO[] pFrameInfo)
        {
            pFrameInfo[0] = GetFrameInfo(dwFieldSpec);
            return VSConstants.S_OK;
        }

        public int GetLanguageInfo(ref string pbstrLanguage, ref Guid pguidLanguage)
        {
            pbstrLanguage = pbstrLanguage ?? "C++";
            pguidLanguage = pguidLanguage == Guid.Empty ? CppLanguageGuid : pguidLanguage;

            if (_documentContext != null)
            {
                _documentContext.GetLanguageInfo(ref pbstrLanguage, ref pguidLanguage);
            }

            return VSConstants.S_OK;
        }

        public int GetName(out string pbstrName)
        {
            pbstrName = _functionName ?? "Main";
            return VSConstants.S_OK;
        }

        public int GetPhysicalStackRange(out ulong paddrMin, out ulong paddrMax)
        {
            paddrMin = 0;
            paddrMax = 0;
            return VSConstants.S_OK;
        }

        public int GetThread(out IDebugThread2 ppThread)
        {
            ppThread = Thread;
            return VSConstants.S_OK;
        }

        internal FRAMEINFO GetFrameInfo(enum_FRAMEINFO_FLAGS dwFieldSpec)
        {
            var languageName = "C++";
            var languageGuid = CppLanguageGuid;
            _documentContext?.GetLanguageInfo(ref languageName, ref languageGuid);

            var frameInfo = new FRAMEINFO
            {
                m_dwValidFields = 0
            };

            frameInfo.m_dwValidFields |= enum_FRAMEINFO_FLAGS.FIF_FUNCNAME;
            frameInfo.m_bstrFuncName = _functionName ?? "Main";

            frameInfo.m_dwValidFields |= enum_FRAMEINFO_FLAGS.FIF_LANGUAGE;
            frameInfo.m_bstrLanguage = languageName;

            frameInfo.m_dwValidFields |= enum_FRAMEINFO_FLAGS.FIF_FRAME;
            frameInfo.m_pFrame = this;

            if (_documentContext != null)
            {
                frameInfo.m_dwValidFields |= enum_FRAMEINFO_FLAGS.FIF_DEBUGINFO;
                frameInfo.m_fHasDebugInfo = 1;
            }

            return frameInfo;
        }

        internal AD7StackFrame(AD7Engine engine, AD7Thread thread, int frameId, string functionName, AD7DocumentContext documentContext)
        {
            Engine = engine;
            Thread = thread;
            FrameId = frameId;
            _functionName = functionName;
            _documentContext = documentContext;
        }

        private AD7Property ConvertToProperty(ProtocolVariable pv, AD7Property parent)
        {
            if (pv == null)
            {
                return null;
            }

            var prop = new AD7Property(pv.Addr, Engine, Thread, this) { Parent = parent };
            return prop;
        }
    }

    internal sealed class FrameRootProperty : IDebugProperty2
    {
        private readonly AD7StackFrame _frame;

        public FrameRootProperty(AD7StackFrame frame)
        {
            _frame = frame;
        }

        public int EnumChildren(enum_DEBUGPROP_INFO_FLAGS dwFields, uint dwRadix, ref Guid guidFilter, enum_DBG_ATTRIB_FLAGS dwAttribFilter, string pszNameFilter, uint dwTimeout, out IEnumDebugPropertyInfo2 ppEnum)
        {
            ppEnum = null;
            if (_frame == null)
            {
                return VSConstants.S_FALSE;
            }

            uint count;
            return _frame.EnumProperties(dwFields, dwRadix, ref guidFilter, dwTimeout, out count, out ppEnum);
        }

        public int GetDerivedMostProperty(out IDebugProperty2 ppDerivedMost)
        {
            ppDerivedMost = null;
            return VSConstants.S_OK;
        }

        public int GetExtendedInfo(ref Guid guidExtendedInfo, out object pExtendedInfo)
        {
            pExtendedInfo = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetMemoryBytes(out IDebugMemoryBytes2 ppMemoryBytes)
        {
            ppMemoryBytes = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetMemoryContext(out IDebugMemoryContext2 ppMemory)
        {
            ppMemory = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetParent(out IDebugProperty2 ppParent)
        {
            ppParent = null;
            return VSConstants.S_OK;
        }

        public int GetPropertyInfo(enum_DEBUGPROP_INFO_FLAGS dwFields, uint dwRadix, uint dwTimeout, IDebugReference2[] rgpArgs, uint dwArgCount, DEBUG_PROPERTY_INFO[] pPropertyInfo)
        {
            if (pPropertyInfo == null || pPropertyInfo.Length == 0)
            {
                return VSConstants.E_INVALIDARG;
            }

            var info = new DEBUG_PROPERTY_INFO
            {
                dwFields = enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_FULLNAME | enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_NAME | enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_PROP | enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_ATTRIB,
                bstrFullName = "Locals",
                bstrName = "Locals",
                pProperty = this,
                dwAttrib = enum_DBG_ATTRIB_FLAGS.DBG_ATTRIB_OBJ_IS_EXPANDABLE
            };

            pPropertyInfo[0] = info;
            return VSConstants.S_OK;
        }

        public int GetReference(out IDebugReference2 ppReference)
        {
            ppReference = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetSize(out uint pdwSize)
        {
            pdwSize = 0;
            return VSConstants.E_NOTIMPL;
        }

        public int SetValueAsString(string pszValue, uint dwRadix, uint dwTimeout)
        {
            return VSConstants.E_NOTIMPL;
        }

        public int SetValueAsReference(IDebugReference2[] rgpArgs, uint dwArgCount, IDebugReference2 pValue, uint dwTimeout)
        {
            return VSConstants.E_NOTIMPL;
        }
    }
}
