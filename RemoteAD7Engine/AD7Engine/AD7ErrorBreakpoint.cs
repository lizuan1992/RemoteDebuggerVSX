using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;

namespace RemoteAD7Engine
{
    internal sealed class AD7ErrorBreakpoint : IDebugErrorBreakpoint2, IDebugErrorBreakpointResolution2
    {
        private readonly AD7Engine _engine;
        private readonly AD7PendingBreakPoint _pendingBreakpoint;
        private readonly string _message;

        public AD7ErrorBreakpoint(AD7Engine engine, AD7PendingBreakPoint pendingBreakpoint, string message = null)
        {
            _engine = engine;
            _pendingBreakpoint = pendingBreakpoint;
            _message = message;
        }

        public int GetBreakpointResolution(out IDebugErrorBreakpointResolution2 ppBPResolution)
        {
            ppBPResolution = this;
            return VSConstants.S_OK;
        }

        public int GetPendingBreakpoint(out IDebugPendingBreakpoint2 ppPendingBreakpoint)
        {
            ppPendingBreakpoint = _pendingBreakpoint;
            return VSConstants.S_OK;
        }

        public int GetBreakpointType(enum_BP_TYPE[] pBPType)
        {
            pBPType[0] = enum_BP_TYPE.BPT_CODE;
            return VSConstants.S_OK;
        }

        public int GetResolutionInfo(enum_BPERESI_FIELDS dwFields, BP_ERROR_RESOLUTION_INFO[] pBPResolutionInfo)
        {
            if ((dwFields & enum_BPERESI_FIELDS.BPERESI_PROGRAM) == enum_BPERESI_FIELDS.BPERESI_PROGRAM)
            {
                pBPResolutionInfo[0].dwFields |= enum_BPERESI_FIELDS.BPERESI_PROGRAM;
                pBPResolutionInfo[0].pProgram = _engine;
            }

            if ((dwFields & enum_BPERESI_FIELDS.BPERESI_TYPE) == enum_BPERESI_FIELDS.BPERESI_TYPE)
            {
                pBPResolutionInfo[0].dwFields |= enum_BPERESI_FIELDS.BPERESI_TYPE;
                pBPResolutionInfo[0].dwType = enum_BP_ERROR_TYPE.BPET_TYPE_WARNING;
            }

            if ((dwFields & enum_BPERESI_FIELDS.BPERESI_MESSAGE) == enum_BPERESI_FIELDS.BPERESI_MESSAGE)
            {
                pBPResolutionInfo[0].dwFields |= enum_BPERESI_FIELDS.BPERESI_MESSAGE;
                pBPResolutionInfo[0].bstrMessage = string.IsNullOrEmpty(_message) ? "Current code is not loaded yet" : _message;
            }

            return VSConstants.S_OK;
        }
    }
}
