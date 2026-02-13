using System;
using Microsoft.VisualStudio.Debugger.Interop;

namespace RemoteAD7Engine
{
    internal sealed class EngineCallback
    {
        private readonly IDebugEventCallback2 _eventCallback;
        private readonly AD7Engine _engine;

        public EngineCallback(AD7Engine engine, IDebugEventCallback2 eventCallback)
        {
            _engine = engine;
            _eventCallback = eventCallback;
        }

        public void Send(IDebugEvent2 eventObject, string iidEvent, IDebugProgram2 program, IDebugThread2 thread)
        {
            if (eventObject == null || string.IsNullOrEmpty(iidEvent) || _eventCallback == null)
            {
                return;
            }

            eventObject.GetAttributes(out var attributes);
            var riidEvent = new Guid(iidEvent);
            _eventCallback.Event(_engine, null, program, thread, eventObject, ref riidEvent, attributes);
        }

        private void RaiseEvent(IDebugEvent2 eventObject, string iidEvent, IDebugProcess2 process, IDebugProgram2 program, IDebugThread2 thread, uint attributes)
        {
            if (_eventCallback == null)
            {
                return;
            }

            var iid = new Guid(iidEvent);
            _eventCallback.Event(_engine, process, program, thread, eventObject, ref iid, attributes);
        }

        public void EngineCreated()
        {
            RaiseEvent(new AD7EngineCreateEvent(_engine), AD7EngineCreateEvent.IID, _engine.RemoteProcess, _engine, null, AD7AsynchronousEvent.Attributes);
        }

        public void ProgramCreated()
        {
            RaiseEvent(new AD7ProgramCreateEvent(), AD7ProgramCreateEvent.IID, null, _engine, null, AD7AsynchronousEvent.Attributes);
        }

        public void EngineLoaded()
        {
            RaiseEvent(new AD7LoadCompleteEvent(), AD7LoadCompleteEvent.IID, _engine.RemoteProcess, _engine, null, AD7StoppingEvent.Attributes);
        }

        internal void DebugEntryPoint()
        {
            RaiseEvent(new AD7EntryPointEvent(), AD7EntryPointEvent.IID, _engine.RemoteProcess, _engine, null, AD7AsynchronousEvent.Attributes);
        }

        internal void ProgramDestroyed(IDebugProgram2 program)
        {
            RaiseEvent(new AD7ProgramDestroyEvent(0), AD7ProgramDestroyEvent.IID, null, program, null, AD7AsynchronousEvent.Attributes);
        }

        internal void BoundBreakpoint(AD7PendingBreakPoint breakpoint)
        {
            RaiseEvent(new AD7BreakpointBoundEvent(breakpoint), AD7BreakpointBoundEvent.IID, _engine.RemoteProcess, _engine, null, AD7AsynchronousEvent.Attributes);
        }

        internal void ErrorBreakpoint(AD7ErrorBreakpoint breakpoint)
        {
            RaiseEvent(new AD7BreakpointErrorEvent(breakpoint), AD7BreakpointErrorEvent.IID, _engine.RemoteProcess, _engine, null, AD7AsynchronousEvent.Attributes);
        }

        internal void ModuleLoaded(AD7Module module)
        {
            RaiseEvent(new AD7ModuleLoadEvent(module, true), AD7ModuleLoadEvent.IID, _engine.RemoteProcess, _engine, null, AD7AsynchronousEvent.Attributes);
        }

        internal void ModuleUnloaded(AD7Module module)
        {
            RaiseEvent(new AD7ModuleLoadEvent(module, false), AD7ModuleLoadEvent.IID, _engine.RemoteProcess, _engine, null, AD7AsynchronousEvent.Attributes);
        }

        internal void BreakpointHit(AD7PendingBreakPoint breakpoint, AD7Thread thread)
        {
            RaiseEvent(new AD7BreakpointEvent(breakpoint), AD7BreakpointEvent.IID, _engine.RemoteProcess, _engine, thread, AD7StoppingEvent.Attributes);
        }

        internal void ThreadStarted(AD7Thread thread)
        {
            RaiseEvent(new AD7ThreadCreateEvent(), AD7ThreadCreateEvent.IID, _engine.RemoteProcess, _engine, thread, AD7AsynchronousEvent.Attributes);
        }

        internal void ThreadEnded(AD7Thread thread)
        {
            RaiseEvent(new AD7ThreadDestroyEvent(0), AD7ThreadDestroyEvent.IID, _engine.RemoteProcess, _engine, thread, AD7AsynchronousEvent.Attributes);
        }

        internal void StepCompleted(AD7Thread thread)
        {
            RaiseEvent(new AD7StepCompleteEvent(), AD7StepCompleteEvent.IID, _engine.RemoteProcess, _engine, thread, AD7StoppingEvent.Attributes);
        }
    }
}
