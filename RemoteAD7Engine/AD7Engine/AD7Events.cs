using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;

namespace RemoteAD7Engine
{
    internal sealed class AD7BreakEvent : AD7StoppingEvent, IDebugBreakEvent2
    {
        public const string IID = "C7405D4D-E24B-44DA-B084-53B0E610C1B9";
    }

    internal sealed class AD7OutputStringEvent : AD7AsynchronousEvent, IDebugOutputStringEvent2
    {
        public const string IID = "569C4BB1-7B82-46FC-AE28-4536DDAD753E";

        private readonly string _message;

        public AD7OutputStringEvent(string message)
        {
            _message = message ?? string.Empty;
        }

        int IDebugOutputStringEvent2.GetString(out string pbstrString)
        {
            pbstrString = _message;
            return VSConstants.S_OK;
        }
    }

    internal sealed class AD7StepCompleteEvent : AD7StoppingEvent, IDebugStepCompleteEvent2
    {
        public const string IID = "0F7F24C1-74D9-4EA6-A3EA-7EDB2D81441D";
    }

    internal sealed class AD7ThreadCreateEvent : AD7SynchronousEvent, IDebugThreadCreateEvent2
    {
        public const string IID = "2090CCFC-70C5-491D-A5E8-BAD2DD9EE3EA";
    }

    internal sealed class AD7ThreadDestroyEvent : AD7AsynchronousEvent, IDebugThreadDestroyEvent2
    {
        public const string IID = "2C3B7532-A36F-4A6E-9072-49BE649B8541";

        private readonly uint _exitCode;

        public AD7ThreadDestroyEvent(uint exitCode)
        {
            _exitCode = exitCode;
        }

        int IDebugThreadDestroyEvent2.GetExitCode(out uint exitCode)
        {
            exitCode = _exitCode;
            return VSConstants.S_OK;
        }
    }

    internal sealed class AD7BreakpointErrorEvent : AD7AsynchronousEvent, IDebugBreakpointErrorEvent2
    {
        public const string IID = "ABB0CA42-F82B-4622-84E4-6903AE90F210";

        private readonly AD7ErrorBreakpoint _errorBreakpoint;

        public AD7BreakpointErrorEvent(AD7ErrorBreakpoint errorBreakpoint = null)
        {
            _errorBreakpoint = errorBreakpoint;
        }

        public int GetErrorBreakpoint(out IDebugErrorBreakpoint2 ppErrorBP)
        {
            ppErrorBP = _errorBreakpoint;
            return VSConstants.S_OK;
        }
    }

    internal sealed class AD7BreakpointBoundEvent : AD7AsynchronousEvent, IDebugBreakpointBoundEvent2
    {
        public const string IID = "1DDDB704-CF99-4B8A-B746-DABB01DD13A0";

        private readonly AD7PendingBreakPoint _pendingBreakpoint;

        public AD7BreakpointBoundEvent(AD7PendingBreakPoint pendingBreakpoint)
        {
            _pendingBreakpoint = pendingBreakpoint;
        }

        int IDebugBreakpointBoundEvent2.EnumBoundBreakpoints(out IEnumDebugBoundBreakpoints2 ppEnum)
        {
            return _pendingBreakpoint.EnumBoundBreakpoints(out ppEnum);
        }

        int IDebugBreakpointBoundEvent2.GetPendingBreakpoint(out IDebugPendingBreakpoint2 ppPendingBP)
        {
            ppPendingBP = _pendingBreakpoint;
            return VSConstants.S_OK;
        }
    }

    internal sealed class AD7BreakpointEvent : AD7StoppingEvent, IDebugBreakpointEvent2
    {
        public const string IID = "501C1E21-C557-48B8-BA30-A1EAB0BC4A74";

        private readonly AD7PendingBreakPoint _boundBreakpoints;

        public AD7BreakpointEvent(AD7PendingBreakPoint boundBreakpoints)
        {
            _boundBreakpoints = boundBreakpoints;
        }

        int IDebugBreakpointEvent2.EnumBreakpoints(out IEnumDebugBoundBreakpoints2 ppEnum)
        {
            ppEnum = null;

            if (_boundBreakpoints == null)
            {
                return VSConstants.S_OK;
            }

            return _boundBreakpoints.EnumBoundBreakpoints(out ppEnum);
        }
    }

    internal sealed class AD7ModuleLoadEvent : AD7AsynchronousEvent, IDebugModuleLoadEvent2
    {
        public const string IID = "989DB083-0D7C-40D1-A9D9-921BF611A4B2";

        private readonly AD7Module _module;
        private readonly bool _isLoad;

        public AD7ModuleLoadEvent(AD7Module module, bool isLoad)
        {
            _module = module;
            _isLoad = isLoad;
        }

