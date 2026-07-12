using System;
using System.Buffers;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UniForge
{
    public partial class TcpTransportClient
    {
        private async Task SendLoopAsync()
        {
            const int BufferSize = 8192;
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            bool shouldReconnect = false;
            try
            {
                while (!_isDisposed && _stream != null && !_cts.Token.IsCancellationRequested)
                {
                    await _sendQueue.WaitForEnqueueAsync(_cts.Token);

                    int offset = 0;
                    bool anyWritten = false;

                    while (_sendQueue.TryDequeue(out var message))
                    {
                        int byteCount = Encoding.UTF8.GetByteCount(message) + 1;

                        if (byteCount > buffer.Length)
                        {
                            if (offset > 0)
                            {
                                await _stream.WriteAsync(buffer, 0, offset, _cts.Token);
                                offset = 0;
                            }
                            var largeBuffer = ArrayPool<byte>.Shared.Rent(byteCount);
                            try
                            {
                                int written = Encoding.UTF8.GetBytes(message, 0, message.Length, largeBuffer, 0);
                                largeBuffer[written] = (byte)'\n';
                                await _stream.WriteAsync(largeBuffer, 0, written + 1, _cts.Token);
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
                            await _stream.WriteAsync(buffer, 0, offset, _cts.Token);
                            offset = 0;
                            anyWritten = true;
                        }

                        offset += Encoding.UTF8.GetBytes(message, 0, message.Length, buffer, offset);
                        buffer[offset++] = (byte)'\n';
                    }

                    if (offset > 0)
                    {
                        await _stream.WriteAsync(buffer, 0, offset, _cts.Token);
                        anyWritten = true;
                    }
                    if (anyWritten)
                    {
                        await _stream.FlushAsync(_cts.Token);
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

            if (shouldReconnect)
            {
                await CloseConnectionAsync();

                if (!_isDisposed && _reconnectAttempts < MaxReconnectAttempts)
                {
                    _ = ScheduleReconnectAsync();
                }
            }
        }
    }
}
