using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace UniForge.Tests
{
    [TestFixture]
    public class ConcurrentPriorityQueueTests
    {
        #region Constructor Tests

        [Test]
        public void Constructor_Default_CreatesQueueWith3Levels()
        {
            var queue = new ConcurrentPriorityQueue<string>();
            Assert.AreEqual(0, queue.Count);
            Assert.IsTrue(queue.IsEmpty);
        }

        [Test]
        public void Constructor_WithPriorityLevels_CreatesQueue()
        {
            var queue = new ConcurrentPriorityQueue<string>(5);
            Assert.AreEqual(0, queue.Count);
        }

        [Test]
        public void Constructor_WithZeroPriorityLevels_ThrowsArgumentOutOfRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConcurrentPriorityQueue<string>(0));
        }

        [Test]
        public void Constructor_WithNegativePriorityLevels_ThrowsArgumentOutOfRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConcurrentPriorityQueue<string>(-1));
        }

        #endregion

        #region Enqueue Tests

        [Test]
        public void Enqueue_SingleItem_IncreasesCount()
        {
            var queue = new ConcurrentPriorityQueue<string>();
            queue.Enqueue("test", 1);
            Assert.AreEqual(1, queue.Count);
            Assert.IsFalse(queue.IsEmpty);
        }

        [Test]
        public void Enqueue_MultipleItems_IncreasesCount()
        {
            var queue = new ConcurrentPriorityQueue<string>();
            queue.Enqueue("low", 0);
            queue.Enqueue("medium", 1);
            queue.Enqueue("high", 2);
            Assert.AreEqual(3, queue.Count);
        }

        [Test]
        public void Enqueue_WithPriorityBelowZero_ClampsToPriorityZero()
        {
            var queue = new ConcurrentPriorityQueue<string>();
            queue.Enqueue("test", -5);
            Assert.AreEqual(1, queue.Count);
            Assert.AreEqual(1, queue.GetCountAtPriority(0));
        }

        [Test]
        public void Enqueue_WithPriorityAboveMax_ClampsToMaxPriority()
        {
            var queue = new ConcurrentPriorityQueue<string>(3); // levels 0, 1, 2
            queue.Enqueue("test", 10);
            Assert.AreEqual(1, queue.Count);
            Assert.AreEqual(1, queue.GetCountAtPriority(2));
        }

        #endregion

        #region TryDequeue Tests

        [Test]
        public void TryDequeue_EmptyQueue_ReturnsFalse()
        {
            var queue = new ConcurrentPriorityQueue<string>();
            Assert.IsFalse(queue.TryDequeue(out var item));
            Assert.IsNull(item);
        }

        [Test]
        public void TryDequeue_SingleItem_ReturnsItem()
        {
            var queue = new ConcurrentPriorityQueue<string>();
            queue.Enqueue("test", 1);

            Assert.IsTrue(queue.TryDequeue(out var item));
            Assert.AreEqual("test", item);
            Assert.AreEqual(0, queue.Count);
        }

        [Test]
        public void TryDequeue_ReturnsHighestPriorityFirst()
        {
            var queue = new ConcurrentPriorityQueue<string>();
            queue.Enqueue("low", 0);
            queue.Enqueue("high", 2);
            queue.Enqueue("medium", 1);

            Assert.IsTrue(queue.TryDequeue(out var first));
            Assert.AreEqual("high", first);

            Assert.IsTrue(queue.TryDequeue(out var second));
            Assert.AreEqual("medium", second);

            Assert.IsTrue(queue.TryDequeue(out var third));
            Assert.AreEqual("low", third);
        }

        [Test]
        public void TryDequeue_SamePriority_ReturnsFIFOOrder()
        {
            var queue = new ConcurrentPriorityQueue<string>();
            queue.Enqueue("first", 1);
            queue.Enqueue("second", 1);
            queue.Enqueue("third", 1);

            Assert.IsTrue(queue.TryDequeue(out var item1));
            Assert.AreEqual("first", item1);

            Assert.IsTrue(queue.TryDequeue(out var item2));
            Assert.AreEqual("second", item2);

            Assert.IsTrue(queue.TryDequeue(out var item3));
            Assert.AreEqual("third", item3);
        }

        #endregion

        #region TryDropLowest Tests

        [Test]
        public void TryDropLowest_EmptyQueue_ReturnsFalse()
        {
            var queue = new ConcurrentPriorityQueue<string>();
            Assert.IsFalse(queue.TryDropLowest(out var dropped));
            Assert.IsNull(dropped);
        }

        [Test]
        public void TryDropLowest_SingleItem_DropsItem()
        {
            var queue = new ConcurrentPriorityQueue<string>();
            queue.Enqueue("test", 1);

            Assert.IsTrue(queue.TryDropLowest(out var dropped));
            Assert.AreEqual("test", dropped);
            Assert.AreEqual(0, queue.Count);
        }

        [Test]
        public void TryDropLowest_DropsLowestPriorityFirst()
        {
            var queue = new ConcurrentPriorityQueue<string>();
            queue.Enqueue("low", 0);
            queue.Enqueue("high", 2);
            queue.Enqueue("medium", 1);

            Assert.IsTrue(queue.TryDropLowest(out var first));
            Assert.AreEqual("low", first);

            Assert.IsTrue(queue.TryDropLowest(out var second));
            Assert.AreEqual("medium", second);

            Assert.IsTrue(queue.TryDropLowest(out var third));
            Assert.AreEqual("high", third);
        }

        [Test]
        public void TryDropLowest_SkipsEmptyLowPriority()
        {
            var queue = new ConcurrentPriorityQueue<string>();
            // Only add to medium priority
            queue.Enqueue("medium", 1);

            Assert.IsTrue(queue.TryDropLowest(out var dropped));
            Assert.AreEqual("medium", dropped);
        }

        #endregion

        #region GetCountAtPriority Tests

        [Test]
        public void GetCountAtPriority_EmptyQueue_ReturnsZero()
        {
            var queue = new ConcurrentPriorityQueue<string>();
            Assert.AreEqual(0, queue.GetCountAtPriority(0));
            Assert.AreEqual(0, queue.GetCountAtPriority(1));
            Assert.AreEqual(0, queue.GetCountAtPriority(2));
        }

        [Test]
        public void GetCountAtPriority_ReturnsCorrectCounts()
        {
            var queue = new ConcurrentPriorityQueue<string>();
            queue.Enqueue("low1", 0);
            queue.Enqueue("low2", 0);
            queue.Enqueue("high", 2);

            Assert.AreEqual(2, queue.GetCountAtPriority(0));
            Assert.AreEqual(0, queue.GetCountAtPriority(1));
            Assert.AreEqual(1, queue.GetCountAtPriority(2));
        }

        [Test]
        public void GetCountAtPriority_InvalidPriority_ReturnsZero()
        {
            var queue = new ConcurrentPriorityQueue<string>();
            queue.Enqueue("test", 1);

            Assert.AreEqual(0, queue.GetCountAtPriority(-1));
            Assert.AreEqual(0, queue.GetCountAtPriority(100));
        }

        #endregion

        #region Thread Safety Tests

        [Test]
        public void ConcurrentEnqueue_IsThreadSafe()
        {
            var queue = new ConcurrentPriorityQueue<int>();
            const int itemsPerThread = 100;
            const int threadCount = 10;
            var threads = new List<Thread>();

            for (int t = 0; t < threadCount; t++)
            {
                var priority = t % 3;
                var thread = new Thread(() =>
                {
                    for (int i = 0; i < itemsPerThread; i++)
                    {
                        queue.Enqueue(i, priority);
                    }
                });
                threads.Add(thread);
            }

            foreach (var thread in threads) thread.Start();
            foreach (var thread in threads) thread.Join();

            Assert.AreEqual(itemsPerThread * threadCount, queue.Count);
        }

        [Test]
        public void ConcurrentDequeue_IsThreadSafe()
        {
            var queue = new ConcurrentPriorityQueue<int>();
            const int totalItems = 1000;

            // Fill the queue
            for (int i = 0; i < totalItems; i++)
            {
                queue.Enqueue(i, i % 3);
            }

            var dequeuedCount = 0;
            const int threadCount = 10;
            var threads = new List<Thread>();

            for (int t = 0; t < threadCount; t++)
            {
                var thread = new Thread(() =>
                {
                    while (queue.TryDequeue(out _))
                    {
                        Interlocked.Increment(ref dequeuedCount);
                    }
                });
                threads.Add(thread);
            }

            foreach (var thread in threads) thread.Start();
            foreach (var thread in threads) thread.Join();

            Assert.AreEqual(totalItems, dequeuedCount);
            Assert.AreEqual(0, queue.Count);
        }

        #endregion

        #region Overflow Simulation Tests

        [Test]
        public void OverflowSimulation_DropsLowPriorityPreservesHigh()
        {
            var queue = new ConcurrentPriorityQueue<string>();
            const int maxSize = 10;

            // Add some high priority items
            queue.Enqueue("high1", 2);
            queue.Enqueue("high2", 2);

            // Fill with low priority items
            for (int i = 0; i < maxSize; i++)
            {
                queue.Enqueue($"low{i}", 0);
            }

            // Simulate overflow - drop until we're under limit
            while (queue.Count > maxSize)
            {
                queue.TryDropLowest(out _);
            }

            // High priority items should still be there
            Assert.AreEqual(2, queue.GetCountAtPriority(2));

            // Dequeue and verify high priority items come first
            Assert.IsTrue(queue.TryDequeue(out var first));
            Assert.AreEqual("high1", first);

            Assert.IsTrue(queue.TryDequeue(out var second));
            Assert.AreEqual("high2", second);
        }

        #endregion
    }
}
