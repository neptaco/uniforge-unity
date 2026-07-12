using NUnit.Framework;

namespace UniForge.Tests
{
    [TestFixture]
    public class CircularBufferTests
    {
        #region Constructor Tests

        [Test]
        public void Constructor_WithValidCapacity_CreatesBuffer()
        {
            var buffer = new CircularBuffer<int>(10);
            Assert.AreEqual(0, buffer.Count);
            Assert.AreEqual(10, buffer.Capacity);
        }

        [Test]
        public void Constructor_WithZeroCapacity_ThrowsArgumentException()
        {
            Assert.Throws<System.ArgumentException>(() => new CircularBuffer<int>(0));
        }

        [Test]
        public void Constructor_WithNegativeCapacity_ThrowsArgumentException()
        {
            Assert.Throws<System.ArgumentException>(() => new CircularBuffer<int>(-1));
        }

        #endregion

        #region Add Tests

        [Test]
        public void Add_SingleItem_IncreasesCount()
        {
            var buffer = new CircularBuffer<int>(10);
            buffer.Add(42);
            Assert.AreEqual(1, buffer.Count);
        }

        [Test]
        public void Add_MultipleItems_IncreasesCount()
        {
            var buffer = new CircularBuffer<int>(10);
            buffer.Add(1);
            buffer.Add(2);
            buffer.Add(3);
            Assert.AreEqual(3, buffer.Count);
        }

        [Test]
        public void Add_MoreThanCapacity_CountEqualsCapacity()
        {
            var buffer = new CircularBuffer<int>(3);
            buffer.Add(1);
            buffer.Add(2);
            buffer.Add(3);
            buffer.Add(4); // Overwrites oldest
            Assert.AreEqual(3, buffer.Count);
        }

        [Test]
        public void Add_MoreThanCapacity_OldestItemOverwritten()
        {
            var buffer = new CircularBuffer<int>(3);
            buffer.Add(1);
            buffer.Add(2);
            buffer.Add(3);
            buffer.Add(4); // Overwrites 1

            // Buffer should now contain [2, 3, 4]
            Assert.AreEqual(2, buffer[0]); // Oldest
            Assert.AreEqual(3, buffer[1]);
            Assert.AreEqual(4, buffer[2]); // Newest
        }

        [Test]
        public void Add_WrapAroundMultipleTimes_MaintainsCorrectOrder()
        {
            var buffer = new CircularBuffer<int>(3);

            // Add 6 items (wraps around twice)
            for (int i = 1; i <= 6; i++)
            {
                buffer.Add(i);
            }

            // Should contain [4, 5, 6]
            Assert.AreEqual(3, buffer.Count);
            Assert.AreEqual(4, buffer[0]);
            Assert.AreEqual(5, buffer[1]);
            Assert.AreEqual(6, buffer[2]);
        }

        #endregion

        #region Indexer Tests

        [Test]
        public void Indexer_ZeroIndex_ReturnsOldestItem()
        {
            var buffer = new CircularBuffer<int>(5);
            buffer.Add(10);
            buffer.Add(20);
            buffer.Add(30);

            Assert.AreEqual(10, buffer[0]);
        }

        [Test]
        public void Indexer_LastIndex_ReturnsNewestItem()
        {
            var buffer = new CircularBuffer<int>(5);
            buffer.Add(10);
            buffer.Add(20);
            buffer.Add(30);

            Assert.AreEqual(30, buffer[buffer.Count - 1]);
        }

        [Test]
        public void Indexer_NegativeIndex_ThrowsIndexOutOfRangeException()
        {
            var buffer = new CircularBuffer<int>(5);
            buffer.Add(1);

            Assert.Throws<System.IndexOutOfRangeException>(() => { var _ = buffer[-1]; });
        }

        [Test]
        public void Indexer_IndexEqualToCount_ThrowsIndexOutOfRangeException()
        {
            var buffer = new CircularBuffer<int>(5);
            buffer.Add(1);
            buffer.Add(2);

            Assert.Throws<System.IndexOutOfRangeException>(() => { var _ = buffer[2]; });
        }

        [Test]
        public void Indexer_EmptyBuffer_ThrowsIndexOutOfRangeException()
        {
            var buffer = new CircularBuffer<int>(5);

            Assert.Throws<System.IndexOutOfRangeException>(() => { var _ = buffer[0]; });
        }

