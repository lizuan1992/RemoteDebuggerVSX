using Microsoft.VisualStudio.OLE.Interop;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace RemoteAD7Engine
{
    public partial class AD7Engine
    {
        private bool EnsureTransportConnectedBeforeStart()
        {
            if (_transportServer.IsConnected)
            {
                return true;
            }

            if (_transportServer.WaitForConnection(TransportConnectTimeout))
            {
                return true;
            }

            LogProtocolWarning(string.Format(
                CultureInfo.InvariantCulture,
                "[RemoteAD7Engine][Transport] Timeout ({0:0.0}s) waiting for VSX connection before sending start command.",
                TransportConnectTimeout.TotalSeconds));

            return false;
        }

        private string SerializeCommand(string command, Dictionary<string, object> payload)
        {
            var msg = payload != null
                ? new Dictionary<string, object>(payload)
                : new Dictionary<string, object>();

            msg["type"] = MessageTypeRequest;
            msg["seq"] = Interlocked.Increment(ref _outSeq);
            msg["command"] = command;
            return JsonConvert.SerializeObject(msg);
        }

        private void SendToVsx(string command, Dictionary<string, object> payload = null)
        {
            if (!_transportServer.IsConnected)
            {
                HandleVsxTransportFailure($"[RemoteAD7Engine][Transport] VSX connection not established; cannot send '{command}'.");
                return;
            }

            var json = SerializeCommand(command, payload);
            if (!_transportServer.TrySendLine(json))
            {
                HandleVsxTransportFailure($"[RemoteAD7Engine][Transport] Failed to send line to VSX for '{command}'.");
                return;
            }
        }

        public void SendDebugCommand(string command, Dictionary<string, object> parameters = null, bool waitResponse = false)
        {
            if (waitResponse)
                _state.SyncManager.Reset(command);

            if (!_transportServer.IsConnected)
            {
                HandleVsxTransportFailure($"[RemoteAD7Engine][Transport] VSX not connected; skipping command '{command}'.");
                return;
            }

            SendToVsx(command, parameters);

            if (waitResponse)
                _state.SyncManager.WaitingResponse();
        }

        private void HandleVsxTransportFailure(string message)
        {
            if (_vsxConnectionFailed)
                return;

            _vsxConnectionFailed = true;
            LogProtocolWarning(message ?? "[RemoteAD7Engine][Transport] VSX connection failure.");
            try
            {
                Debug.WriteLine(message);
            }
            catch
            {
            }

            _state.ResetVariableTree();
            ClearPendingBreakpoints();

            if (!_localDestroyed)
            {
                _localDestroyed = true;
                Callback?.ProgramDestroyed(this);
            }

            ResetEngineCachesAfterExit(disposeTransport: false);
        }
    }
}
