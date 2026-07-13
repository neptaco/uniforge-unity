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

        // JSON-RPC method names for priority detection
        private const string MethodExecuteTool = "daemon.executeTool";
        private const string MethodPing = "daemon.ping";
        private const string MethodRegister = "unity.register";
        private const string MethodToolsUpdate = "unity.toolsUpdate";
        private const string MethodPong = "unity.pong";
        private const string MethodBusy = "unity.busy";

        // For JSON-RPC responses (tool results)
        private const string HasResultField = "\"result\":";
        private const string HasErrorField = "\"error\":";

        private bool _isConnecting;
        private bool _isConnected;
        private int _reconnectAttempts;
        // 明示切断のたびに増える世代番号。ConnectAsync が待機中に切断が完了した場合の再接続を防ぐ
        private int _disconnectEpoch;
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
        private const int LoopShutdownTimeoutMs = 2000;

        // 送受信ループのタスク（再接続時に旧ループの完了を待つために保持する）
        private Task _receiveLoopTask;
        private Task _sendLoopTask;

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
            return GetMessagePriority(message, ExtractTopLevelMethod(message));
        }

        private static int GetMessagePriority(string message, string method)
        {
            if (method != null)
            {
                switch (method)
                {
                    // High priority messages - critical for tool execution
                    case MethodRegister:
                    case MethodExecuteTool:
                    case MethodToolsUpdate:
                        return PriorityHigh;

                    // Low priority messages - can be dropped without major impact
                    case MethodPong:
                    case MethodBusy:
                    case MethodPing:
                        return PriorityLow;

                    default:
                        return PriorityMedium;
                }
            }

            // method を持たない JSON-RPC レスポンス（result/error）は高優先度
            if (message.Contains(HasResultField) || message.Contains(HasErrorField))
            {
                return PriorityHigh;
            }

            // Default to medium priority
            return PriorityMedium;
        }

        /// <summary>
        /// トップレベルの "method" フィールドの文字列値を抽出する。
        /// ネストされたオブジェクトや文字列リテラル内の出現は無視する。
        /// </summary>
        internal static string ExtractTopLevelMethod(string json)
        {
            var literal = ExtractTopLevelLiteral(json, "method");
            if (literal == null || literal.Length < 2 || literal[0] != '"')
            {
                return null;
            }

            return literal.Substring(1, literal.Length - 2);
        }

        /// <summary>
        /// トップレベルフィールドの値を JSON リテラルのまま抽出する（文字列は引用符込み）。
        /// 文字列リテラル内・ネスト内のキーは無視する。オブジェクト・配列値は対象外（null）。
        /// </summary>
        internal static string ExtractTopLevelLiteral(string json, string fieldName)
        {
            if (string.IsNullOrEmpty(json)) return null;

            int depth = 0;
            int length = json.Length;
            int i = 0;

            while (i < length)
            {
                char c = json[i];
                if (c == '"')
                {
                    int keyStart = i + 1;
                    int afterKey = SkipString(json, i);
                    if (afterKey < 0) return null;

                    if (depth == 1)
                    {
                        int j = afterKey;
                        while (j < length && char.IsWhiteSpace(json[j])) j++;
                        if (j < length && json[j] == ':')
                        {
                            j++;
                            while (j < length && char.IsWhiteSpace(json[j])) j++;

                            bool matches = afterKey - 1 - keyStart == fieldName.Length
                                && string.CompareOrdinal(json, keyStart, fieldName, 0, fieldName.Length) == 0;
                            if (matches)
                            {
                                return ReadValueLiteral(json, j);
                            }

                            i = j;
                            continue;
                        }
                    }

                    i = afterKey;
                    continue;
                }

                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
                i++;
            }

            return null;
        }

        /// <summary>
        /// 開始クォート位置から文字列リテラルを読み飛ばし、閉じクォートの次のインデックスを返す。
        /// 未終端の場合は -1。
        /// </summary>
        private static int SkipString(string json, int openQuoteIndex)
        {
            for (int i = openQuoteIndex + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\')
                {
                    i++;
                    continue;
                }

                if (c == '"') return i + 1;
            }

            return -1;
        }

        private static string ReadValueLiteral(string json, int start)
        {
            if (start >= json.Length) return null;

            char c = json[start];
            if (c == '"')
            {
                int end = SkipString(json, start);
                if (end < 0) return null;
                return json.Substring(start, end - start);
            }

            if (c == '{' || c == '[') return null;

            int i = start;
            while (i < json.Length && json[i] != ',' && json[i] != '}' && json[i] != ']' && !char.IsWhiteSpace(json[i]))
            {
                i++;
            }

            return i > start ? json.Substring(start, i - start) : null;
        }
    }
}
