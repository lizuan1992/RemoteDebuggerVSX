using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RemoteAD7Engine
{
    internal sealed class EngineTransportClient : IDisposable
    {
        private static int _lastLogTick;

        private const int LogThrottleMs = 5000;

        private readonly object _sendLock = new object();
        private readonly ManualResetEventSlim _connectedEvent = new ManualResetEventSlim(false);

        private CancellationTokenSource _cts;
        private Task _runTask;
        private TcpClient _client;
        private StreamWriter _writer;

        public event Action<string> LineReceived;

        public bool IsConnected { get; private set; }

        public string Host { get; set; }
        public int Port { get; set; }

        private static void DebugThrottled(string message, Exception ex = null)
        {
            try
            {
                var now = Environment.TickCount;
                var last = Interlocked.Exchange(ref _lastLogTick, now);
                if (last != 0 && unchecked(now - last) < LogThrottleMs)
                {
                    return;
                }

                var text = ex == null ? message : string.Concat(message, " | ", ex);
                Debug.WriteLine("[RemoteAD7Engine][Tcp] " + text);
            }
            catch
            {
            }
        }

        public void Start()
        {
            if (_runTask != null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _runTask = Task.Run(() => RunTransportOnceAsync(_cts.Token));
        }

        public bool WaitForConnection(TimeSpan timeout)
        {
            if (IsConnected)
            {
                return true;
            }

            try
            {
                if (_connectedEvent.Wait(timeout))
                {
                    return IsConnected;
                }
            }
            catch (ObjectDisposedException)
            {
            }

            return IsConnected;
        }

        private async Task RunTransportOnceAsync(CancellationToken token)
        {
            TcpClient client = null;

            try
            {
                token.ThrowIfCancellationRequested();

                client = new TcpClient();
                await client.ConnectAsync(Host, Port).ConfigureAwait(false);
                DebugThrottled("Connected to VSX engine bridge.");

                var stream = client.GetStream();

                lock (_sendLock)
                {
                    _client = client;
                    _writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true)
                    {
                        AutoFlush = true
                    };

                    IsConnected = true;
                    _connectedEvent.Set();
                }

                using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true))
                {
                    while (!token.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (line == null)
                        {
                            break;
                        }

                        try
                        {
                            LineReceived?.Invoke(line);
                        }
                        catch (Exception ex)
                        {
                            DebugThrottled("LineReceived handler threw.", ex);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                DebugThrottled("EngineTransportClient transport error.", ex);
            }
            finally
            {
                lock (_sendLock)
                {
                    SafeCloseClient_NoLock();
                    IsConnected = false;
                    _connectedEvent.Reset();
                }

                if (client != null)
                {
                    try
                    {
                        client.Dispose();
                    }
                    catch
                    {
                    }
                }
            }
        }

        public bool TrySendLine(string jsonLine)
        {
            if (string.IsNullOrEmpty(jsonLine))
            {
                return false;
            }

            lock (_sendLock)
            {
                if (_writer == null)
                {
                    return false;
                }

                try
                {
                    _writer.WriteLine(jsonLine);
                    return true;
                }
                catch (Exception ex)
                {
                    DebugThrottled("Failed to send line to VSX.", ex);
                    SafeCloseClient_NoLock();
                    IsConnected = false;
                    _connectedEvent.Reset();
                    return false;
                }
            }
        }

        private void SafeCloseClient_NoLock()
        {
            try
            {
                _writer?.Dispose();
            }
            catch
            {
            }

            _writer = null;

            try
            {
                _client?.Close();
            }
            catch
            {
            }

            _client = null;
        }

        public void Dispose()
        {
            try
            {
                _cts?.Cancel();
            }
            catch
            {
            }

            _cts = null;

            lock (_sendLock)
            {
                SafeCloseClient_NoLock();
                IsConnected = false;
                _connectedEvent.Reset();
            }

            try
            {
                _connectedEvent.Set();
            }
            catch
            {
            }

            _connectedEvent.Dispose();
        }
    }
}
