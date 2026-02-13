using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;

namespace RemoteAD7Engine
{
    public partial class AD7Engine
    {
        public int Attach(IDebugEventCallback2 pCallback)
        {
            if (_callback == null && pCallback != null)
            {
                _callback = new EngineCallback(this, pCallback);
            }

            return VSConstants.S_OK;
        }

        public int CanDetach()
        {
            return VSConstants.S_OK;
        }

        public int CauseBreak()
        {
            //SendDebugCommand("pause");
            return VSConstants.E_NOTIMPL;
        }

        public int Continue(IDebugThread2 pThread)
        {
            return VSConstants.E_NOTIMPL;
        }

        public int Detach()
        {
            return VSConstants.E_NOTIMPL;
        }

        public int EnumCodeContexts(IDebugDocumentPosition2 pDocPos, out IEnumDebugCodeContexts2 ppEnum)
        {
            ppEnum = null;
            return VSConstants.E_NOTIMPL;
        }

        public int EnumCodePaths(string pszHint, IDebugCodeContext2 pStart, IDebugStackFrame2 pFrame, int fSource, out IEnumCodePaths2 ppEnum, out IDebugCodeContext2 ppSafety)
        {
            ppEnum = null;
            ppSafety = null;
            return VSConstants.E_NOTIMPL;
        }

        public int EnumModules(out IEnumDebugModules2 ppEnum)
        {
            ppEnum = null;
            return VSConstants.E_NOTIMPL;
        }

        public int EnumThreads(out IEnumDebugThreads2 ppEnum)
        {
            ppEnum = null;
            return VSConstants.E_NOTIMPL;
        }

        public int Execute()
        {
            return VSConstants.E_NOTIMPL;
        }

        public int ExecuteOnThread(IDebugThread2 pThread)
        {
            foreach (var ti in _state.ThreadsInfo.Values)
            {
                if (ti.IsStopped && ti.Ad7Thread != null)
                {
                    SendDebugCommand("continue", new Dictionary<string, object> { { "threadId", ti.Id } });
                }
            }

            return VSConstants.S_OK;
        }

