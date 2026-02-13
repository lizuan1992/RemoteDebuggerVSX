using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;

namespace RemoteAD7Engine
{
    internal sealed class AD7PendingBreakPoint : IDebugPendingBreakpoint2
    {
        private static readonly TimeSpan EnableDebounceInterval = TimeSpan.FromMilliseconds(200);

        private readonly AD7Engine _engine;
        private readonly IDebugBreakpointRequest2 _breakpointRequest;
        private readonly BP_REQUEST_INFO _breakpointRequestInfo;

        private AD7BoundBreakpoint _boundBreakpoint;
        private DateTime _lastEnableToggleUtc;
        private bool _permanentlyDisabled;
        private string _disableReason;

        public enum_BP_STATE State { get; private set; }

        public AD7PendingBreakPoint(AD7Engine engine, IDebugBreakpointRequest2 breakpointRequest)
        {
            _engine = engine;
            _breakpointRequest = breakpointRequest;

            State = enum_BP_STATE.BPS_ENABLED;

            if (breakpointRequest != null)
            {
                var requestInfo = new BP_REQUEST_INFO[1];
                breakpointRequest.GetRequestInfo(enum_BPREQI_FIELDS.BPREQI_BPLOCATION, requestInfo);
                _breakpointRequestInfo = requestInfo[0];
            }
        }

        internal void MarkPermanentlyDisabled(string reason = null)
        {
            _permanentlyDisabled = true;
            _disableReason = string.IsNullOrWhiteSpace(reason) ? "Breakpoint location is not executable." : reason;
            UpdateEnabledState(false);
        }

        internal bool IsPermanentlyDisabled
        {
            get { return _permanentlyDisabled; }
        }

        internal string DisableReason
        {
            get { return _disableReason; }
        }

        internal void UpdateEnabledState(bool enabled)
        {
            State = enabled ? enum_BP_STATE.BPS_ENABLED : enum_BP_STATE.BPS_DISABLED;
        }

        private AD7ErrorBreakpoint CreateErrorBreakpoint()
        {
            return new AD7ErrorBreakpoint(_engine, this, _disableReason);
        }

        internal void NotifyDisabled()
        {
            UpdateEnabledState(false);

            try
            {
                _engine?.NotifyVsxBreakpointChanged("enabled_changed", this, false);
            }
            catch
            {
            }

            try
            {
                _engine?.Callback?.ErrorBreakpoint(CreateErrorBreakpoint());
            }
            catch
            {
            }
        }

        internal void AttachBoundBreakpoint(AD7BoundBreakpoint bound)
        {
            _boundBreakpoint = bound;
        }

        public int Bind()
        {
            return VSConstants.S_OK;
        }

        public int CanBind(out IEnumDebugErrorBreakpoints2 ppErrorEnum)
        {
            ppErrorEnum = null;
            return VSConstants.E_NOTIMPL;
        }

        public int Delete()
        {
            _engine?.NotifyVsxBreakpointChanged("removed", this);
            return VSConstants.S_OK;
        }

        public int Enable(int fEnable)
        {
            var nowUtc = DateTime.UtcNow;
            var elapsed = nowUtc - _lastEnableToggleUtc;
            if (elapsed < EnableDebounceInterval)
            {
                return VSConstants.S_OK;
            }
            _lastEnableToggleUtc = nowUtc;

            if (_permanentlyDisabled)
            {
                NotifyDisabled();
                return VSConstants.S_OK;
            }

            var newEnabled = fEnable != 0;
            UpdateEnabledState(newEnabled);

            try
            {
                _engine?.NotifyVsxBreakpointChanged("enabled_changed", this, newEnabled);
            }
            catch
            {
            }

            return VSConstants.S_OK;
        }

        public int EnumBoundBreakpoints(out IEnumDebugBoundBreakpoints2 ppEnum)
        {
            var bound = _boundBreakpoint != null
                ? new IDebugBoundBreakpoint2[] { _boundBreakpoint }
                : Array.Empty<IDebugBoundBreakpoint2>();

            ppEnum = new AD7BoundBreakpointsEnum(bound);
            return VSConstants.S_OK;
        }

        public int EnumErrorBreakpoints(enum_BP_ERROR_TYPE bpErrorType, out IEnumDebugErrorBreakpoints2 ppEnum)
        {
            var errors = _permanentlyDisabled
                ? new IDebugErrorBreakpoint2[] { CreateErrorBreakpoint() }
                : Array.Empty<IDebugErrorBreakpoint2>();

            ppEnum = new AD7ErrorBreakpointsEnum(errors);
            return VSConstants.S_OK;
        }

        public int GetBreakpointRequest(out IDebugBreakpointRequest2 ppBPRequest)
        {
            ppBPRequest = _breakpointRequest;
            return ppBPRequest == null ? VSConstants.E_NOTIMPL : VSConstants.S_OK;
        }

        public int GetState(PENDING_BP_STATE_INFO[] pState)
        {
            pState[0].state = (enum_PENDING_BP_STATE)State;
            return VSConstants.S_OK;
        }

        public int SetCondition(BP_CONDITION bpCondition)
        {
            return VSConstants.S_OK;
        }

        public int SetPassCount(BP_PASSCOUNT bpPassCount)
        {
            return VSConstants.E_NOTIMPL;
        }

        public int Virtualize(int fVirtualize)
        {
            return VSConstants.S_OK;
        }

        internal string TryGetLocationKey()
        {
            return TryGetLocation(out var file, out var line)
                ? AD7Engine.MakeLocationKey(file, line)
                : null;
        }

        internal bool TryGetLocation(out string file, out int line)
        {
            file = null;
            line = 0;

            try
            {
                if (_breakpointRequestInfo.bpLocation.unionmember2 == IntPtr.Zero)
                {
                    return false;
                }

                if (!(Marshal.GetObjectForIUnknown(_breakpointRequestInfo.bpLocation.unionmember2) is IDebugDocumentPosition2 docPosition))
                {
                    return false;
                }

                docPosition.GetFileName(out var documentName);

                var startPosition = new TEXT_POSITION[1];
                var endPosition = new TEXT_POSITION[1];
                docPosition.GetRange(startPosition, endPosition);

                file = documentName;
                line = (int)startPosition[0].dwLine + 1;

                return !string.IsNullOrEmpty(file) && line > 0;
            }
            catch
            {
                file = null;
                line = 0;
                return false;
            }
        }
    }
}
