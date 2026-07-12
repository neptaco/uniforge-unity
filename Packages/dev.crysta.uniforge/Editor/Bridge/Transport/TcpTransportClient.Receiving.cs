using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UniForge
{
    public partial class TcpTransportClient
    {
        /// <summary>
        /// Process queued callbacks on the main thread.
        /// Call this from EditorApplication.update
        /// </summary>
        public void ProcessMainThreadQueue()
        {
            _lastMainThreadProcessTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[UniForge] Main thread callback error: {ex}");
                }
            }

            while (_receiveQueue.TryDequeue(out var message))
            {
                try
                {
                    OnMessage?.Invoke(message);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[UniForge] Message handler error: {ex}");
                }
            }
        }

        /// <summary>
        /// Check if main thread is busy (hasn't processed queue recently).
        /// This can be called from any thread.
        /// </summary>
        private bool IsMainThreadBusy()
        {
            if (_lastMainThreadProcessTime == 0) return false;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return (now - _lastMainThreadProcessTime) > MainThreadBusyThresholdMs;
        }

        private void EnqueueMainThread(Action action)
        {
            if (_mainThreadQueue.Count >= MaxQueueSize)
            {
                Debug.LogWarning($"[UniForge] Main thread queue overflow ({_mainThreadQueue.Count} actions), dropping oldest action");
                _mainThreadQueue.TryDequeue(out _);
            }

            _mainThreadQueue.Enqueue(action);
        }

        /// <summary>
        /// Enqueues a received message with priority-based overflow handling.
        /// </summary>
        private void EnqueueReceivedMessage(string message)
        {
            var priority = GetMessagePriority(message);

            while (_receiveQueue.Count >= MaxQueueSize)
            {
                if (_receiveQueue.TryDropLowest(out _))
                {
                    Debug.LogWarning($"[UniForge] Receive queue overflow ({_receiveQueue.Count} messages), dropped low priority message");
                }
                else
                {
                    break;
                }
            }

            _receiveQueue.Enqueue(message, priority);
        }

        /// <summary>
        /// Try to handle the message on the receive thread without going through main thread.
        /// Handles ping/pong and busy detection.
        /// </summary>
        private bool TryHandleOnReceiveThread(string message)
        {
            if (message.Contains(MethodPing))
            {
                _sendQueue.Enqueue(PongMessage, PriorityLow);
                return true;
            }

            if (IsMainThreadBusy())
            {
                var requestId = ExtractStringField(message, "id");
                if (requestId != null)
                {
                    var busyResponse = $"{{\"jsonrpc\":\"2.0\",\"method\":\"unity.busy\",\"params\":{{\"requestId\":\"{requestId}\",\"retry_after_ms\":{BusyRetryAfterMs},\"reason\":\"Main thread is busy\"}}}}";
                    _sendQueue.Enqueue(busyResponse, PriorityLow);
                    return true;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Extract a string field value from JSON (simple string search, no full parsing).
        /// </summary>
        internal static string ExtractStringField(string json, string fieldName)
        {
            var key = $"\"{fieldName}\":\"";
            int startIndex = json.IndexOf(key, StringComparison.Ordinal);
            if (startIndex < 0) return null;

            startIndex += key.Length;
            int endIndex = json.IndexOf('"', startIndex);
            if (endIndex < 0) return null;

            return json.Substring(startIndex, endIndex - startIndex);
        }

        private async Task ReceiveLoopAsync()
        {
            var reader = new StreamReader(_stream, Encoding.UTF8);

            try
            {
                while (!_isDisposed && _stream != null && !_cts.Token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync();
                    if (line == null) break;
                    if (line.Length == 0) continue;

                    if (line[0] <= ' ' || line[line.Length - 1] <= ' ')
                    {
                        line = line.Trim();
                        if (line.Length == 0) continue;
                    }

                    if (!TryHandleOnReceiveThread(line))
                    {
                        EnqueueReceivedMessage(line);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (IOException ex)
            {
                if (!_isDisposed)
                {
                    EnqueueMainThread(() =>
                    {
                        try { OnError?.Invoke(ex.Message); }
                        catch (Exception e) { Debug.LogWarning($"[UniForge] OnError handler error: {e.Message}"); }
                    });
                }
            }
            catch (Exception ex)
            {
                if (!_isDisposed)
                {
                    Debug.LogError($"[UniForge] Receive loop error: {ex}");
                }
            }

            _isConnected = false;

            if (!_isDisposed && _reconnectAttempts < MaxReconnectAttempts)
            {
                _ = ScheduleReconnectAsync();
            }
        }
    }
}
