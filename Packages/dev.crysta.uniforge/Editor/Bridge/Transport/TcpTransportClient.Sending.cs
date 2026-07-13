using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UniForge
{
    public partial class TcpTransportClient
    {
        // ストリームとトークンは接続時のものを引数で受け取る。
        // フィールド参照だと再接続後の新しい接続を旧ループが触ってしまうため。
        private async Task SendLoopAsync(Stream stream, CancellationToken cancellationToken)
        {
            const int BufferSize = 8192;
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            bool shouldReconnect = false;
            try
            {
                while (!_isDisposed && !cancellationToken.IsCancellationRequested)
                {
                    // ConfigureAwait(false): 継続を Unity メインスレッドへ戻さない
                    await _sendQueue.WaitForEnqueueAsync(cancellationToken).ConfigureAwait(false);

                    int offset = 0;
                    bool anyWritten = false;

                    while (_sendQueue.TryDequeue(out var message))
                    {
                        int byteCount = Encoding.UTF8.GetByteCount(message) + 1;

                        if (byteCount > buffer.Length)
                        {
                            if (offset > 0)
                            {
                                await stream.WriteAsync(buffer, 0, offset, cancellationToken).ConfigureAwait(false);
                                offset = 0;
                            }
                            var largeBuffer = ArrayPool<byte>.Shared.Rent(byteCount);
                            try
                            {
                                int written = Encoding.UTF8.GetBytes(message, 0, message.Length, largeBuffer, 0);
                                largeBuffer[written] = (byte)'\n';
                                await stream.WriteAsync(largeBuffer, 0, written + 1, cancellationToken).ConfigureAwait(false);
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(largeBuffer);
                            }
                            anyWritten = true;
                            continue;
                        }

                        if (offset + byteCount > buffer.Length)
                        {
                            await stream.WriteAsync(buffer, 0, offset, cancellationToken).ConfigureAwait(false);
                            offset = 0;
                            anyWritten = true;
                        }

                        offset += Encoding.UTF8.GetBytes(message, 0, message.Length, buffer, offset);
                        buffer[offset++] = (byte)'\n';
                    }

                    if (offset > 0)
                    {
                        await stream.WriteAsync(buffer, 0, offset, cancellationToken).ConfigureAwait(false);
                        anyWritten = true;
                    }
                    if (anyWritten)
                    {
                        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                if (!_isDisposed)
                {
                    Debug.LogWarning($"[UniForge] Send loop error: {ex.Message}");
                    shouldReconnect = true;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (shouldReconnect && !cancellationToken.IsCancellationRequested)
            {
                // CloseConnectionAsync は本ループのタスク完了を待つため、自己待機を避けて await しない
                // （_isConnected=false は CloseConnectionAsync 冒頭で同期的に設定される）
                _ = CloseConnectionAsync();

                if (!_isDisposed && _reconnectAttempts < MaxReconnectAttempts)
                {
                    _ = ScheduleReconnectAsync();
                }
            }
        }
    }
}
