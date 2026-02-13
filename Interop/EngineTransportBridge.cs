using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;
using RemoteDebuggerVSX.Logging;

namespace RemoteDebuggerVSX.Interop
{
    internal sealed class EngineTransportBridge : IDisposable
    {
        private const string DefaultHost = "127.0.0.1";
        private static readonly IPAddress DefaultHostAddress = IPAddress.Parse(DefaultHost);
        private static readonly Random PortRandom = new Random(unchecked((int)DateTime.UtcNow.Ticks));
        private static readonly object PortRandomGate = new object();

        private const string CommandFieldName = "command";

        private readonly Func<string, Dictionary<string, object>, bool> _sendToRemote;
        private readonly object _sendLock = new object();

        private static int _lastBoundPort;

        private int _port;
        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private Task _acceptTask;
        private TcpClient _client;
        private StreamWriter _clientWriter;

        internal static int ActivePort => Interlocked.CompareExchange(ref _lastBoundPort, 0, 0);

        public event Action<string, Dictionary<string, object>> EngineCommandParsed;

        public EngineTransportBridge(Func<string, Dictionary<string, object>, bool> sendToRemote, int port)
        {
            _sendToRemote = sendToRemote ?? throw new ArgumentNullException(nameof(sendToRemote));
            SetActivePort(port);
        }

        private void SetActivePort(int port)
        {
            _port = port;
            Interlocked.Exchange(ref _lastBoundPort, port);
        }

        public void EnsureListening()
        {
            if (_listener != null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            var listenPort = _port > 0 ? _port : NextRandomPort();

            while (true)
            {
                TcpListener candidate = null;
                try
                {
                    candidate = new TcpListener(DefaultHostAddress, listenPort);
                    candidate.Start();
                    _listener = candidate;
                    SetActivePort(listenPort);
                    break;
                }
                catch (Exception ex)
                {
                    candidate?.Stop();
                    var retryPort = NextRandomPort();
                    VsxLog.Debug("EngineLink", $"Failed to listen on tcp://{DefaultHost}:{listenPort}; retrying on {retryPort}.", ex, TimeSpan.FromSeconds(5));
                    listenPort = retryPort;
                }
            }

            _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
            VsxLog.Debug("EngineLink", $"Listening for engine connections on tcp://{DefaultHost}:{_port}", TimeSpan.FromSeconds(5));
        }

        public bool TrySendLine(string jsonLine)
        {
            if (string.IsNullOrEmpty(jsonLine))
            {
                return false;
            }

            lock (_sendLock)
            {
                var writer = _clientWriter;
                if (writer == null)
                {
                    return false;
                }

                try
                {
                    writer.WriteLine(jsonLine);
                    writer.Flush();
                    return true;
                }
                catch (Exception ex)
                {
                    VsxLog.Debug("EngineLink", "Failed to send line to engine.", ex, TimeSpan.FromSeconds(5));
                    SafeCloseClient_NoLock();
                    return false;
                }
            }
        }

        private static int NextRandomPort()
        {
            lock (PortRandomGate)
            {
                return PortRandom.Next(20000, 60000);
            }
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    VsxLog.Debug("EngineLink", "Engine connected.", TimeSpan.FromSeconds(5));
                    ReplaceClient(client);
                    _ = Task.Run(() => ReadLoopAsync(client, token));
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    VsxLog.Debug("EngineLink", "Accept loop failure.", ex, TimeSpan.FromSeconds(5));
                    await Task.Delay(200, token).ConfigureAwait(false);
                }
            }
        }

        private void ReplaceClient(TcpClient client)
        {
            lock (_sendLock)
            {
                SafeCloseClient_NoLock();
                _client = client;

                var stream = client.GetStream();
                _clientWriter = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1024, leaveOpen: true)
                {
                    AutoFlush = true
                };
            }
        }

        private async Task ReadLoopAsync(TcpClient client, CancellationToken token)
        {
            try
            {
                using (var reader = new StreamReader(client.GetStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true))
                {
                    while (!token.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (line == null)
                        {
                            break;
                        }

                        OnEngineLine(line);
                    }
                }
            }
            catch (Exception ex)
            {
                VsxLog.Debug("EngineLink", "Engine read loop failure.", ex, TimeSpan.FromSeconds(5));
            }
            finally
            {
                lock (_sendLock)
                {
                    if (_client == client)
                    {
                        SafeCloseClient_NoLock();
                    }
                }

                VsxLog.Debug("EngineLink", "Engine disconnected.", TimeSpan.FromSeconds(5));
            }
        }

        private void OnEngineLine(string line)
        {
            Dictionary<string, object> obj = null;
            string cmd = null;

            try
            {
                obj = JsonConvert.DeserializeObject<Dictionary<string, object>>(line);
                if (obj == null)
                {
                    return;
                }

                if (!obj.TryGetValue(CommandFieldName, out var cmdObj) || cmdObj == null)
                {
                    return;
                }

                cmd = cmdObj.ToString();
                obj.Remove(CommandFieldName);

                _sendToRemote(cmd, obj);
            }
            catch (Exception ex)
            {
                VsxLog.Debug("Broker", "Failed to parse/forward engine line.", ex, TimeSpan.FromSeconds(5));
            }
            finally
            {
                try
                {
                    EngineCommandParsed?.Invoke(cmd, obj);
                }
                catch (Exception ex)
                {
                    VsxLog.Debug("Broker", "EngineCommandParsed handler failed.", ex, TimeSpan.FromSeconds(5));
                }
            }
        }

        private void SafeCloseClient_NoLock()
        {
            try
            {
                _clientWriter?.Dispose();
            }
            catch
            {
            }

            _clientWriter = null;

            try
            {
                _client?.Close();
            }
            catch
            {
            }

            _client = null;
        }

        public void DisconnectEngine()
        {
            lock (_sendLock)
            {
                SafeCloseClient_NoLock();
            }
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_listener != null)
            {
                try
                {
                    _listener.Stop();
                }
                catch
                {
                }

                _listener = null;
            }

            if (_cts != null)
            {
                try
                {
                    _cts.Cancel();
                }
                catch
                {
                }

                _cts.Dispose();
                _cts = null;
            }

            lock (_sendLock)
            {
                SafeCloseClient_NoLock();
            }
        }
    }
}