        public int GetDebugProperty(out IDebugProperty2 ppProperty)
        {
            ppProperty = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetDisassemblyStream(enum_DISASSEMBLY_STREAM_SCOPE dwScope, IDebugCodeContext2 pCodeContext, out IDebugDisassemblyStream2 ppDisassemblyStream)
        {
            ppDisassemblyStream = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetENCUpdate(out object ppUpdate)
        {
            ppUpdate = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetEngineInfo(out string pbstrEngine, out Guid pguidEngine)
        {
            pbstrEngine = EngineConstants.EngineName;
            pguidEngine = new Guid(EngineConstants.EngineGUID);
            return VSConstants.S_OK;
        }

        public int GetMemoryBytes(out IDebugMemoryBytes2 ppMemoryBytes)
        {
            ppMemoryBytes = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetName(out string pbstrName)
        {
            pbstrName = "Remote Debug Program";
            return VSConstants.S_OK;
        }

        public int GetProcess(out IDebugProcess2 ppProcess)
        {
            ppProcess = _process;
            return ppProcess != null ? VSConstants.S_OK : VSConstants.E_NOTIMPL;
        }

        public int GetProgramId(out Guid pguidProgramId)
        {
            pguidProgramId = _programId;
            return VSConstants.S_OK;
        }

        public int Step(IDebugThread2 pThread, enum_STEPKIND sk, enum_STEPUNIT step)
        {
            var tid = TryGetThreadId(pThread);

            SendDebugCommand("step", new Dictionary<string, object>
                {
                    { "threadId", tid },
                    { "stepKind", sk.ToString() },
                    { "stepUnit", step.ToString() }
                });

            return VSConstants.S_OK;
        }

        public int Terminate()
        {
            SendDebugCommand("stop");

            _state.ResetVariableTree();
            ClearPendingBreakpoints();

            if (!_localDestroyed)
            {
                _localDestroyed = true;
                Callback?.ProgramDestroyed(this);
            }

            ResetEngineCachesAfterExit(disposeTransport: true);

            return VSConstants.S_OK;
        }

        public int WriteDump(enum_DUMPTYPE DUMPTYPE, string pszDumpUrl)
        {
            return VSConstants.E_NOTIMPL;
        }

        int IDebugEngine2.Attach(
            IDebugProgram2[] rgpPrograms,
            IDebugProgramNode2[] rgpProgramNodes,
            uint celtPrograms,
            IDebugEventCallback2 pCallback,
            enum_ATTACH_REASON dwReason)
        {
            rgpPrograms[0].GetProgramId(out _programId);
            _callback = new EngineCallback(this, pCallback);

            Callback.EngineCreated();
            Callback.ProgramCreated();
            Callback.EngineLoaded();

            SendDebugCommand("start");
            SendDebugCommand("get_threads");

            return VSConstants.S_OK;
        }

        int IDebugEngineLaunch2.LaunchSuspended(
            string pszServer,
            IDebugPort2 pPort,
            string pszExe,
            string pszArgs,
            string pszDir,
            string bstrEnv,
            string pszOptions,
            enum_LAUNCH_FLAGS dwLaunchFlags,
            uint hStdInput,
            uint hStdOutput,
            uint hStdError,
            IDebugEventCallback2 pCallback,
            out IDebugProcess2 ppProcess)
        {
            var host = string.IsNullOrWhiteSpace(pszServer) ? "127.0.0.1" : pszServer;
            var port = 0;
            if (TryParseEndpoint(pszExe, out var parsedHost, out var parsedPort))
            {
                host = parsedHost;
                port = parsedPort;
            }

            _transportServer.Host = host;
            _transportServer.Port = port;
            _transportServer.Start();

            if (!EnsureTransportConnectedBeforeStart())
            {
                ppProcess = null;
                return VSConstants.E_FAIL;
            }

            _callback = new EngineCallback(this, pCallback);

            ppProcess = new AD7Process(pPort);
            _process = ppProcess;

            return VSConstants.S_OK;
        }

        int IDebugEngine2.RemoveAllSetExceptions(ref Guid guidType)
        {
            return VSConstants.E_NOTIMPL;
        }

        int IDebugEngine2.RemoveSetException(EXCEPTION_INFO[] pException)
        {
            return VSConstants.E_NOTIMPL;
        }

        int IDebugEngineLaunch2.TerminateProcess(IDebugProcess2 pProcess)
        {
            SendDebugCommand("stop");

            ClearPendingBreakpoints();

            if (!_localDestroyed)
            {
                _localDestroyed = true;
                Callback.ProgramDestroyed(this);
            }

            ResetEngineCachesAfterExit(disposeTransport: true);

            return VSConstants.S_OK;
        }

        private void ResetEngineCachesAfterExit(bool disposeTransport)
        {
            try
            {
                _state.ResetAll();
            }
            catch
            {
            }

            try
            {
                ClearPendingBreakpoints();
                _pendingBreakpointsByLocation.Clear();
                LastVsxMessageLine = null;
                _vsxConnectionFailed = false;
                _outSeq = 0;
            }
            catch
            {
            }

            if (disposeTransport)
            {
                try
                {
                    _transportServer.Dispose();
                }
                catch
                {
                }
            }
        }

        int IDebugEngine2.SetException(EXCEPTION_INFO[] pException)
        {
            return VSConstants.E_NOTIMPL;
        }

        int IDebugEngine2.SetLocale(ushort wLangID)
        {
            return VSConstants.S_OK;
        }

        int IDebugEngine2.SetMetric(string pszMetric, object varValue)
        {
            return VSConstants.S_OK;
        }

        int IDebugEngine2.SetRegistryRoot(string pszRegistryRoot)
        {
            return VSConstants.S_OK;
        }

        int IDebugEngineLaunch2.ResumeProcess(IDebugProcess2 pProcess)
        {
            pProcess.GetPort(out var port);
            pProcess.GetProcessId(out var id);
            var defaultPort = (IDebugDefaultPort2)port;
            defaultPort.GetPortNotify(out var notify);
            notify.AddProgramNode(new AD7ProgramNode(id));
            return VSConstants.S_OK;
        }

        int IDebugEngineLaunch2.CanTerminateProcess(IDebugProcess2 pProcess)
        {
            return VSConstants.S_OK;
        }

        int IDebugEngine2.ContinueFromSynchronousEvent(IDebugEvent2 pEvent)
        {
            return VSConstants.S_OK;
        }

        int IDebugEngine2.CreatePendingBreakpoint(IDebugBreakpointRequest2 pBPRequest, out IDebugPendingBreakpoint2 ppPendingBP)
        {
            var pending = new AD7PendingBreakPoint(this, pBPRequest);
            ppPendingBP = pending;

            try
            {
                var requestInfo = new BP_REQUEST_INFO[1];
                pBPRequest.GetRequestInfo(enum_BPREQI_FIELDS.BPREQI_BPLOCATION, requestInfo);
                var location = requestInfo[0].bpLocation;
                var docPosition = (IDebugDocumentPosition2)Marshal.GetObjectForIUnknown(location.unionmember2);

                docPosition.GetFileName(out var documentName);

                var startPosition = new TEXT_POSITION[1];
                var endPosition = new TEXT_POSITION[1];
                docPosition.GetRange(startPosition, endPosition);

                var normalizedFile = documentName;
                var oneBasedLine = (int)startPosition[0].dwLine + 1;

                var key = MakeLocationKey(normalizedFile, oneBasedLine);
                if (key != null)
                    _pendingBreakpointsByLocation[key] = pending;

                if (!IsLikelyExecutableSourceLine(normalizedFile, oneBasedLine))
                    pending.MarkPermanentlyDisabled("Breakpoint line is not executable.");
            }
            catch
            {
            }

            return VSConstants.S_OK;
        }

        int IDebugEngine2.DestroyProgram(IDebugProgram2 pProgram)
        {
            return VSConstants.E_NOTIMPL;
        }

        int IDebugEngine2.EnumPrograms(out IEnumDebugPrograms2 ppEnum)
        {
            ppEnum = new AD7ProgramEnum(new IDebugProgram2[] { this });
            return VSConstants.S_OK;
        }

        int IDebugEngine2.GetEngineId(out Guid pguidEngine)
        {
            pguidEngine = new Guid(EngineConstants.EngineGUID);
            return VSConstants.S_OK;
        }

        private static int TryGetThreadId(IDebugThread2 pThread)
        {
            if (pThread == null)
                return 0;

            try
            {
                if (pThread.GetThreadId(out uint tid) == VSConstants.S_OK)
                    return (int)tid;
            }
            catch
            {
            }

            return 0;
        }
    }
}
