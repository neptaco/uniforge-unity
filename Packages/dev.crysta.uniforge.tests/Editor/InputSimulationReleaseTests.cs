using System;
using System.Collections.Generic;
using NUnit.Framework;
using UniForge.Tools.Mutations.InputSimulation;

namespace UniForge.Tests
{
    /// <summary>
    /// PendingInputReleaseRegistry のテスト。
    /// EditorApplication.delayCall ベースの遅延解放はドメインリロードで失われ、
    /// キー/マウスボタンがシステム全体で押しっぱなしになる（stuck input）ため、
    /// レジストリで管理してリロード前に確実にフラッシュすることを検証する。
    /// </summary>
    [TestFixture]
    public class InputSimulationReleaseTests
    {
        private double _currentTime;

        [SetUp]
        public void SetUp()
        {
            _currentTime = 100.0;
            PendingInputReleaseRegistry.ResetForTest();
            PendingInputReleaseRegistry.TimeProvider = () => _currentTime;
        }

        [TearDown]
        public void TearDown()
        {
            PendingInputReleaseRegistry.ResetForTest();
        }

        #region Register / FlushAll

        [Test]
        public void Register_IncreasesPendingCount()
        {
            PendingInputReleaseRegistry.Register(() => { }, 0.1);
            PendingInputReleaseRegistry.Register(() => { }, 0.2);

            Assert.AreEqual(2, PendingInputReleaseRegistry.PendingCount);
        }

        [Test]
        public void Register_NullRelease_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => PendingInputReleaseRegistry.Register(null, 0.1));
        }

        [Test]
        public void FlushAll_FiresAllPendingReleases_RegardlessOfDueTime()
        {
            var fired = new List<string>();
            PendingInputReleaseRegistry.Register(() => fired.Add("a"), 0.0);
            PendingInputReleaseRegistry.Register(() => fired.Add("b"), 10.0);
            PendingInputReleaseRegistry.Register(() => fired.Add("c"), 60.0);

            PendingInputReleaseRegistry.FlushAll();

            CollectionAssert.AreEquivalent(new[] { "a", "b", "c" }, fired);
            Assert.AreEqual(0, PendingInputReleaseRegistry.PendingCount);
        }

        [Test]
        public void FlushAll_CalledTwice_FiresEachReleaseOnlyOnce()
        {
            int count = 0;
            PendingInputReleaseRegistry.Register(() => count++, 5.0);

            PendingInputReleaseRegistry.FlushAll();
            PendingInputReleaseRegistry.FlushAll();

            Assert.AreEqual(1, count);
        }

        [Test]
        public void FlushAll_WithNoPendingReleases_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => PendingInputReleaseRegistry.FlushAll());
        }

        #endregion

        #region Due-time processing

        [Test]
        public void ProcessDueReleases_ZeroDelay_FiresOnNextTick()
        {
            bool fired = false;
            PendingInputReleaseRegistry.Register(() => fired = true, 0.0);

            // 時間を進めずに処理しても、遅延 0 は即時に実行される
            PendingInputReleaseRegistry.ProcessDueReleases();

            Assert.IsTrue(fired);
            Assert.AreEqual(0, PendingInputReleaseRegistry.PendingCount);
        }

        [Test]
        public void ProcessDueReleases_FiresOnlyDueReleases()
        {
            var fired = new List<string>();
            PendingInputReleaseRegistry.Register(() => fired.Add("soon"), 0.1);
            PendingInputReleaseRegistry.Register(() => fired.Add("later"), 5.0);

            // 0.2 秒経過: soon のみ発火
            _currentTime += 0.2;
            PendingInputReleaseRegistry.ProcessDueReleases();

            CollectionAssert.AreEqual(new[] { "soon" }, fired);
            Assert.AreEqual(1, PendingInputReleaseRegistry.PendingCount);

            // さらに 5 秒経過: later も発火
            _currentTime += 5.0;
            PendingInputReleaseRegistry.ProcessDueReleases();

            CollectionAssert.AreEqual(new[] { "soon", "later" }, fired);
            Assert.AreEqual(0, PendingInputReleaseRegistry.PendingCount);
        }

        [Test]
        public void ProcessDueReleases_NotYetDue_DoesNotFire()
        {
            bool fired = false;
            PendingInputReleaseRegistry.Register(() => fired = true, 1.0);

            _currentTime += 0.5;
            PendingInputReleaseRegistry.ProcessDueReleases();

            Assert.IsFalse(fired);
            Assert.AreEqual(1, PendingInputReleaseRegistry.PendingCount);
        }

        [Test]
        public void ProcessDueReleases_FiresInDueTimeOrder()
        {
            var fired = new List<string>();
            // 登録順と発火期限順が逆になるように登録
            PendingInputReleaseRegistry.Register(() => fired.Add("late"), 2.0);
            PendingInputReleaseRegistry.Register(() => fired.Add("early"), 1.0);

            _currentTime += 3.0;
            PendingInputReleaseRegistry.ProcessDueReleases();

            CollectionAssert.AreEqual(new[] { "early", "late" }, fired);
        }

        [Test]
        public void Register_NegativeDelay_TreatedAsImmediate()
        {
            bool fired = false;
            PendingInputReleaseRegistry.Register(() => fired = true, -1.0);

            PendingInputReleaseRegistry.ProcessDueReleases();

            Assert.IsTrue(fired);
        }

        #endregion

        #region Exception isolation

        [Test]
        public void FlushAll_ExceptionInOneRelease_DoesNotBlockOthers()
        {
            var fired = new List<string>();
            PendingInputReleaseRegistry.Register(() => fired.Add("first"), 0.0);
            PendingInputReleaseRegistry.Register(() => throw new InvalidOperationException("boom"), 0.0);
            PendingInputReleaseRegistry.Register(() => fired.Add("last"), 0.0);

            Assert.DoesNotThrow(() => PendingInputReleaseRegistry.FlushAll());

            CollectionAssert.AreEquivalent(new[] { "first", "last" }, fired);
            Assert.AreEqual(0, PendingInputReleaseRegistry.PendingCount);
        }

        [Test]
        public void ProcessDueReleases_ExceptionInOneRelease_DoesNotBlockOthers()
        {
            var fired = new List<string>();
            PendingInputReleaseRegistry.Register(() => throw new InvalidOperationException("boom"), 0.0);
            PendingInputReleaseRegistry.Register(() => fired.Add("survivor"), 0.0);

            Assert.DoesNotThrow(() => PendingInputReleaseRegistry.ProcessDueReleases());

            CollectionAssert.AreEqual(new[] { "survivor" }, fired);
        }

        #endregion

        #region Reentrancy

        [Test]
        public void FlushAll_ReleaseRegisteringAnotherRelease_DoesNotFireNewOneInSameFlush()
        {
            var fired = new List<string>();
            PendingInputReleaseRegistry.Register(() =>
            {
                fired.Add("outer");
                PendingInputReleaseRegistry.Register(() => fired.Add("inner"), 0.0);
            }, 0.0);

            PendingInputReleaseRegistry.FlushAll();

            // フラッシュ中に登録されたものは次回の処理に持ち越される
            CollectionAssert.AreEqual(new[] { "outer" }, fired);
            Assert.AreEqual(1, PendingInputReleaseRegistry.PendingCount);

            PendingInputReleaseRegistry.FlushAll();
            CollectionAssert.AreEqual(new[] { "outer", "inner" }, fired);
        }

        #endregion
    }
}
