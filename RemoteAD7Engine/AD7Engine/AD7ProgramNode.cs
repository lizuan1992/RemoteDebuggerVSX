using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;

namespace RemoteAD7Engine
{
    internal sealed class AD7ProgramNode : IDebugProgramNode2
    {
        private static readonly string MachineName = Environment.MachineName;

        private readonly Guid _programId;

        public AD7ProgramNode(Guid programId)
        {
            _programId = programId;
        }

        public int Attach_V7(IDebugProgram2 pMDMProgram, IDebugEventCallback2 pCallback, uint dwReason)
        {
            return VSConstants.E_NOTIMPL;
        }

        public int DetachDebugger_V7()
        {
            return VSConstants.E_NOTIMPL;
        }

        public int GetEngineInfo(out string pbstrEngine, out Guid pguidEngine)
        {
            pbstrEngine = EngineConstants.EngineName;
            pguidEngine = new Guid(EngineConstants.EngineGUID);
            return VSConstants.S_OK;
        }

        public int GetHostMachineName_V7(out string pbstrHostMachineName)
        {
            return GetMachine(out pbstrHostMachineName);
        }

        public int GetHostName(enum_GETHOSTNAME_TYPE dwHostNameType, out string pbstrHostName)
        {
            return GetMachine(out pbstrHostName);
        }

        private static int GetMachine(out string name)
        {
            name = MachineName;
            return VSConstants.S_OK;
        }

        public int GetHostPid(AD_PROCESS_ID[] pHostProcessId)
        {
            pHostProcessId[0].ProcessIdType = (uint)enum_AD_PROCESS_ID.AD_PROCESS_ID_GUID;
            pHostProcessId[0].guidProcessId = _programId;
            return VSConstants.S_OK;
        }

        public int GetProgramName(out string pbstrProgramName)
        {
            pbstrProgramName = "Remote Debug Program";
            return VSConstants.S_OK;
        }
    }
}
