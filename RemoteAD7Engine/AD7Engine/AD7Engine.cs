using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Debugger.Interop;

namespace RemoteAD7Engine
{
    [ComVisible(true)]
    [Guid("AD7056E6-0F4F-4D79-83A5-9AF4A1A7A4B0")]
    public partial class AD7Engine : IDebugEngine2, IDebugEngineLaunch2, IDebugProgram3
    {
        private const string MessageTypeRequest = "request";
        private const string MessageTypeResponse = "response";
        private const string MessageTypeEvent = "event";

        private EngineCallback _callback;
        private IDebugProcess2 _process;
        private Guid _programId = Guid.NewGuid();

        private readonly EngineTransportClient _transportServer = new EngineTransportClient();
        private readonly RemoteState _state;

        internal string LastVsxMessageLine { get; private set; }

        internal EngineCallback Callback
        {
            get { return _callback; }
        }

        internal IDebugProcess2 RemoteProcess
        {
            get { return _process; }
        }

        internal RemoteState State
        {
            get { return _state; }
        }

        private bool _localDestroyed;
        private bool _vsxConnectionFailed;

        private int _outSeq;
        private static readonly TimeSpan TransportConnectTimeout = TimeSpan.FromSeconds(3);

        private readonly Dictionary<string, AD7PendingBreakPoint> _pendingBreakpointsByLocation =
            new Dictionary<string, AD7PendingBreakPoint>(StringComparer.OrdinalIgnoreCase);

        public AD7Engine()
        {
            _state = new RemoteState(this);

            _transportServer.LineReceived += line =>
            {
                LastVsxMessageLine = line;
                HandleVsxLine(line);
            };
        }

        internal bool IsTransportConnected
        {
            get { return _transportServer.IsConnected; }
        }
    }
}