        [Test]
        public void Indexer_AfterWrapAround_ReturnsCorrectItems()
        {
            var buffer = new CircularBuffer<int>(3);
            buffer.Add(1);
            buffer.Add(2);
            buffer.Add(3);
            buffer.Add(4);
            buffer.Add(5);

            // Should contain [3, 4, 5]
            Assert.AreEqual(3, buffer[0]);
            Assert.AreEqual(4, buffer[1]);
            Assert.AreEqual(5, buffer[2]);
        }

        #endregion

        #region Clear Tests

        [Test]
        public void Clear_ResetsCountToZero()
        {
            var buffer = new CircularBuffer<int>(5);
            buffer.Add(1);
            buffer.Add(2);
            buffer.Add(3);

            buffer.Clear();

            Assert.AreEqual(0, buffer.Count);
        }

        [Test]
        public void Clear_CapacityUnchanged()
        {
            var buffer = new CircularBuffer<int>(5);
            buffer.Add(1);
            buffer.Add(2);

            buffer.Clear();

            Assert.AreEqual(5, buffer.Capacity);
        }

        [Test]
        public void Clear_ThenAdd_WorksCorrectly()
        {
            var buffer = new CircularBuffer<int>(3);
            buffer.Add(1);
            buffer.Add(2);
            buffer.Add(3);

            buffer.Clear();

            buffer.Add(10);
            buffer.Add(20);

            Assert.AreEqual(2, buffer.Count);
            Assert.AreEqual(10, buffer[0]);
            Assert.AreEqual(20, buffer[1]);
        }

        #endregion

        #region ToList Tests

        [Test]
        public void ToList_EmptyBuffer_ReturnsEmptyList()
        {
            var buffer = new CircularBuffer<int>(5);
            var list = buffer.ToList();

            Assert.IsNotNull(list);
            Assert.AreEqual(0, list.Count);
        }

        [Test]
        public void ToList_PartiallyFilledBuffer_ReturnsCorrectItems()
        {
            var buffer = new CircularBuffer<int>(5);
            buffer.Add(1);
            buffer.Add(2);
            buffer.Add(3);

            var list = buffer.ToList();

            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(1, list[0]);
            Assert.AreEqual(2, list[1]);
            Assert.AreEqual(3, list[2]);
        }

        [Test]
        public void ToList_FullBuffer_ReturnsAllItems()
        {
            var buffer = new CircularBuffer<int>(3);
            buffer.Add(1);
            buffer.Add(2);
            buffer.Add(3);

            var list = buffer.ToList();

            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(1, list[0]);
            Assert.AreEqual(2, list[1]);
            Assert.AreEqual(3, list[2]);
        }

        [Test]
        public void ToList_AfterWrapAround_ReturnsItemsInOrder()
        {
            var buffer = new CircularBuffer<int>(3);
            buffer.Add(1);
            buffer.Add(2);
            buffer.Add(3);
            buffer.Add(4);
            buffer.Add(5);

            var list = buffer.ToList();

            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(3, list[0]); // Oldest
            Assert.AreEqual(4, list[1]);
            Assert.AreEqual(5, list[2]); // Newest
        }

        #endregion

        #region Reference Type Tests

        [Test]
        public void Add_ReferenceTypes_WorksCorrectly()
        {
            var buffer = new CircularBuffer<string>(3);
            buffer.Add("first");
            buffer.Add("second");
            buffer.Add("third");

            Assert.AreEqual("first", buffer[0]);
            Assert.AreEqual("second", buffer[1]);
            Assert.AreEqual("third", buffer[2]);
        }

        [Test]
        public void Add_NullValues_WorksCorrectly()
        {
            var buffer = new CircularBuffer<string>(3);
            buffer.Add("first");
            buffer.Add(null);
            buffer.Add("third");

            Assert.AreEqual("first", buffer[0]);
            Assert.IsNull(buffer[1]);
            Assert.AreEqual("third", buffer[2]);
        }

        #endregion

        #region Capacity 1 Edge Case

        [Test]
        public void Capacity1_Add_ReplacesOnlyItem()
        {
            var buffer = new CircularBuffer<int>(1);
            buffer.Add(1);
            Assert.AreEqual(1, buffer[0]);

            buffer.Add(2);
            Assert.AreEqual(1, buffer.Count);
            Assert.AreEqual(2, buffer[0]);

            buffer.Add(3);
            Assert.AreEqual(1, buffer.Count);
            Assert.AreEqual(3, buffer[0]);
        }

        #endregion
    }
}
