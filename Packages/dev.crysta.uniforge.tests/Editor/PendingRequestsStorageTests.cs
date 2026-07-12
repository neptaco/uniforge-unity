using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UniForge.Tools;

namespace UniForge.Tests
{
    [TestFixture]
    public class PendingRequestsStorageTests
    {
        private ToolRuntimeStateScope _runtimeStateScope;

        [SetUp]
        public void SetUp()
        {
            _runtimeStateScope = new ToolRuntimeStateScope();
            PendingDomainReloadToolRequestsStorage.instance.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _runtimeStateScope?.Dispose();
            _runtimeStateScope = null;
        }

        [Test]
        public void DomainReloadTrackerStore_MarkDomainReload_UpdatesTime()
        {
            var store = new DomainReloadTrackerStore();
            long before = store.LastDomainReloadTime;

            Thread.Sleep(10);
            store.MarkDomainReload();

            Assert.Greater(store.LastDomainReloadTime, before);
        }

        [Test]
        public void DomainReloadTracker_Instance_ReturnsSameInstance()
        {
            var instance1 = DomainReloadTracker.instance;
            var instance2 = DomainReloadTracker.instance;

            Assert.AreSame(instance1, instance2);
        }

        [Test]
        public void PendingDomainReloadToolRequest_CanBeCreated()
        {
            var pending = new PendingDomainReloadToolRequest
            {
                requestId = "tool-123",
                toolName = "control-playmode",
                startTime = 1000,
                timeoutMs = 40000,
                nextPollTime = 1250,
                stateJson = "{\"expected_state\":\"playing\"}",
                readyToSend = true,
                finalSuccess = true,
                finalResultText = "{\"ok\":true}",
                finalError = null
            };

            Assert.AreEqual("tool-123", pending.requestId);
            Assert.AreEqual("control-playmode", pending.toolName);
            Assert.AreEqual(1000, pending.startTime);
            Assert.AreEqual(40000, pending.timeoutMs);
            Assert.AreEqual(1250, pending.nextPollTime);
            Assert.AreEqual("{\"expected_state\":\"playing\"}", pending.stateJson);
            Assert.IsTrue(pending.readyToSend);
            Assert.IsTrue(pending.finalSuccess);
            Assert.AreEqual("{\"ok\":true}", pending.finalResultText);
            Assert.IsNull(pending.finalError);
        }

        [Test]
        public void PendingDomainReloadToolRequestsStore_MultipleAdds_MaintainsOrder()
        {
            var store = new PendingDomainReloadToolRequestsStore();

            store.Add(new PendingDomainReloadToolRequest { requestId = "first" });
            store.Add(new PendingDomainReloadToolRequest { requestId = "second" });
            store.Add(new PendingDomainReloadToolRequest { requestId = "third" });

            Assert.AreEqual(3, store.Requests.Count);
            Assert.AreEqual("first", store.Requests[0].requestId);
            Assert.AreEqual("second", store.Requests[1].requestId);
            Assert.AreEqual("third", store.Requests[2].requestId);
        }

        [Test]
        public void PendingDomainReloadToolRequestsStore_Clear_EmptiesRequests()
        {
            var store = new PendingDomainReloadToolRequestsStore();

            store.Add(new PendingDomainReloadToolRequest { requestId = "first" });
            store.Add(new PendingDomainReloadToolRequest { requestId = "second" });
            store.Clear();

            Assert.AreEqual(0, store.Requests.Count);
        }

        [Test]
        public void PendingDomainReloadToolRequestsStorage_Instance_ReturnsSameInstance()
        {
            var instance1 = PendingDomainReloadToolRequestsStorage.instance;
            var instance2 = PendingDomainReloadToolRequestsStorage.instance;

            Assert.AreSame(instance1, instance2);
        }

        [Test]
        public void PendingDomainReloadToolRequestsStore_AddAndRemove_UpdatesIsolatedStore()
        {
            var store = new PendingDomainReloadToolRequestsStore();
            store.Add(new PendingDomainReloadToolRequest { requestId = "store-test" });

            Assert.AreEqual(1, store.Requests.Count);
            Assert.AreEqual("store-test", store.Requests[0].requestId);

            store.RemoveAt(0);

            Assert.AreEqual(0, store.Requests.Count);
        }

        [Test]
        public void PendingDomainReloadToolRequestProcessor_TrackPendingRequest_AddsRequestWithExpectedState()
        {
            PendingDomainReloadToolRequestProcessor.TrackPendingRequest(
                "req-1",
                "control-playmode",
                ToolResult.WaitForDomainReload(
                    "{\"phase\":\"waiting\"}",
                    new Dictionary<string, object> { { "accepted", true } },
                    1234,
                    250));

            Assert.AreEqual(1, PendingDomainReloadToolRequestsStorage.instance.Requests.Count);
            var pending = PendingDomainReloadToolRequestsStorage.instance.Requests[0];
            Assert.AreEqual("req-1", pending.requestId);
            Assert.AreEqual("control-playmode", pending.toolName);
            Assert.AreEqual(1234, pending.timeoutMs);
            Assert.AreEqual("{\"phase\":\"waiting\"}", pending.stateJson);
            Assert.GreaterOrEqual(pending.nextPollTime, pending.startTime + 250);
        }

        [Test]
        public void PendingDomainReloadToolRequestProcessor_TrackPendingRequest_UsesDefaultTimeoutWhenUnset()
        {
            PendingDomainReloadToolRequestProcessor.TrackPendingRequest(
                "req-1",
                "control-playmode",
                ToolResult.WaitForDomainReload(
                    "{\"phase\":\"waiting\"}",
                    new Dictionary<string, object> { { "accepted", true } },
                    0,
                    0));

            Assert.AreEqual(1, PendingDomainReloadToolRequestsStorage.instance.Requests.Count);
            Assert.AreEqual(30000, PendingDomainReloadToolRequestsStorage.instance.Requests[0].timeoutMs);
        }

        [Test]
        public void PendingDomainReloadToolRequestProcessor_GetPendingRequestIds_IgnoresEmptyIds()
        {
            PendingDomainReloadToolRequestsStorage.instance.Add(new PendingDomainReloadToolRequest { requestId = "req-1" });
            PendingDomainReloadToolRequestsStorage.instance.Add(new PendingDomainReloadToolRequest { requestId = "" });
            PendingDomainReloadToolRequestsStorage.instance.Add(new PendingDomainReloadToolRequest { requestId = null });

            var ids = PendingDomainReloadToolRequestProcessor.GetPendingRequestIds();

            CollectionAssert.AreEqual(new[] { "req-1" }, ids);
        }
    }
}