        int IDebugModuleLoadEvent2.GetModule(out IDebugModule2 module, ref string debugMessage, ref int fIsLoad)
        {
            module = _module;

            if (_isLoad)
            {
                debugMessage = string.Concat("Loaded '", _module.ModuleName, "'");
                fIsLoad = 1;
            }
            else
            {
                debugMessage = string.Concat("Unloaded '", _module.ModuleName, "'");
                fIsLoad = 0;
            }

            return VSConstants.S_OK;
        }
    }

    internal sealed class AD7EngineCreateEvent : AD7AsynchronousEvent, IDebugEngineCreateEvent2
    {
        public const string IID = "FE5B734C-759D-4E59-AB04-F103343BDD06";

        private readonly IDebugEngine2 _engine;

        public AD7EngineCreateEvent(AD7Engine engine)
        {
            _engine = engine;
        }

        public static void Send(AD7Engine engine)
        {
            var eventObject = new AD7EngineCreateEvent(engine);
            engine.Callback.Send(eventObject, IID, null, null);
        }

        int IDebugEngineCreateEvent2.GetEngine(out IDebugEngine2 engine)
        {
            engine = _engine;
            return VSConstants.S_OK;
        }
    }

    internal sealed class AD7ProgramCreateEvent : AD7SynchronousEvent, IDebugProgramCreateEvent2
    {
        public const string IID = "96CD11EE-ECD4-4E89-957E-B5D496FC4139";

        internal static void Send(AD7Engine engine)
        {
            var eventObject = new AD7ProgramCreateEvent();
            engine.Callback.Send(eventObject, IID, engine, null);
        }
    }

    internal sealed class AD7LoadCompleteEvent : AD7StoppingEvent, IDebugLoadCompleteEvent2
    {
        public const string IID = "B1844850-1349-45D4-9F12-495212F5EB0B";
    }

    internal sealed class AD7ProgramDestroyEvent : AD7SynchronousEvent, IDebugProgramDestroyEvent2
    {
        public const string IID = "E147E9E3-6440-4073-A7B7-A65592C714B5";

        private readonly uint _exitCode;

        public AD7ProgramDestroyEvent(uint exitCode)
        {
            _exitCode = exitCode;
        }

        int IDebugProgramDestroyEvent2.GetExitCode(out uint exitCode)
        {
            exitCode = _exitCode;
            return VSConstants.S_OK;
        }
    }

    internal sealed class AD7EntryPointEvent : AD7StoppingEvent, IDebugEntryPointEvent2
    {
        public const string IID = "E8414A3E-1642-48EC-829E-5F4040E16DA9";
    }

    internal sealed class AD7ExceptionEvent : AD7StoppingEvent, IDebugExceptionEvent2
    {
        public const string IID = "51A94113-8788-4A54-AE15-08B74FF922D0";

        private readonly string _exceptionName;
        private readonly string _exceptionDescription;

        public AD7ExceptionEvent(string exceptionName = "Exception", string exceptionDescription = "An exception occurred.")
        {
            _exceptionName = exceptionName;
            _exceptionDescription = exceptionDescription;
        }

        int IDebugExceptionEvent2.GetException(EXCEPTION_INFO[] pExceptionInfo)
        {
            pExceptionInfo[0] = new EXCEPTION_INFO
            {
                bstrExceptionName = _exceptionName,
                dwCode = 0,
                dwState = enum_EXCEPTION_STATE.EXCEPTION_STOP_FIRST_CHANCE,
                guidType = Guid.Empty
            };
            return VSConstants.S_OK;
        }

        int IDebugExceptionEvent2.GetExceptionDescription(out string pbstrDescription)
        {
            pbstrDescription = string.IsNullOrEmpty(_exceptionName) ? _exceptionDescription : $"{_exceptionName} : {_exceptionDescription}";
            return VSConstants.S_OK;
        }

        int IDebugExceptionEvent2.CanPassToDebuggee()
        {
            return VSConstants.S_FALSE; // Assuming exceptions cannot be passed to debuggee
        }

        int IDebugExceptionEvent2.PassToDebuggee(int fPass)
        {
            return VSConstants.E_NOTIMPL;
        }
    }

    internal class AD7AsynchronousEvent : IDebugEvent2
    {
        public const uint Attributes = (uint)enum_EVENTATTRIBUTES.EVENT_ASYNCHRONOUS;

        int IDebugEvent2.GetAttributes(out uint eventAttributes)
        {
            eventAttributes = Attributes;
            return VSConstants.S_OK;
        }
    }

    internal class AD7SynchronousEvent : IDebugEvent2
    {
        public const uint Attributes = (uint)enum_EVENTATTRIBUTES.EVENT_SYNCHRONOUS;

        int IDebugEvent2.GetAttributes(out uint eventAttributes)
        {
            eventAttributes = Attributes;
            return VSConstants.S_OK;
        }
    }

    internal class AD7StoppingEvent : IDebugEvent2
    {
        public const uint Attributes = (uint)enum_EVENTATTRIBUTES.EVENT_ASYNC_STOP;

        int IDebugEvent2.GetAttributes(out uint eventAttributes)
        {
            eventAttributes = Attributes;
            return VSConstants.S_OK;
        }
    }
}
