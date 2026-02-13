using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;

namespace RemoteAD7Engine
{
    internal sealed class AD7Process : IDebugProcessEx2, IDebugProcess2
    {
        private const string RemoteProcessDisplayName = "Remote Process";

        private readonly Guid _processId = Guid.NewGuid();
        private readonly IDebugPort2 _port;

        public AD7Process(IDebugPort2 port)
        {
            _port = port ?? throw new ArgumentNullException(nameof(port));
        }

        public int Attach(IDebugEventCallback2 pCallback, Guid[] rgguidSpecificEngines, uint celtSpecificEngines, int[] rghrEngineAttach)
        {
            return VSConstants.E_NOTIMPL;
        }

        public int CanDetach()
        {
            return VSConstants.S_FALSE;
        }

        public int CauseBreak()
        {
            return VSConstants.E_NOTIMPL;
        }

        public int Detach()
        {
            return VSConstants.E_NOTIMPL;
        }

        public int EnumPrograms(out IEnumDebugPrograms2 ppEnum)
        {
            ppEnum = null;
            return VSConstants.E_NOTIMPL;
        }

        public int EnumThreads(out IEnumDebugThreads2 ppEnum)
        {
            ppEnum = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetAttachedSessionName(out string pbstrSessionName)
        {
            pbstrSessionName = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetInfo(enum_PROCESS_INFO_FIELDS Fields, PROCESS_INFO[] pProcessInfo)
        {
            if (pProcessInfo == null || pProcessInfo.Length == 0)
            {
                return VSConstants.E_INVALIDARG;
            }

            pProcessInfo[0].Fields = Fields;
            pProcessInfo[0].bstrTitle = RemoteProcessDisplayName;
            pProcessInfo[0].bstrBaseName = RemoteProcessDisplayName;
            pProcessInfo[0].bstrFileName = RemoteProcessDisplayName;
            return VSConstants.S_OK;
        }

        public int GetName(enum_GETNAME_TYPE gnType, out string pbstrName)
        {
            pbstrName = RemoteProcessDisplayName;
            return VSConstants.S_OK;
        }

        public int GetPhysicalProcessId(AD_PROCESS_ID[] pProcessId)
        {
            if (pProcessId == null || pProcessId.Length == 0)
            {
                return VSConstants.E_INVALIDARG;
            }

            pProcessId[0].guidProcessId = _processId;
            pProcessId[0].ProcessIdType = (uint)enum_AD_PROCESS_ID.AD_PROCESS_ID_GUID;
            return VSConstants.S_OK;
        }

        public int GetPort(out IDebugPort2 ppPort)
        {
            ppPort = _port;
            return VSConstants.S_OK;
        }

        public int GetProcessId(out Guid pguidProcessId)
        {
            pguidProcessId = _processId;
            return VSConstants.S_OK;
        }

        public int GetServer(out IDebugCoreServer2 ppServer)
        {
            ppServer = null;
            return VSConstants.E_NOTIMPL;
        }

        public int Terminate()
        {
            return VSConstants.S_OK;
        }

        int IDebugProcessEx2.AddImplicitProgramNodes(ref Guid guidLaunchingEngine, Guid[] rgguidSpecificEngines, uint celtSpecificEngines)
        {
            return VSConstants.S_OK;
        }

        int IDebugProcessEx2.Attach(IDebugSession2 pSession)
        {
            return VSConstants.S_OK;
        }

        int IDebugProcessEx2.Detach(IDebugSession2 pSession)
        {
            return VSConstants.E_NOTIMPL;
        }
    }
}
