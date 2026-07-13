using System;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UniForge
{
    public partial class TcpTransportClient
    {
        /// <summary>
        /// Connect to the daemon via local IPC.
        /// Reads the transport endpoint from ~/.uniforge/daemon.json.
        /// </summary>
        public Task ConnectAsync()
        {
            return ConnectAsyncCore(expectedEpoch: null);
        }

        /// <summary>
        /// 接続処理の本体。expectedEpoch は自動再接続パスが delay 前に捕捉した世代番号で、
        /// 明示切断（世代の進行）後の自動再接続を確実に中断するために使う。
        /// ユーザー起点の接続（ConnectAsync）は null を渡し、常に現在の世代で接続する。
        /// </summary>
        private async Task ConnectAsyncCore(int? expectedEpoch)
        {
            int epoch;
            lock (_lock)
            {
                if (_isDisposed || _isConnecting || IsConnected) return;
                if (expectedEpoch.HasValue && _disconnectEpoch != expectedEpoch.Value) return;
                _isConnecting = true;
                epoch = _disconnectEpoch;
            }

            try
            {
                // 旧送受信ループを確実に停止してから新しい接続を開始する
                // （旧ループが生き残ると同一ストリームへの二重ライターになる）
                await StopLoopsAsync().ConfigureAwait(false);

                CancellationTokenSource cts;
                lock (_lock)
                {
                    // 停止待機中に Dispose / 明示切断が完了していたら接続を再開しない
                    // （明示切断後の意図しない再接続と、破棄後のソケットリークを防ぐ）
                    if (_isDisposed || _disconnectEpoch != epoch) return;
                    cts = new CancellationTokenSource();
                    _cts = cts;
                }

                var connectionInfo = _readConnectionInfo();
                if (connectionInfo == null)
                {
                    // Daemon not running - try to start silently
                    if (await _tryStartDaemonAsync().ConfigureAwait(false))
                    {
                        connectionInfo = _readConnectionInfo();
                    }

                    if (connectionInfo == null)
                    {
                        throw new InvalidOperationException("Daemon is not running and failed to start");
                    }
                }

                await ConnectToDaemonAsync(connectionInfo, cts.Token).ConfigureAwait(false);

                lock (_lock)
                {
                    // 接続確立中に Dispose / 明示切断が完了していたら公開せず後始末する
                    // （StopLoopsAsync と同じロックを取ることで、公開とループ停止の交錯を防ぐ）
                    if (_isDisposed || _disconnectEpoch != epoch)
                    {
                        DisposeConnectionObjects();
                        try { cts.Cancel(); } catch { }
                        try { cts.Dispose(); } catch { }
                        return;
                    }

                    _isConnected = true;
                    _reconnectAttempts = 0;
                    _connectedTransport = connectionInfo.transport;
                    _connectedEndpoint = GetConnectionDisplayName(connectionInfo);
                    _lastError = null;

                    // トランスポートループはメインスレッドを占有しないようスレッドプールで実行する
                    var stream = _stream;
                    _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(stream, cts.Token));
                    _sendLoopTask = Task.Run(() => SendLoopAsync(stream, cts.Token));

                    // 通知のキュー投入も公開と同じロック内で行う。
                    // ロック外だと、直後の切断が OnDisconnected を先に投入し
                    // 「Disconnected → Connected」の逆順で観測される
                    EnqueueMainThread(() =>
                    {
                        try { OnConnected?.Invoke(); }
                        catch (Exception e) { Debug.LogError($"[UniForge] OnConnected handler error: {e}"); }
                    });
                }

                Debug.Log($"[UniForge] Connected to daemon via {_connectedTransport}: {_connectedEndpoint}");
            }
            catch (ObjectDisposedException)
            {
                // Disposed during connection, ignore
            }
            catch (OperationCanceledException)
            {
                // Connection was cancelled, ignore
            }
            catch (Exception ex)
            {
                // Connection failure is normal when daemon is not running - don't spam console
                _lastError = ex.Message;
                EnqueueMainThread(() =>
                {
                    try { OnError?.Invoke(ex.Message); }
                    catch (Exception e) { Debug.LogError($"[UniForge] OnError handler error: {e}"); }
                });
                _isConnecting = false;
                if (!_isDisposed)
                {
                    _ = ScheduleReconnectAsync();
                }
            }
            finally
            {
                _isConnecting = false;
            }
        }

        /// <summary>
        /// Disconnect from the server
        /// </summary>
        public async Task DisconnectAsync()
        {
            lock (_lock)
            {
                _disconnectEpoch++; // 進行中の ConnectAsync を無効化する
            }
            _reconnectAttempts = MaxReconnectAttempts; // Prevent reconnection
            await CloseConnectionAsync().ConfigureAwait(false);
        }

        private async Task ConnectToDaemonAsync(DaemonConnectionInfo connectionInfo, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DisposeConnectionObjects();

            switch (connectionInfo.transport)
            {
                case "namedPipe":
                    await ConnectNamedPipeAsync(connectionInfo.endpoint, cancellationToken).ConfigureAwait(false);
                    return;

                case "tcp":
                    if (!connectionInfo.port.HasValue)
                    {
                        throw new InvalidOperationException("Daemon info is missing a TCP port");
                    }

                    await ConnectSocketAsync(
                        new IPEndPoint(IPAddress.Parse(connectionInfo.host ?? "127.0.0.1"), connectionInfo.port.Value),
                        AddressFamily.InterNetwork,
                        ProtocolType.Tcp,
                        cancellationToken).ConfigureAwait(false);
                    return;

                case "unix":
                default:
                    if (string.IsNullOrEmpty(connectionInfo.endpoint))
                    {
                        throw new InvalidOperationException("Daemon info is missing a Unix socket path");
                    }

                    var endPoint = CreateUnixDomainSocketEndpoint(connectionInfo.endpoint);
                    await ConnectSocketAsync(endPoint, AddressFamily.Unix, ProtocolType.Unspecified, cancellationToken).ConfigureAwait(false);
                    return;
            }
        }

        private async Task ConnectNamedPipeAsync(string endpoint, CancellationToken cancellationToken)
        {
            var pipeName = ExtractPipeName(endpoint);
            if (string.IsNullOrEmpty(pipeName))
            {
                throw new InvalidOperationException("Daemon info is missing a named pipe endpoint");
            }

            _namedPipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using (cancellationToken.Register(() =>
            {
                try { _namedPipeClient?.Dispose(); }
                catch { }
            }))
            {
                await Task.Run(() => _namedPipeClient.Connect(10000), cancellationToken).ConfigureAwait(false);
            }
            _stream = _namedPipeClient;
        }

        private async Task ConnectSocketAsync(
            EndPoint endPoint,
            AddressFamily addressFamily,
            ProtocolType protocolType,
            CancellationToken cancellationToken)
        {
            _socket = new Socket(addressFamily, SocketType.Stream, protocolType);
            using (cancellationToken.Register(() =>
            {
                try { _socket?.Dispose(); }
                catch { }
            }))
            {
                await Task.Run(() => _socket.Connect(endPoint), cancellationToken).ConfigureAwait(false);
            }
            _stream = new NetworkStream(_socket, true);
        }

        private static EndPoint CreateUnixDomainSocketEndpoint(string endpoint)
        {
            var type = Type.GetType("System.Net.Sockets.UnixDomainSocketEndPoint")
                ?? typeof(Socket).Assembly.GetType("System.Net.Sockets.UnixDomainSocketEndPoint")
                ?? Type.GetType("Mono.Unix.UnixEndPoint, Mono.Posix");

            if (type == null)
            {
                throw new NotSupportedException("Unix domain sockets are not available in this Unity runtime");
            }

            var instance = Activator.CreateInstance(type, endpoint) as EndPoint;
            if (instance == null)
            {
                throw new InvalidOperationException("Failed to create Unix domain socket endpoint");
            }

            return instance;
        }

        private static string ExtractPipeName(string endpoint)
        {
            if (string.IsNullOrEmpty(endpoint))
            {
                return null;
            }

            const string prefix = @"\\.\pipe\";
            if (endpoint.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return endpoint.Substring(prefix.Length);
            }

            return endpoint;
        }

        private static string GetConnectionDisplayName(DaemonConnectionInfo connectionInfo)
        {
            if (connectionInfo == null)
            {
                return string.Empty;
            }

            if (connectionInfo.transport == "tcp")
            {
                return $"{connectionInfo.host ?? "127.0.0.1"}:{connectionInfo.port.GetValueOrDefault()}";
            }

            return connectionInfo.endpoint ?? string.Empty;
        }

        private async Task CloseConnectionAsync()
        {
            _isConnected = false;
            await StopLoopsAsync().ConfigureAwait(false);
            _connectedEndpoint = null;
            _connectedTransport = null;

            EnqueueMainThread(() =>
            {
                try { OnDisconnected?.Invoke(); }
                catch (Exception e) { Debug.LogError($"[UniForge] OnDisconnected handler error: {e}"); }
            });
        }

        /// <summary>
        /// 旧送受信ループを停止して CTS を破棄する。
        /// キャンセル → 接続破棄（ブロック中の Read/Write を解除）→ 完了待ち（上限付き）の順で、
        /// 旧ループが新しい接続に書き込む余地をなくす。
        /// </summary>
        private async Task StopLoopsAsync()
        {
            CancellationTokenSource oldCts;
            Task oldReceiveLoop;
            Task oldSendLoop;
            lock (_lock)
            {
                oldCts = _cts;
                _cts = null;
                oldReceiveLoop = _receiveLoopTask;
                oldSendLoop = _sendLoopTask;
                _receiveLoopTask = null;
                _sendLoopTask = null;
            }

            try { oldCts?.Cancel(); }
            catch { }

            DisposeConnectionObjects();

            var loops = Task.WhenAll(oldReceiveLoop ?? Task.CompletedTask, oldSendLoop ?? Task.CompletedTask);
            if (!loops.IsCompleted)
            {
                await Task.WhenAny(loops, Task.Delay(LoopShutdownTimeoutMs)).ConfigureAwait(false);
            }

            try { oldCts?.Dispose(); }
            catch { }
        }

        /// <summary>
        /// Dispose the active connection objects without touching the cancellation token.
        /// Safe to call multiple times.
        /// </summary>
        private void DisposeConnectionObjects()
        {
            try { _stream?.Dispose(); }
            catch { }
            _stream = null;

            try { _namedPipeClient?.Dispose(); }
            catch { }
            _namedPipeClient = null;

            try { _socket?.Dispose(); }
            catch { }
            _socket = null;
        }

        /// <summary>
        /// Release transport resources (CTS, stream, socket, pipe). Safe to call multiple times.
        /// </summary>
        private void ReleaseResources()
        {
            try { _cts?.Cancel(); }
            catch { }

            DisposeConnectionObjects();

            try { _cts?.Dispose(); }
            catch { }
            _cts = null;
            _connectedEndpoint = null;
            _connectedTransport = null;
        }

        private async Task ScheduleReconnectAsync()
        {
            int epoch;
            lock (_lock)
            {
                if (_isDisposed || _isConnecting || _reconnectAttempts >= MaxReconnectAttempts) return;
                // delay 前の世代を捕捉して ConnectAsyncCore に引き渡す。
                // delay 中〜接続開始直前に明示切断が完了した場合、世代不一致で確実に中断される
                epoch = _disconnectEpoch;
            }

            _reconnectAttempts++;
            var delay = BaseReconnectDelayMs * (int)Math.Pow(2, Math.Min(_reconnectAttempts - 1, MaxReconnectExponent));

            try
            {
                await Task.Delay(delay).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!_isDisposed && !_isConnecting && !IsConnected)
            {
                await ConnectAsyncCore(epoch);
            }
        }

        public void Dispose()
        {
            // ConnectAsyncCore の公開処理（_lock 内の epoch/_isDisposed 確認）と原子的に競合させるため、
            // 状態の反転は必ず _lock 内で行う
            lock (_lock)
            {
                if (_isDisposed) return;
                _isDisposed = true;
                _disconnectEpoch++;
                _reconnectAttempts = MaxReconnectAttempts; // Prevent reconnection
            }

            ReleaseResources();
            _sendQueue.Dispose();
        }
    }
}
