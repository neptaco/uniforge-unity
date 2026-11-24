using System;
using UnityEditor;
using UnityEngine;

namespace UniForge.Tools
{
    /// <summary>PlayModeFrameSync.WaitForRenderedFramesAsync の結果</summary>
    internal enum PlayModeFrameSyncStatus
    {
        /// <summary>Play 中でないので待たなかった</summary>
        NotPlaying,

        /// <summary>一時停止中でフレームが進まないので待たなかった</summary>
        Paused,

        /// <summary>指定のフレーム数だけ進んだ</summary>
        Advanced,

        /// <summary>時間内にフレームが進まなかった</summary>
        TimedOut,
    }

    /// <summary>PlayModeFrameSync が参照する Editor の状態（テストで差し替える）</summary>
    internal interface IPlayModeFrameClock
    {
        bool IsPlaying { get; }
        bool IsPaused { get; }
        int FrameCount { get; }
        bool RunInBackground { get; set; }
        double NowSeconds { get; }

        /// <summary>次の EditorApplication.update まで待つ</summary>
        Awaitable WaitForNextUpdateAsync();
    }

    /// <summary>
    /// Game ビューを撮る前に Play のフレームを進め、Game ビューの絵を新しくする。
    /// </summary>
    /// <remarks>
    /// Player Settings の Run In Background がオフだと、Editor が背面の間は Play のプレイヤーループが止まる。
    /// Canvas のジオメトリ生成と Game ビューへの描画はプレイヤーループの中で行われるため、
    /// 止まった後に生成した Canvas（Screen Space Overlay など）は Game ビューに描かれず、
    /// GrabPixels で撮っても写らない。待つ間だけ Application.runInBackground を true にして
    /// フレームを進め、撮った後に元の値へ戻す（Play を抜ければ Unity が設定値へ戻す）。
    /// </remarks>
    internal static class PlayModeFrameSync
    {
        /// <summary>待つフレーム数。生成したフレームで描かれたものが Game ビューに出るまでの余裕を含める</summary>
        internal const int DefaultFrames = 2;

        /// <summary>フレームが進むのを待つ上限</summary>
        internal const int DefaultTimeoutMs = 2000;

        internal static Awaitable<PlayModeFrameSyncStatus> WaitForRenderedFramesAsync()
            => WaitForRenderedFramesAsync(EditorPlayModeFrameClock.Instance, DefaultFrames, DefaultTimeoutMs);

        internal static async Awaitable<PlayModeFrameSyncStatus> WaitForRenderedFramesAsync(
            IPlayModeFrameClock clock, int frames, int timeoutMs)
        {
            if (!clock.IsPlaying)
                return PlayModeFrameSyncStatus.NotPlaying;
            if (clock.IsPaused)
                return PlayModeFrameSyncStatus.Paused;

            var targetFrame = clock.FrameCount + Math.Max(1, frames);
            var deadline = clock.NowSeconds + Math.Max(0, timeoutMs) / 1000.0;
            var previousRunInBackground = clock.RunInBackground;
            clock.RunInBackground = true;
            try
            {
                while (clock.FrameCount < targetFrame)
                {
                    if (clock.NowSeconds >= deadline)
                        return PlayModeFrameSyncStatus.TimedOut;

                    await clock.WaitForNextUpdateAsync();

                    if (!clock.IsPlaying)
                        return PlayModeFrameSyncStatus.NotPlaying;
                    if (clock.IsPaused)
                        return PlayModeFrameSyncStatus.Paused;
                }

                return PlayModeFrameSyncStatus.Advanced;
            }
            finally
            {
                clock.RunInBackground = previousRunInBackground;
            }
        }

        /// <summary>結果を撮影結果の warning にする。問題が無ければ null</summary>
        internal static string ToCaptureWarning(PlayModeFrameSyncStatus status)
        {
            switch (status)
            {
                case PlayModeFrameSyncStatus.Paused:
                    return "Play mode is paused; the Game view shows the last rendered frame and may not include objects created since then.";
                case PlayModeFrameSyncStatus.TimedOut:
                    return $"The Game view did not render a new frame within {DefaultTimeoutMs}ms; the image may be stale.";
                default:
                    return null;
            }
        }

        private sealed class EditorPlayModeFrameClock : IPlayModeFrameClock
        {
            internal static readonly EditorPlayModeFrameClock Instance = new EditorPlayModeFrameClock();

            public bool IsPlaying => EditorApplication.isPlaying;
            public bool IsPaused => EditorApplication.isPaused;
            public int FrameCount => Time.frameCount;

            public bool RunInBackground
            {
                get => Application.runInBackground;
                set => Application.runInBackground = value;
            }

            public double NowSeconds => EditorApplication.timeSinceStartup;

            public async Awaitable WaitForNextUpdateAsync()
            {
                var completionSource = new AwaitableCompletionSource();
                EditorApplication.CallbackFunction callback = null;
                callback = () =>
                {
                    EditorApplication.update -= callback;
                    completionSource.TrySetResult();
                };
                EditorApplication.update += callback;
                try
                {
                    await completionSource.Awaitable;
                }
                finally
                {
                    EditorApplication.update -= callback;
                }
            }
        }
    }
}
