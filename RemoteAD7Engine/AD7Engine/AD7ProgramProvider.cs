using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;

namespace RemoteAD7Engine
{
    [ComVisible(true)]
    [Guid("AD704CA4-6029-48C2-B60E-84E7D6AA4458")]
    internal sealed class AD7ProgramProvider : IDebugProgramProvider2
    {
        public int GetProviderProcessData(enum_PROVIDER_FLAGS Flags, IDebugDefaultPort2 pPort, AD_PROCESS_ID ProcessId, CONST_GUID_ARRAY EngineFilter, PROVIDER_PROCESS_DATA[] pProcess)
        {
            if (pProcess == null || pProcess.Length == 0)
                return VSConstants.E_INVALIDARG;

            pProcess[0] = default;
            return VSConstants.S_OK;
        }

        public int GetProviderProgramNode(enum_PROVIDER_FLAGS Flags, IDebugDefaultPort2 pPort, AD_PROCESS_ID ProcessId, ref Guid guidEngine, ulong programId, out IDebugProgramNode2 ppProgramNode)
        {
            ppProgramNode = new AD7ProgramNode(ProcessId.guidProcessId);
            return VSConstants.S_OK;
        }

        public int SetLocale(ushort wLangID)
        {
            return VSConstants.S_OK;
        }

        public int WatchForProviderEvents(enum_PROVIDER_FLAGS Flags, IDebugDefaultPort2 pPort, AD_PROCESS_ID ProcessId, CONST_GUID_ARRAY EngineFilter, ref Guid guidLaunchingEngine, IDebugPortNotify2 pEventCallback)
        {
            return VSConstants.S_OK;
        }
    }
}
