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

        #endregion

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
