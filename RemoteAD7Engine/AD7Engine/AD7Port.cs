using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;

namespace RemoteAD7Engine
{
    internal sealed class AD7Port : IDebugDefaultPort2, IDebugPortEx2, IDebugPortNotify2
    {
        private readonly AD7PortSupplier _supplier;
        private readonly IDebugPortRequest2 _request;
        private readonly string _portName;
        private readonly Guid _portId = Guid.NewGuid();

        private readonly AD7Process _process;
        private readonly IDebugProcess2[] _processArray;

        public AD7Port(AD7PortSupplier supplier, IDebugPortRequest2 request)
        {
            _supplier = supplier ?? throw new ArgumentNullException(nameof(supplier));
            _request = request ?? throw new ArgumentNullException(nameof(request));

            request.GetPortName(out var name);
            _portName = string.IsNullOrWhiteSpace(name) ? "Remote Port" : name;

            _process = new AD7Process(this);
            _processArray = new IDebugProcess2[] { _process };
        }

        public int EnumProcesses(out IEnumDebugProcesses2 ppEnum)
        {
            ppEnum = new AD7ProcessEnum(_processArray);
            return VSConstants.S_OK;
        }

        public int GetPortId(out Guid pguidPort)
        {
            pguidPort = _portId;
            return VSConstants.S_OK;
        }

        public int GetPortName(out string pbstrName)
        {
            pbstrName = _portName;
            return VSConstants.S_OK;
        }

        public int GetPortRequest(out IDebugPortRequest2 ppRequest)
        {
            ppRequest = _request;
            return VSConstants.S_OK;
        }

        public int GetPortSupplier(out IDebugPortSupplier2 ppSupplier)
        {
            ppSupplier = _supplier;
            return VSConstants.S_OK;
        }

        public int GetProcess(AD_PROCESS_ID ProcessId, out IDebugProcess2 ppProcess)
        {
            ppProcess = _process;
            return VSConstants.S_OK;
        }

        public int GetPortNotify(out IDebugPortNotify2 ppPortNotify)
        {
            ppPortNotify = this;
            return VSConstants.S_OK;
        }

        public int GetServer(out IDebugCoreServer3 ppServer)
        {
            ppServer = null;
            return VSConstants.E_NOTIMPL;
        }

        public int QueryIsLocal()
        {
            return VSConstants.S_FALSE;
        }

        int IDebugPortEx2.LaunchSuspended(
            string pszExe,
            string pszArgs,
            string pszDir,
            string bstrEnv,
            uint hStdInput,
            uint hStdOutput,
            uint hStdError,
            out IDebugProcess2 ppPortProcess)
        {
            ppPortProcess = _process;
            return VSConstants.S_OK;
        }

        int IDebugPortEx2.ResumeProcess(IDebugProcess2 pPortProcess)
        {
            return VSConstants.S_OK;
        }

        int IDebugPortEx2.CanTerminateProcess(IDebugProcess2 pPortProcess)
        {
            return VSConstants.S_FALSE;
        }

        int IDebugPortEx2.TerminateProcess(IDebugProcess2 pPortProcess)
        {
            return VSConstants.E_NOTIMPL;
        }

        int IDebugPortEx2.GetPortProcessId(out uint pdwProcessId)
        {
            pdwProcessId = 0;
            return VSConstants.S_OK;
        }

        int IDebugPortEx2.GetProgram(IDebugProgramNode2 pProgramNode, out IDebugProgram2 ppProgram)
        {
            ppProgram = null;
            return VSConstants.E_NOTIMPL;
        }

        int IDebugPortNotify2.AddProgramNode(IDebugProgramNode2 pProgramNode)
        {
            return VSConstants.S_OK;
        }

        int IDebugPortNotify2.RemoveProgramNode(IDebugProgramNode2 pProgramNode)
        {
            return VSConstants.S_OK;
        }
    }
}
