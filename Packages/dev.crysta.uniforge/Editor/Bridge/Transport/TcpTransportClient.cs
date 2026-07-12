using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UniForge
{
    /// <summary>
    /// Local IPC transport client for connecting to the UniForge Daemon.
    /// Uses NDJSON (newline-delimited JSON) over Unix domain sockets or Windows named pipes.
    /// Falls back to TCP only when daemon.json advertises a TCP endpoint.
    /// </summary>
    public partial class TcpTransportClient : IDisposable
    {
        private Socket _socket;
        private NamedPipeClientStream _namedPipeClient;
        private Stream _stream;
        private CancellationTokenSource _cts;
        private readonly Func<DaemonConnectionInfo> _readConnectionInfo;
        private readonly Func<Task<bool>> _tryStartDaemonAsync;
        private readonly ConcurrentPriorityQueue<string> _sendQueue;
        private readonly ConcurrentPriorityQueue<string> _receiveQueue = new ConcurrentPriorityQueue<string>(3);
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();

        // Message priority levels
        private const int PriorityLow = 0;
        private const int PriorityMedium = 1;
        internal const int PriorityHigh = 2;

        // JSON-RPC method-based message markers for priority detection
        private const string MethodExecuteTool = "\"method\":\"daemon.executeTool\"";
        private const string MethodPing = "\"method\":\"daemon.ping\"";
        private const string MethodRegister = "\"method\":\"unity.register\"";
        private const string MethodToolsUpdate = "\"method\":\"unity.toolsUpdate\"";
        private const string MethodPong = "\"method\":\"unity.pong\"";
        private const string MethodBusy = "\"method\":\"unity.busy\"";

        // For JSON-RPC responses (tool results)
        private const string HasResultField = "\"result\":";
        private const string HasErrorField = "\"error\":";

        private bool _isConnecting;
        private bool _isConnected;
        private int _reconnectAttempts;
        private string _connectedEndpoint;
        private string _connectedTransport;
        private string _lastError;
        private const int MaxReconnectAttempts = 10;
        private const int BaseReconnectDelayMs = 1000;
        private const int MaxQueueSize = 1000; // Prevent unbounded queue growth

        // Pong response for ping handling on receive thread
        internal const string PongMessage = "{\"jsonrpc\":\"2.0\",\"method\":\"unity.pong\"}";

        // Main thread busy detection
        private long _lastMainThreadProcessTime;
        private const int MainThreadBusyThresholdMs = 200;

        // Timing constants
        private const int MaxReconnectExponent = 5;
        private const int BusyRetryAfterMs = 1000;

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnMessage;
        public event Action<string> OnError;

        private readonly object _lock = new object();
        private bool _isDisposed;

        public TcpTransportClient()
            : this(DaemonBootstrap.ReadConnectionInfo, DaemonBootstrap.TryStartAsync)
        {
        }

        internal TcpTransportClient(
            Func<DaemonConnectionInfo> readConnectionInfo,
            Func<Task<bool>> tryStartDaemonAsync)
        {
            _readConnectionInfo = readConnectionInfo ?? throw new ArgumentNullException(nameof(readConnectionInfo));
            _tryStartDaemonAsync = tryStartDaemonAsync ?? throw new ArgumentNullException(nameof(tryStartDaemonAsync));
            _sendQueue = new ConcurrentPriorityQueue<string>(3, enableSignal: true);
        }

        public bool IsConnected
        {
            get
            {
                try
                {
                    return _isConnected && _stream != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool IsConnecting => _isConnecting;
        public int ReconnectAttempts => _reconnectAttempts;
        public static int MaxReconnects => MaxReconnectAttempts;
        public string ConnectedEndpoint => _connectedEndpoint;
        public string ConnectedTransport => _connectedTransport;
        public string LastError => _lastError;

        /// <summary>
        /// Send a message to the server.
        /// Priority is auto-detected from the message content.
        /// </summary>
        public bool Send(string message)
        {
            if (!IsConnected)
            {
                Debug.LogWarning("[UniForge] Cannot send - not connected");
                return false;
            }

            return Send(message, GetMessagePriority(message));
        }

        /// <summary>
        /// Send a message to the server with an explicit priority.
        /// Use this overload to skip the Contains()-based priority detection on hot paths.
        /// </summary>
        public bool Send(string message, int priority)
        {
            if (!IsConnected)
            {
                Debug.LogWarning("[UniForge] Cannot send - not connected");
                return false;
            }

            // Check queue size to prevent unbounded growth
            while (_sendQueue.Count >= MaxQueueSize)
            {
                if (_sendQueue.TryDropLowest(out var dropped))
                {
                    Debug.LogWarning($"[UniForge] Send queue overflow ({_sendQueue.Count} messages), dropped low priority message");
                }
                else
                {
                    break;
                }
            }

            _sendQueue.Enqueue(message, priority);
            return true;
        }

        /// <summary>
        /// Determines the priority of a message based on its JSON-RPC content.
        /// High priority: JSON-RPC responses (result/error), register, execute-tool, tools-update
        /// Low priority: pong, busy, ping
        /// Medium priority: everything else
        /// </summary>
        internal static int GetMessagePriority(string message)
        {
            // High priority messages - critical for tool execution
            if (message.Contains(HasResultField) ||
                message.Contains(HasErrorField) ||
                message.Contains(MethodRegister) ||
                message.Contains(MethodExecuteTool) ||
                message.Contains(MethodToolsUpdate))
            {
                return PriorityHigh;
            }

            // Low priority messages - can be dropped without major impact
            if (message.Contains(MethodPong) ||
                message.Contains(MethodBusy) ||
                message.Contains(MethodPing))
            {
                return PriorityLow;
            }

            // Default to medium priority
            return PriorityMedium;
        }

    }
}
