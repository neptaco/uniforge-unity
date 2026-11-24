using System.Collections.Generic;
using NUnit.Framework;
using UniForge.Tools;
using UnityEngine;

namespace UniForge.Tests
{
    /// <summary>
    /// Game ビューを撮る前に Play のフレームを進める待ち（PlayModeFrameSync）のテスト。
    /// Run In Background がオフだと Editor が背面の間は Play のフレームが止まり、
    /// その後に生成した Overlay の Canvas が Game ビューに描かれないまま撮影されていた。
    /// </summary>
    public class PlayModeFrameSyncTests
    {
        /// <summary>
        /// Editor の状態を模したクロック。WaitForNextUpdateAsync のたびに 0.1 秒進み、
        /// 背面の Editor と同じく runInBackground が true の間だけフレームが進む。
        /// </summary>
        private sealed class FakeClock : IPlayModeFrameClock
        {
            public bool IsPlaying { get; set; } = true;
            public bool IsPaused { get; set; }
            public int FrameCount { get; set; } = 100;
            public bool RunInBackground { get; set; }
            public double NowSeconds { get; private set; }
            public bool FramesAdvance { get; set; } = true;
            public int UpdateCount { get; private set; }
            public List<bool> RunInBackgroundDuringWaits { get; } = new List<bool>();

#pragma warning disable CS1998
            public async Awaitable WaitForNextUpdateAsync()
            {
                UpdateCount++;
                RunInBackgroundDuringWaits.Add(RunInBackground);
                NowSeconds += 0.1;
                if (FramesAdvance && RunInBackground)
                {
                    FrameCount++;
                }
            }
#pragma warning restore CS1998
        }

        private static PlayModeFrameSyncStatus Run(FakeClock clock, int frames = 2, int timeoutMs = 2000)
        {
            return PlayModeFrameSync.WaitForRenderedFramesAsync(clock, frames, timeoutMs).GetAwaiter().GetResult();
        }

        [Test]
        public void WaitForRenderedFrames_InBackgroundEditor_EnablesRunInBackgroundUntilFramesAdvance()
        {
            var clock = new FakeClock { RunInBackground = false };

            var status = Run(clock, frames: 2);

            Assert.AreEqual(PlayModeFrameSyncStatus.Advanced, status);
            Assert.AreEqual(102, clock.FrameCount);
            CollectionAssert.IsNotEmpty(clock.RunInBackgroundDuringWaits);
            CollectionAssert.DoesNotContain(clock.RunInBackgroundDuringWaits, false);
            Assert.IsFalse(clock.RunInBackground, "runInBackground must be restored after the wait");
        }

        [Test]
        public void WaitForRenderedFrames_RunInBackgroundAlreadyOn_KeepsItOn()
        {
            var clock = new FakeClock { RunInBackground = true };

            var status = Run(clock);

            Assert.AreEqual(PlayModeFrameSyncStatus.Advanced, status);
            Assert.IsTrue(clock.RunInBackground);
        }

        [Test]
        public void WaitForRenderedFrames_FramesNeverAdvance_TimesOutAndRestores()
        {
            var clock = new FakeClock { RunInBackground = false, FramesAdvance = false };

            var status = Run(clock, frames: 2, timeoutMs: 500);

            Assert.AreEqual(PlayModeFrameSyncStatus.TimedOut, status);
            Assert.AreEqual(100, clock.FrameCount);
            Assert.That(clock.UpdateCount, Is.InRange(5, 7));
            Assert.IsFalse(clock.RunInBackground, "runInBackground must be restored after a timeout");
        }

        [Test]
        public void WaitForRenderedFrames_NotPlaying_DoesNotWait()
        {
            var clock = new FakeClock { IsPlaying = false };

            var status = Run(clock);

            Assert.AreEqual(PlayModeFrameSyncStatus.NotPlaying, status);
            Assert.AreEqual(0, clock.UpdateCount);
            Assert.IsFalse(clock.RunInBackground);
        }

        [Test]
        public void WaitForRenderedFrames_Paused_DoesNotWait()
        {
            var clock = new FakeClock { IsPaused = true };

            var status = Run(clock);

            Assert.AreEqual(PlayModeFrameSyncStatus.Paused, status);
            Assert.AreEqual(0, clock.UpdateCount);
            Assert.IsFalse(clock.RunInBackground);
        }

        [Test]
        public void WaitForRenderedFrames_PlayModeExitsDuringWait_StopsWaiting()
        {
            var clock = new FakeClock { RunInBackground = false, FramesAdvance = false };
            var exitingClock = new ExitAfterFirstUpdateClock(clock);

            var status = PlayModeFrameSync.WaitForRenderedFramesAsync(exitingClock, 2, 2000).GetAwaiter().GetResult();

            Assert.AreEqual(PlayModeFrameSyncStatus.NotPlaying, status);
            Assert.AreEqual(1, clock.UpdateCount);
        }

        /// <summary>最初の更新の後に Play を抜けるクロック</summary>
        private sealed class ExitAfterFirstUpdateClock : IPlayModeFrameClock
        {
            private readonly FakeClock _inner;

            public ExitAfterFirstUpdateClock(FakeClock inner) => _inner = inner;

            public bool IsPlaying => _inner.UpdateCount == 0;
            public bool IsPaused => _inner.IsPaused;
            public int FrameCount => _inner.FrameCount;
            public bool RunInBackground
            {
                get => _inner.RunInBackground;
                set => _inner.RunInBackground = value;
            }
            public double NowSeconds => _inner.NowSeconds;
            public Awaitable WaitForNextUpdateAsync() => _inner.WaitForNextUpdateAsync();
        }
    }
}
