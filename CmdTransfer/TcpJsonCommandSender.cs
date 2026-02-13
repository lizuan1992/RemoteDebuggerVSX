using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RemoteDebuggerVSX.Debugging;
using RemoteDebuggerVSX.Logging;

namespace RemoteDebuggerVSX.CmdTransfer
{
    internal sealed class TcpJsonCommandSender : IDisposable
    {
        private readonly object _sendLock = new object();

        private TcpClient _client;
        private NetworkStream _stream;
        private CancellationTokenSource _rxCts;
        private Task _rxTask;

        private volatile bool _connected;
        private int _connectionSequence;
        private int _activeConnectionId;
        private int _expectedCloseConnectionId;

        public event Action<string> LineReceived;
        public event Action Disconnected;
        public event Action<string> ConnectionFailed;

        public bool Connect()
        {
            if (_connected && _client != null && _stream != null)
            {
                return true;
            }

            try
            {
                RemoteEndpointSettings.GetEndpoint(out var host, out var port);

                _client = new TcpClient();
                _client.Connect(host, port);

                _stream = _client.GetStream();

                var connectionId = Interlocked.Increment(ref _connectionSequence);
                Volatile.Write(ref _activeConnectionId, connectionId);
                Interlocked.Exchange(ref _expectedCloseConnectionId, 0);

                _connected = true;
                StartReceiveLoop(connectionId);

                try
                {
                    RemoteEndpointSettings.MarkTransportConnected();
                }
                catch (Exception ex)
                {
                    VsxLog.Debug("Transport", ex);
                }

                VsxLog.Debug("Transport", $"TCP connected to {host}:{port}.", TimeSpan.FromSeconds(10));
                return true;
            }
            catch (Exception ex)
            {
                _connected = false;

                TryMarkConnectionFailed();
                RaiseConnectionFailed(ex.Message);
                VsxLog.Debug("Transport", ex);
                Close();
                return false;
            }
        }

        private void StartReceiveLoop(int connectionId)
        {
            if (_rxTask != null)
            {
                return;
            }

            _rxCts = new CancellationTokenSource();
            var token = _rxCts.Token;

            _rxTask = Task.Run(async () =>
            {
                try
                {
                    using (var reader = new StreamReader(_stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true))
                    {
                        while (!token.IsCancellationRequested)
                        {
                            var line = await reader.ReadLineAsync().ConfigureAwait(false);
                            if (line == null)
                            {
                                if (IsExpectedClose(connectionId))
                                {
                                    ClearExpectedClose(connectionId);
                                    TryMarkTransportDisconnected();
                                }
                                else
                                {
                                    TryMarkConnectionFailed();
                                    RaiseConnectionFailed("Transport disconnected.");
                                }

                                RaiseDisconnected();
                                Close();
                                break;
                            }

                            try
                            {
                                LineReceived?.Invoke(line);
                            }
                            catch (Exception ex)
                            {
                                VsxLog.Debug("Transport", ex);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!IsExpectedClose(connectionId))
                    {
                        VsxLog.Debug("Transport", ex);

                        TryMarkConnectionFailed();
                        RaiseConnectionFailed(ex.Message);
                    }
                    else
                    {
                        ClearExpectedClose(connectionId);
                    }

                    RaiseDisconnected();
                    Close();
                }
            }, token);
        }

        public bool SendRawLine(string jsonLine)
        {
            if (string.IsNullOrEmpty(jsonLine))
            {
                return false;
            }

            lock (_sendLock)
            {
                if (!_connected || _stream == null)
                {
                    return false;
                }

                try
                {
                    var data = Encoding.UTF8.GetBytes(jsonLine + "\n");
                    _stream.Write(data, 0, data.Length);
                    _stream.Flush();
                    return true;
                }
                catch (Exception ex)
                {
                    VsxLog.Debug("Transport", ex);

                    TryMarkConnectionFailed();
                    RaiseDisconnected();
                    RaiseConnectionFailed(ex.Message);
                    Close();
                    return false;
                }
            }
        }

        private bool IsExpectedClose(int connectionId)
        {
            return connectionId != 0 && Volatile.Read(ref _expectedCloseConnectionId) == connectionId;
        }

        private void ClearExpectedClose(int connectionId)
        {
            if (connectionId != 0)
            {
                Interlocked.CompareExchange(ref _expectedCloseConnectionId, 0, connectionId);
            }
        }

        private void MarkExpectedClose()
        {
            var currentId = Volatile.Read(ref _activeConnectionId);
            if (currentId != 0)
            {
                Interlocked.Exchange(ref _expectedCloseConnectionId, currentId);
            }
        }

        public void Dispose()
        {
            Close(suppressFailureNotifications: true);
        }

        public void Close(bool suppressFailureNotifications = false)
        {
            if (suppressFailureNotifications)
            {
                MarkExpectedClose();
            }
            else
            {
                Interlocked.Exchange(ref _expectedCloseConnectionId, 0);
            }

            _connected = false;

            try
            {
                _rxCts?.Cancel();
            }
            catch (Exception ex)
            {
                VsxLog.Debug("Transport", ex);
            }

            _rxCts = null;
            _rxTask = null;

            try
            {
                _stream?.Dispose();
            }
            catch (Exception ex)
            {
                VsxLog.Debug("Transport", ex);
            }

            try
            {
                _client?.Close();
            }
            catch (Exception ex)
            {
                VsxLog.Debug("Transport", ex);
            }

            _stream = null;
            _client = null;

            Volatile.Write(ref _activeConnectionId, 0);
        }

        private static void TryMarkConnectionFailed()
        {
            try
            {
                RemoteEndpointSettings.MarkConnectionFailed();
            }
            catch (Exception ex)
            {
                VsxLog.Debug("Transport", ex);
            }
        }

        private static void TryMarkTransportDisconnected()
        {
            try
            {
                RemoteEndpointSettings.MarkTransportDisconnected();
            }
            catch (Exception ex)
            {
                VsxLog.Debug("Transport", ex);
            }
        }

        private void RaiseConnectionFailed(string message)
        {
            try
            {
                ConnectionFailed?.Invoke(message);
            }
            catch (Exception ex)
            {
                VsxLog.Debug("Transport", ex);
            }
        }

        private void RaiseDisconnected()
        {
            try
            {
                Disconnected?.Invoke();
            }
            catch (Exception ex)
            {
                VsxLog.Debug("Transport", ex);
            }
        }
    }
}
