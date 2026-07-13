using System;
using System.Collections.Generic;
using UnityEditor;

namespace UniForge.Tools.Mutations.InputSimulation
{
    /// <summary>
    /// 遅延実行する入力解放イベント（キー/マウスボタンの up）を管理するレジストリ。
    /// EditorApplication.delayCall による遅延解放はドメインリロードやエディタ終了時に
    /// 破棄され、OS レベルでキー/ボタンが押しっぱなしになる（stuck input）ため、
    /// beforeAssemblyReload / quitting のタイミングで全解放を即時フラッシュする。
    /// durationMs を honoring するため EditorApplication.update でのポーリングで発火する。
    /// スレッドセーフではない（すべてエディタメインスレッドで呼ばれる前提）。
    /// </summary>
    public static class PendingInputReleaseRegistry
    {
        private sealed class PendingRelease
        {
            public Action Release;
            public double DueTime; // TimeProvider (EditorApplication.timeSinceStartup) 基準
            public long Sequence;  // 同一 DueTime の場合の登録順維持用
        }

        private static readonly List<PendingRelease> Pending = new List<PendingRelease>();
        private static long _sequenceCounter;
        private static bool _updateHooked;
        private static bool _lifecycleHooked;

        /// <summary>現在時刻の取得元（テスト時に差し替え可能）</summary>
        internal static Func<double> TimeProvider = () => EditorApplication.timeSinceStartup;

        /// <summary>未実行の解放イベント数</summary>
        public static int PendingCount => Pending.Count;

        /// <summary>
        /// 解放イベントを登録する。delaySeconds 経過後の EditorApplication.update で実行される。
        /// 0 以下の場合は次の update tick で実行される。
        /// </summary>
        /// <param name="release">解放イベント（up イベントの送信処理）</param>
        /// <param name="delaySeconds">発火までの遅延秒数（durationMs / 1000.0）</param>
        public static void Register(Action release, double delaySeconds)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));

            EnsureLifecycleHooks();

            Pending.Add(new PendingRelease
            {
                Release = release,
                DueTime = TimeProvider() + Math.Max(0.0, delaySeconds),
                Sequence = _sequenceCounter++
            });

            if (!_updateHooked)
            {
                EditorApplication.update += OnEditorUpdate;
                _updateHooked = true;
            }
        }

        /// <summary>
        /// 全ての未実行解放イベントを発火期限に関わらず即時実行する。
        /// ドメインリロード前・エディタ終了時に呼ばれ、stuck input を防ぐ。
        /// </summary>
        public static void FlushAll()
        {
            FireReleases(_ => true);
        }

        /// <summary>
        /// 発火期限が到来した解放イベントを実行する（EditorApplication.update から呼ばれる）。
        /// </summary>
        internal static void ProcessDueReleases()
        {
            var now = TimeProvider();
            FireReleases(p => p.DueTime <= now);
        }

        private static void EnsureLifecycleHooks()
        {
            if (_lifecycleHooked) return;
            _lifecycleHooked = true;

            // ドメインリロードでデリゲートリストが消える前に up イベントを送信する
            AssemblyReloadEvents.beforeAssemblyReload += FlushAll;
            EditorApplication.quitting += FlushAll;
        }

        private static void OnEditorUpdate()
        {
            ProcessDueReleases();
        }

        private static void FireReleases(Predicate<PendingRelease> shouldFire)
        {
            if (Pending.Count == 0)
            {
                UnhookUpdateIfIdle();
                return;
            }

            // 実行中の Register による再入に備え、対象を先にリストから取り除いてから実行する
            var toFire = new List<PendingRelease>();
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                if (shouldFire(Pending[i]))
                {
                    toFire.Add(Pending[i]);
                    Pending.RemoveAt(i);
                }
            }

            // 発火期限順（同一期限は登録順）に実行
            toFire.Sort((a, b) =>
            {
                int byDue = a.DueTime.CompareTo(b.DueTime);
                return byDue != 0 ? byDue : a.Sequence.CompareTo(b.Sequence);
            });

            foreach (var pending in toFire)
            {
                try
                {
                    pending.Release();
                }
                catch (Exception ex)
                {
                    // 1 件の失敗が他の解放（stuck input の防止）を妨げないようにする
                    UnityEngine.Debug.LogWarning($"[PendingInputReleaseRegistry] Release action failed: {ex.Message}");
                }
            }

            UnhookUpdateIfIdle();
        }

        private static void UnhookUpdateIfIdle()
        {
            if (_updateHooked && Pending.Count == 0)
            {
                EditorApplication.update -= OnEditorUpdate;
                _updateHooked = false;
            }
        }

        /// <summary>テスト用: 内部状態をリセットする（解放イベントは実行しない）</summary>
        internal static void ResetForTest()
        {
            Pending.Clear();
            UnhookUpdateIfIdle();
            TimeProvider = () => EditorApplication.timeSinceStartup;
        }
    }
}
