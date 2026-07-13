using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UniForge.Tests
{
    [TestFixture]
    public class TcpTransportClientTests
    {
        #region Constructor Tests

        [Test]
        public void Constructor_CreatesClient()
        {
            using (var client = new TcpTransportClient())
            {
                Assert.IsNotNull(client);
            }
        }

        #endregion

        #region Initial State Tests

        [Test]
        public void IsConnected_BeforeConnect_ReturnsFalse()
        {
            using (var client = new TcpTransportClient())
            {
                Assert.IsFalse(client.IsConnected);
            }
        }

        #endregion

        #region Dispose Tests

        [Test]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            var client = new TcpTransportClient();
            client.Dispose();
            client.Dispose();
            client.Dispose();

            // Should not throw
            Assert.Pass("Multiple Dispose calls did not throw");
        }

        [Test]
        public void Dispose_AfterDispose_IsConnectedReturnsFalse()
        {
            var client = new TcpTransportClient();
            client.Dispose();

            Assert.IsFalse(client.IsConnected);
        }

        [Test]
        [Timeout(5000)]
        public void ConnectAsync_WithUnixSocketDaemonJson_ConnectsToLocalSocket()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                Assert.Ignore("Unix domain sockets are not used on Windows editors.");
            }

            var socketPath = Path.Combine(Path.GetTempPath(), $"uniforge-test-{Guid.NewGuid():N}.sock");
            Socket acceptedSocket = null;

            try
            {
                using (var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
                {
                    listener.Bind(CreateUnixDomainSocketEndpoint(socketPath));
                    listener.Listen(1);

                    var acceptTask = Task.Run(() => listener.Accept());
                    using (var client = new TcpTransportClient(
                        () => new DaemonConnectionInfo
                        {
                            transport = "unix",
                            endpoint = socketPath
                        },
                        () => Task.FromResult(false)))
                    {
                        Task.Run(() => client.ConnectAsync()).GetAwaiter().GetResult();

                        acceptedSocket = WaitWithTimeout(acceptTask, 2000).GetAwaiter().GetResult();

                        Assert.IsTrue(client.IsConnected);
                        Assert.AreEqual("unix", client.ConnectedTransport);
                        Assert.AreEqual(socketPath, client.ConnectedEndpoint);
                        Assert.IsNotNull(acceptedSocket);
                        Assert.IsTrue(acceptedSocket.Connected);

                        client.DisconnectAsync().GetAwaiter().GetResult();
                    }
                }
            }
            finally
            {
                try { acceptedSocket?.Dispose(); }
                catch { }

                if (File.Exists(socketPath))
                {
                    File.Delete(socketPath);
                }
            }
        }

        #endregion

        #region Send Tests (without connection)

        [Test]
        public void Send_WhenNotConnected_DoesNotThrow()
        {
            using (var client = new TcpTransportClient())
            {
                LogAssert.Expect(LogType.Warning, "[UniForge] Cannot send - not connected");
                // Should log warning but not throw
                Assert.DoesNotThrow(() => client.Send("test message"));
            }
        }

        [Test]
        public void Send_AfterDispose_DoesNotThrow()
        {
            var client = new TcpTransportClient();
            client.Dispose();

            LogAssert.Expect(LogType.Warning, "[UniForge] Cannot send - not connected");
            // Should not throw even after dispose
            Assert.DoesNotThrow(() => client.Send("test message"));
        }

        #endregion

        #region ProcessMainThreadQueue Tests

        [Test]
        public void ProcessMainThreadQueue_WhenEmpty_DoesNotThrow()
        {
            using (var client = new TcpTransportClient())
            {
                Assert.DoesNotThrow(() => client.ProcessMainThreadQueue());
            }
        }

        [Test]
        public void ProcessMainThreadQueue_CanBeCalledMultipleTimes()
        {
            using (var client = new TcpTransportClient())
            {
                client.ProcessMainThreadQueue();
                client.ProcessMainThreadQueue();
                client.ProcessMainThreadQueue();

                Assert.Pass("Multiple ProcessMainThreadQueue calls did not throw");
            }
        }

        #endregion

        #region Event Subscription Tests

        [Test]
        public void OnConnected_CanSubscribe()
        {
            using (var client = new TcpTransportClient())
            {
                bool eventFired = false;
                client.OnConnected += () => eventFired = true;

                // Event won't fire without actual connection, but subscription should work
                Assert.IsFalse(eventFired);
            }
        }

        [Test]
        public void OnDisconnected_CanSubscribe()
        {
            using (var client = new TcpTransportClient())
            {
                bool eventFired = false;
                client.OnDisconnected += () => eventFired = true;

                Assert.IsFalse(eventFired);
            }
        }

        [Test]
        public void OnMessage_CanSubscribe()
        {
            using (var client = new TcpTransportClient())
            {
                string receivedMessage = null;
                client.OnMessage += (msg) => receivedMessage = msg;

                Assert.IsNull(receivedMessage);
            }
        }

        [Test]
        public void OnError_CanSubscribe()
        {
            using (var client = new TcpTransportClient())
            {
                string errorMessage = null;
                client.OnError += (err) => errorMessage = err;

                Assert.IsNull(errorMessage);
            }
        }

        #endregion

        #region Message Priority Tests (JSON-RPC format)

        [Test]
        public void GetMessagePriority_JsonRpcResponse_ReturnsHighPriority()
        {
            var message = "{\"jsonrpc\":\"2.0\",\"id\":\"123\",\"result\":{\"success\":true}}";
            var priority = TcpTransportClient.GetMessagePriority(message);
            Assert.AreEqual(2, priority); // PriorityHigh = 2
        }

        [Test]
        public void GetMessagePriority_JsonRpcErrorResponse_ReturnsHighPriority()
        {
            var message = "{\"jsonrpc\":\"2.0\",\"id\":\"123\",\"error\":{\"code\":-32600,\"message\":\"Invalid\"}}";
            var priority = TcpTransportClient.GetMessagePriority(message);
            Assert.AreEqual(2, priority);
        }

        [Test]
        public void GetMessagePriority_ToolsUpdateMethod_ReturnsHighPriority()
        {
            var message = "{\"jsonrpc\":\"2.0\",\"method\":\"unity.toolsUpdate\",\"params\":{}}";
            var priority = TcpTransportClient.GetMessagePriority(message);
            Assert.AreEqual(2, priority);
        }

        [Test]
        public void GetMessagePriority_RegisterMethod_ReturnsHighPriority()
        {
            var message = "{\"jsonrpc\":\"2.0\",\"id\":\"u-1\",\"method\":\"unity.register\",\"params\":{}}";
            var priority = TcpTransportClient.GetMessagePriority(message);
            Assert.AreEqual(2, priority);
        }

        [Test]
        public void GetMessagePriority_ExecuteToolMethod_ReturnsHighPriority()
        {
            var message = "{\"jsonrpc\":\"2.0\",\"id\":\"d-1\",\"method\":\"daemon.executeTool\",\"params\":{\"tool\":\"test\"}}";
            var priority = TcpTransportClient.GetMessagePriority(message);
            Assert.AreEqual(2, priority);
        }

        [Test]
        public void GetMessagePriority_PongMethod_ReturnsLowPriority()
        {
            var message = "{\"jsonrpc\":\"2.0\",\"method\":\"unity.pong\"}";
            var priority = TcpTransportClient.GetMessagePriority(message);
            Assert.AreEqual(0, priority); // PriorityLow = 0
        }

        [Test]
        public void GetMessagePriority_BusyMethod_ReturnsLowPriority()
        {
            var message = "{\"jsonrpc\":\"2.0\",\"method\":\"unity.busy\",\"params\":{\"reason\":\"Main thread is busy\"}}";
            var priority = TcpTransportClient.GetMessagePriority(message);
            Assert.AreEqual(0, priority);
        }

        [Test]
        public void GetMessagePriority_PingMethod_ReturnsLowPriority()
        {
            var message = "{\"jsonrpc\":\"2.0\",\"method\":\"daemon.ping\"}";
            var priority = TcpTransportClient.GetMessagePriority(message);
            Assert.AreEqual(0, priority);
        }

        [Test]
        public void GetMessagePriority_UnknownMethod_ReturnsMediumPriority()
        {
            var message = "{\"jsonrpc\":\"2.0\",\"method\":\"unknown\",\"params\":{}}";
            var priority = TcpTransportClient.GetMessagePriority(message);
            Assert.AreEqual(1, priority); // PriorityMedium = 1
        }

        [Test]
        public void GetMessagePriority_EmptyMessage_ReturnsMediumPriority()
        {
            var message = "{}";
            var priority = TcpTransportClient.GetMessagePriority(message);
            Assert.AreEqual(1, priority);
        }

        [Test]
        public void GetMessagePriority_NoMethodField_ReturnsMediumPriority()
        {
            var message = "{\"data\":\"test\"}";
            var priority = TcpTransportClient.GetMessagePriority(message);
            Assert.AreEqual(1, priority);
        }

        [Test]
        public void GetMessagePriority_PingInsideNestedObject_IsNotLowPriority()
        {
            // ネストされたオブジェクト内の "method":"daemon.ping" を ping と誤判定しないこと
            var message = "{\"jsonrpc\":\"2.0\",\"method\":\"unknown\",\"params\":{\"inner\":{\"method\":\"daemon.ping\"}}}";
            var priority = TcpTransportClient.GetMessagePriority(message);
            Assert.AreEqual(1, priority); // PriorityMedium
        }

        [Test]
        public void GetMessagePriority_PongInsideNestedObject_WithoutTopLevelMethod_IsMedium()
        {
            var message = "{\"jsonrpc\":\"2.0\",\"params\":{\"inner\":{\"method\":\"unity.pong\"}}}";
            var priority = TcpTransportClient.GetMessagePriority(message);
            Assert.AreEqual(1, priority);
        }

        #endregion

        #region Top-level method extraction Tests

        [Test]
        public void ExtractTopLevelMethod_SimpleMessage_ReturnsMethod()
        {
            var json = "{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"method\":\"daemon.executeTool\",\"params\":{}}";
            Assert.AreEqual("daemon.executeTool", TcpTransportClient.ExtractTopLevelMethod(json));
        }

        [Test]
        public void ExtractTopLevelMethod_NestedMethodOnly_ReturnsNull()
        {
            var json = "{\"jsonrpc\":\"2.0\",\"params\":{\"method\":\"daemon.ping\"}}";
            Assert.IsNull(TcpTransportClient.ExtractTopLevelMethod(json));
        }

        [Test]
        public void ExtractTopLevelMethod_MethodInsideStringValue_ReturnsNull()
        {
            // 文字列値の中に "method":"daemon.ping" 相当のテキストがあっても無視すること
            var json = "{\"jsonrpc\":\"2.0\",\"params\":{\"text\":\"\\\"method\\\":\\\"daemon.ping\\\"\"}}";
            Assert.IsNull(TcpTransportClient.ExtractTopLevelMethod(json));
        }

        [Test]
        public void ExtractTopLevelMethod_TopLevelMethodAfterNestedObject_ReturnsTopLevel()
        {
            var json = "{\"params\":{\"method\":\"fake.method\"},\"method\":\"unity.register\"}";
            Assert.AreEqual("unity.register", TcpTransportClient.ExtractTopLevelMethod(json));
        }

        [Test]
        public void ExtractTopLevelMethod_EscapedQuotesInPrecedingValue_ReturnsMethod()
        {
            var json = "{\"a\":\"say \\\"method\\\": no\",\"method\":\"daemon.ping\"}";
            Assert.AreEqual("daemon.ping", TcpTransportClient.ExtractTopLevelMethod(json));
        }

        [Test]
        public void ExtractTopLevelMethod_NonStringMethodValue_ReturnsNull()
        {
            var json = "{\"method\":123}";
            Assert.IsNull(TcpTransportClient.ExtractTopLevelMethod(json));
        }

        [Test]
        public void ExtractTopLevelLiteral_StringId_ReturnsQuotedLiteral()
        {
            var json = "{\"jsonrpc\":\"2.0\",\"id\":\"abc-1\",\"method\":\"daemon.executeTool\"}";
            Assert.AreEqual("\"abc-1\"", TcpTransportClient.ExtractTopLevelLiteral(json, "id"));
        }

        [Test]
        public void ExtractTopLevelLiteral_NumericId_ReturnsNumberLiteral()
        {
            var json = "{\"jsonrpc\":\"2.0\",\"id\":42,\"method\":\"daemon.executeTool\"}";
            Assert.AreEqual("42", TcpTransportClient.ExtractTopLevelLiteral(json, "id"));
        }

        [Test]
        public void ExtractTopLevelLiteral_MissingField_ReturnsNull()
        {
            var json = "{\"jsonrpc\":\"2.0\",\"method\":\"unity.toolsUpdate\"}";
            Assert.IsNull(TcpTransportClient.ExtractTopLevelLiteral(json, "id"));
        }

        [Test]
        public void ExtractTopLevelLiteral_NestedIdOnly_ReturnsNull()
        {
            var json = "{\"params\":{\"id\":\"nested\"}}";
            Assert.IsNull(TcpTransportClient.ExtractTopLevelLiteral(json, "id"));
        }

        #endregion

        #region Receive thread message handling Tests

        [Test]
        public void HandleReceivedLine_Ping_EnqueuesPongWithoutEnqueueingMessage()
        {
            using (var client = CreateOfflineClient())
            {
                client.HandleReceivedLine("{\"jsonrpc\":\"2.0\",\"method\":\"daemon.ping\"}");

                Assert.IsTrue(GetSendQueue(client).TryDequeue(out var sent));
                Assert.AreEqual(TcpTransportClient.PongMessage, sent);
                Assert.IsFalse(GetReceiveQueue(client).TryDequeue(out _));
            }
        }

        [Test]
        public void HandleReceivedLine_PingInsideNestedObject_IsEnqueuedNotPonged()
        {
            using (var client = CreateOfflineClient())
            {
                var line = "{\"jsonrpc\":\"2.0\",\"method\":\"unknown\",\"params\":{\"method\":\"daemon.ping\"}}";
                client.HandleReceivedLine(line);

                Assert.IsFalse(GetSendQueue(client).TryDequeue(out _));
                Assert.IsTrue(GetReceiveQueue(client).TryDequeue(out var queued));
                Assert.AreEqual(line, queued);
            }
        }

        [Test]
        public void HandleReceivedLine_WhenBusy_StringId_EnqueuesBusyAndMessage()
        {
            using (var client = CreateOfflineClient())
            {
                MarkMainThreadBusy(client);
                var line = "{\"jsonrpc\":\"2.0\",\"id\":\"req-1\",\"method\":\"daemon.executeTool\",\"params\":{}}";
                client.HandleReceivedLine(line);

                Assert.IsTrue(GetSendQueue(client).TryDequeue(out var busy));
                StringAssert.Contains("\"method\":\"unity.busy\"", busy);
                StringAssert.Contains("\"requestId\":\"req-1\"", busy);

                // busy 応答後もメッセージ本体は破棄されないこと
                Assert.IsTrue(GetReceiveQueue(client).TryDequeue(out var queued));
                Assert.AreEqual(line, queued);
            }
        }

        [Test]
        public void HandleReceivedLine_WhenBusy_NumericId_EnqueuesBusyAndMessage()
        {
            using (var client = CreateOfflineClient())
            {
                MarkMainThreadBusy(client);
                var line = "{\"jsonrpc\":\"2.0\",\"id\":42,\"method\":\"daemon.executeTool\",\"params\":{}}";
                client.HandleReceivedLine(line);

                Assert.IsTrue(GetSendQueue(client).TryDequeue(out var busy));
                StringAssert.Contains("\"method\":\"unity.busy\"", busy);
                StringAssert.Contains("\"requestId\":42", busy);

                Assert.IsTrue(GetReceiveQueue(client).TryDequeue(out var queued));
                Assert.AreEqual(line, queued);
            }
        }

        [Test]
        public void HandleReceivedLine_WhenBusy_WithoutId_MessageIsNotDropped()
        {
            using (var client = CreateOfflineClient())
            {
                MarkMainThreadBusy(client);
                var line = "{\"jsonrpc\":\"2.0\",\"method\":\"daemon.someNotification\",\"params\":{}}";
                client.HandleReceivedLine(line);

                Assert.IsFalse(GetSendQueue(client).TryDequeue(out _));
                Assert.IsTrue(GetReceiveQueue(client).TryDequeue(out var queued));
                Assert.AreEqual(line, queued);
            }
        }

        #endregion

        #region Loop lifecycle Tests

        [Test]
        [Timeout(10000)]
        public void DisconnectAsync_StopsSendAndReceiveLoops()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                Assert.Ignore("Unix domain sockets are not used on Windows editors.");
            }

            var socketPath = Path.Combine(Path.GetTempPath(), $"uniforge-test-{Guid.NewGuid():N}.sock");
            Socket acceptedSocket = null;

            try
            {
                using (var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
                {
                    listener.Bind(CreateUnixDomainSocketEndpoint(socketPath));
                    listener.Listen(1);

                    var acceptTask = Task.Run(() => listener.Accept());
                    using (var client = new TcpTransportClient(
                        () => new DaemonConnectionInfo
                        {
                            transport = "unix",
                            endpoint = socketPath
                        },
                        () => Task.FromResult(false)))
                    {
                        Task.Run(() => client.ConnectAsync()).GetAwaiter().GetResult();
                        acceptedSocket = WaitWithTimeout(acceptTask, 2000).GetAwaiter().GetResult();
                        Assert.IsTrue(client.IsConnected);

                        var receiveLoopTask = GetPrivateTask(client, "_receiveLoopTask");
                        var sendLoopTask = GetPrivateTask(client, "_sendLoopTask");
                        Assert.IsNotNull(receiveLoopTask, "receive loop task should be tracked");
                        Assert.IsNotNull(sendLoopTask, "send loop task should be tracked");

                        client.DisconnectAsync().GetAwaiter().GetResult();

                        // 旧ループは切断完了時点で終了していること（同一ストリームへの二重ライター防止）
                        Assert.IsTrue(WaitUntil(() => receiveLoopTask.IsCompleted, 3000), "receive loop should complete");
                        Assert.IsTrue(WaitUntil(() => sendLoopTask.IsCompleted, 3000), "send loop should complete");
                        Assert.IsFalse(client.IsConnected);
                    }
                }
            }
            finally
            {
                try { acceptedSocket?.Dispose(); }
                catch { }

                if (File.Exists(socketPath))
                {
                    File.Delete(socketPath);
                }
            }
        }

        #endregion

        private static TcpTransportClient CreateOfflineClient()
        {
            return new TcpTransportClient(
                () => null,
                () => Task.FromResult(false));
        }

        private static void MarkMainThreadBusy(TcpTransportClient client)
        {
            var field = typeof(TcpTransportClient).GetField(
                "_lastMainThreadProcessTime", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            field.SetValue(client, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 60000L);
        }

        private static ConcurrentPriorityQueue<string> GetSendQueue(TcpTransportClient client)
        {
            return GetPrivateQueue(client, "_sendQueue");
        }

        private static ConcurrentPriorityQueue<string> GetReceiveQueue(TcpTransportClient client)
        {
            return GetPrivateQueue(client, "_receiveQueue");
        }

        private static ConcurrentPriorityQueue<string> GetPrivateQueue(TcpTransportClient client, string fieldName)
        {
            var field = typeof(TcpTransportClient).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"{fieldName} field should exist");
            return (ConcurrentPriorityQueue<string>)field.GetValue(client);
        }

        private static Task GetPrivateTask(TcpTransportClient client, string fieldName)
        {
            var field = typeof(TcpTransportClient).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"{fieldName} field should exist");
            return (Task)field.GetValue(client);
        }

        private static bool WaitUntil(Func<bool> condition, int timeoutMs)
        {
            var deadline = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + timeoutMs;
            while (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < deadline)
            {
                if (condition()) return true;
                System.Threading.Thread.Sleep(20);
            }

            return condition();
        }

        private static async Task<T> WaitWithTimeout<T>(Task<T> task, int timeoutMs)
        {
            var completedTask = await Task.WhenAny(task, Task.Delay(timeoutMs));
            if (completedTask != task)
            {
                throw new TimeoutException($"Task did not complete within {timeoutMs}ms");
            }

            return await task;
        }

        private static EndPoint CreateUnixDomainSocketEndpoint(string endpoint)
        {
            var type = Type.GetType("System.Net.Sockets.UnixDomainSocketEndPoint")
                ?? typeof(Socket).Assembly.GetType("System.Net.Sockets.UnixDomainSocketEndPoint")
                ?? Type.GetType("Mono.Unix.UnixEndPoint, Mono.Posix");

            Assert.IsNotNull(type, "UnixDomainSocketEndPoint is not available in this Unity runtime");

            var instance = Activator.CreateInstance(type, endpoint) as EndPoint;
            Assert.IsNotNull(instance, "Failed to create Unix domain socket endpoint");
            return instance;
        }
    }
}
