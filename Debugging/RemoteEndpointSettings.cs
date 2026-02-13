using System;
using System.Globalization;

namespace RemoteDebuggerVSX.Debugging
{
    internal static class RemoteEndpointSettings
    {
        public const string DefaultHost = "127.0.0.1";
        public const int DefaultPort = 9000;

        private static readonly object _gate = new object();

        private static string _host = DefaultHost;
        private static int _port = DefaultPort;
        private static bool _transportConnected;
        private static bool _sessionEstablished;
        private static bool _everEstablished;

        public static string Host
        {
            get
            {
                lock (_gate)
                {
                    return _host;
                }
            }
            set
            {
                lock (_gate)
                {
                    _host = string.IsNullOrWhiteSpace(value) ? DefaultHost : value.Trim();
                }
            }
        }

        public static int Port
        {
            get
            {
                lock (_gate)
                {
                    return _port;
                }
            }
            set
            {
                lock (_gate)
                {
                    _port = value <= 0 ? DefaultPort : value;
                }
            }
        }

        public static bool TransportConnected
        {
            get
            {
                lock (_gate)
                {
                    return _transportConnected;
                }
            }
        }

        public static bool SessionEstablished
        {
            get
            {
                lock (_gate)
                {
                    return _sessionEstablished;
                }
            }
        }

        public static bool EverEstablished
        {
            get
            {
                lock (_gate)
                {
                    return _everEstablished;
                }
            }
        }

        public static string ToHostPort()
        {
            lock (_gate)
            {
                return string.Concat(_host, ":", _port.ToString(CultureInfo.InvariantCulture));
            }
        }

        public static void MarkTransportConnected()
        {
            UpdateState(connected: true);
        }

        public static void MarkSessionEstablished()
        {
            UpdateState(session: true, ever: true);
        }

        public static void MarkTransportDisconnected()
        {
            UpdateState(connected: false, session: false);
        }

        public static void MarkConnectionFailed()
        {
            UpdateState(connected: false, session: false, ever: false);
        }

        public static bool TrySet(string host, string portText)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            if (!int.TryParse(portText, out var port) || port <= 0 || port > 65535)
            {
                return false;
            }

            lock (_gate)
            {
                _host = host.Trim();
                _port = port;
                _transportConnected = false;
                _sessionEstablished = false;
                _everEstablished = false;
            }

            return true;
        }

        public static void GetEndpoint(out string host, out int port)
        {
            lock (_gate)
            {
                host = _host;
                port = _port;
            }
        }

        private static void UpdateState(bool? connected = null, bool? session = null, bool? ever = null)
        {
            lock (_gate)
            {
                if (connected.HasValue)
                {
                    _transportConnected = connected.Value;
                }

                if (session.HasValue)
                {
                    _sessionEstablished = session.Value;
                }

                if (ever.HasValue)
                {
                    _everEstablished = ever.Value;
                }
            }
        }
    }
}
