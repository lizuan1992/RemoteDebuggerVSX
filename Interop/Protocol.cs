namespace RemoteDebuggerVSX.Interop
{
    internal static class Protocol
    {
        public static class Pipe
        {
            public const string BrokerName = "RemoteDebuggerVSX.Broker";
            public const string EngineName = "RemoteDebuggerVSX.Engine";
        }

        public static class Type
        {
            public const string Request = "request";
            public const string Response = "response";
            public const string Event = "event";
        }

        public static class Command
        {
            public const string Start = "start";
            public const string Ready = "ready";
            public const string Stop = "stop";
            public const string Pause = "pause";
            public const string Continue = "continue";
            public const string Step = "step";
            public const string SetBreakpoint = "set_breakpoint";
            public const string RemoveBreakpoint = "remove_breakpoint";

            public const string GetStack = "get_stack";
            public const string GetScopes = "get_scopes";
            public const string Evaluate = "evaluate";
            public const string GetThreads = "get_threads";
            public const string SetVariables = "set_variable";
        }

        public static class Event
        {
            public const string Stopped = "stopped";
            public const string Continued = "continued";
            public const string Output = "output";
            public const string ProgramExited = "program_exited";
            public const string ThreadStarted = "thread_started";
            public const string ThreadExited = "thread_exited";
        }
    }
}
