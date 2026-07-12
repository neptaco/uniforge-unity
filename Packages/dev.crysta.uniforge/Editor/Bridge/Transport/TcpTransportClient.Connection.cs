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
        public async Task ConnectAsync()
        {
            lock (_lock)
            {
                if (_isDisposed || _isConnecting || IsConnected) return;
                _isConnecting = true;
            }

            try
            {
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                var connectionInfo = _readConnectionInfo();
                if (connectionInfo == null)
                {
                    // Daemon not running - try to start silently
                    if (await _tryStartDaemonAsync())
                    {
                        connectionInfo = _readConnectionInfo();
                    }

                    if (connectionInfo == null)
                    {
                        throw new InvalidOperationException("Daemon is not running and failed to start");
                    }
                }

                await ConnectToDaemonAsync(connectionInfo, _cts.Token);

                _isConnected = true;
                _reconnectAttempts = 0;
                _connectedTransport = connectionInfo.transport;
                _connectedEndpoint = GetConnectionDisplayName(connectionInfo);
                _lastError = null;

                _ = ReceiveLoopAsync();
                _ = SendLoopAsync();

                EnqueueMainThread(() =>
                {
                    try { OnConnected?.Invoke(); }
                    catch (Exception e) { Debug.LogError($"[UniForge] OnConnected handler error: {e}"); }
                });
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
            _reconnectAttempts = MaxReconnectAttempts; // Prevent reconnection
            await CloseConnectionAsync();
        }

        private async Task ConnectToDaemonAsync(DaemonConnectionInfo connectionInfo, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DisposeConnectionObjects();

            switch (connectionInfo.transport)
            {
                case "namedPipe":
                    await ConnectNamedPipeAsync(connectionInfo.endpoint, cancellationToken);
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
                        cancellationToken);
                    return;

                case "unix":
                default:
                    if (string.IsNullOrEmpty(connectionInfo.endpoint))
                    {
                        throw new InvalidOperationException("Daemon info is missing a Unix socket path");
                    }

                    var endPoint = CreateUnixDomainSocketEndpoint(connectionInfo.endpoint);
                    await ConnectSocketAsync(endPoint, AddressFamily.Unix, ProtocolType.Unspecified, cancellationToken);
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
                await Task.Run(() => _namedPipeClient.Connect(10000), cancellationToken);
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
                await Task.Run(() => _socket.Connect(endPoint), cancellationToken);
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

        private Task CloseConnectionAsync()
        {
            _isConnected = false;
            ReleaseResources();

            EnqueueMainThread(() =>
            {
                try { OnDisconnected?.Invoke(); }
                catch (Exception e) { Debug.LogError($"[UniForge] OnDisconnected handler error: {e}"); }
            });

            return Task.CompletedTask;
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
            if (_isDisposed || _isConnecting || _reconnectAttempts >= MaxReconnectAttempts) return;

            _reconnectAttempts++;
            var delay = BaseReconnectDelayMs * (int)Math.Pow(2, Math.Min(_reconnectAttempts - 1, MaxReconnectExponent));

            try
            {
                await Task.Delay(delay);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!_isDisposed && !_isConnecting && !IsConnected)
            {
                await ConnectAsync();
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _reconnectAttempts = MaxReconnectAttempts; // Prevent reconnection
            ReleaseResources();
            _sendQueue.Dispose();
        }
    }
}
