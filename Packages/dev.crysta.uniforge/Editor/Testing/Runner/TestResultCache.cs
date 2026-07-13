using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UniForge.TestRunner
{
    /// <summary>
    /// Domain reload と editor startup 時の run 状態を整合させる。
    /// </summary>
    internal static class TestResultCacheInitializer
    {
        private const string SessionInitializedKey = "UniForge.TestRunner.SessionInitialized";

        [InitializeOnLoadMethod]
        private static void OnDomainReload()
        {
            var firstLoadInSession = !SessionState.GetBool(SessionInitializedKey, false);
            SessionState.SetBool(SessionInitializedKey, true);

            EditorApplication.delayCall += () =>
            {
                HandleEditorLoad(TestResultCache.instance, firstLoadInSession);
            };
        }

        internal static void HandleEditorLoad(TestResultCache cache, bool firstLoadInSession)
        {
            if (cache == null || !cache.IsRunning)
            {
                return;
            }

            var runId = cache.CurrentRunId;
            if (firstLoadInSession)
            {
                cache.AbortRun(runId, TestResultCache.EditorRestartAbortReason, 0);
                Debug.LogWarning($"[TestRunner] Aborted orphaned test run '{runId}' after editor restart");
                return;
            }

            Debug.Log($"[TestRunner] Domain reload detected while test run '{runId}' is active");
        }
    }

    /// <summary>
    /// Single test result entry
    /// </summary>
    [Serializable]
    public class TestResultEntry
    {
        public string fullName;
        public string displayName;
        public string status;       // "Passed", "Failed", "Skipped", "Inconclusive"
        public double durationSeconds;
        public string message;
        public string stackTrace;
        public string assembly;
        public string[] categories;
    }

    /// <summary>
    /// A complete test run record
    /// </summary>
    [Serializable]
    public class TestRunRecord
    {
        public string runId;
        public string mode;         // "EditMode", "PlayMode", "Both"
        public long startTime;
        public long endTime;
        public bool completed;
        public bool success;
        public bool aborted;
        public string abortedReason;
        public int passCount;
        public int failCount;
        public int skipCount;
        public int totalCount;
        public double durationSeconds;
        public List<TestResultEntry> results = new List<TestResultEntry>();

        public void UpdateCounts()
        {
            passCount = 0;
            failCount = 0;
            skipCount = 0;

            foreach (var result in results)
            {
                switch (result.status)
                {
                    case "Passed":
                        passCount++;
                        break;
                    case "Failed":
                        failCount++;
                        break;
                    case "Skipped":
                    case "Inconclusive":
                        skipCount++;
                        break;
                }
            }

            totalCount = results.Count;
            success = failCount == 0;
        }
    }

    [Serializable]
    internal class TestRunIndexState
    {
        public string currentRunId;
        public List<string> runIds = new List<string>();
    }

    [Serializable]
    internal class TestResultCacheSnapshot
    {
        public List<TestRunRecord> runs = new List<TestRunRecord>();
        public string currentRunId;
    }

    internal static class TestRunPersistence
    {
        private const string IndexFileName = "index.json";

        internal static Func<string> BaseDirectoryOverrideForTests;

        internal static string BaseDirectoryPath
        {
            get
            {
                var overridden = BaseDirectoryOverrideForTests?.Invoke();
                if (!string.IsNullOrEmpty(overridden))
                {
                    return overridden;
                }

                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                return Path.Combine(projectRoot ?? ".", "Library", "UniForge", "TestRuns");
            }
        }

        private static string IndexFilePath => Path.Combine(BaseDirectoryPath, IndexFileName);

        internal static bool TryLoad(out List<TestRunRecord> runs, out string currentRunId)
        {
            runs = new List<TestRunRecord>();
            currentRunId = null;

            if (!File.Exists(IndexFilePath))
            {
                return false;
            }

            try
            {
                var indexJson = File.ReadAllText(IndexFilePath);
                var index = JsonUtility.FromJson<TestRunIndexState>(indexJson);
                if (index == null)
                {
                    return false;
                }

                currentRunId = string.IsNullOrEmpty(index.currentRunId) ? null : index.currentRunId;
                foreach (var runId in index.runIds)
                {
                    var run = LoadRun(runId);
                    if (run != null)
                    {
                        runs.Add(run);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestRunner] Failed to load persisted test runs: {ex.Message}");
                return false;
            }
        }

        internal static void Save(IReadOnlyList<TestRunRecord> runs, string currentRunId)
        {
            try
            {
                Directory.CreateDirectory(BaseDirectoryPath);

                var activeRunIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var run in runs)
                {
                    if (run == null || string.IsNullOrEmpty(run.runId))
                    {
                        continue;
                    }

                    activeRunIds.Add(run.runId);
                    WriteJsonAtomic(GetRunFilePath(run.runId), JsonUtility.ToJson(run, false));
                }

                DeleteObsoleteRunFiles(activeRunIds);

                var index = new TestRunIndexState
                {
                    currentRunId = currentRunId,
                    runIds = new List<string>()
                };

                foreach (var run in runs)
                {
                    if (run != null && !string.IsNullOrEmpty(run.runId))
                    {
                        index.runIds.Add(run.runId);
                    }
                }

                WriteJsonAtomic(IndexFilePath, JsonUtility.ToJson(index, false));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestRunner] Failed to persist test runs: {ex.Message}");
            }
        }

        /// <summary>
        /// 単一 run のファイルのみ書き込む（index や他の run は変更しない）。
        /// run が既に Save 済みの index に含まれていることが前提。
        /// </summary>
        internal static void SaveRun(TestRunRecord run)
        {
            if (run == null || string.IsNullOrEmpty(run.runId))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(BaseDirectoryPath);
                WriteJsonAtomic(GetRunFilePath(run.runId), JsonUtility.ToJson(run, false));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestRunner] Failed to persist test run '{run.runId}': {ex.Message}");
            }
        }

        internal static void ClearAll()
        {
            try
            {
                if (Directory.Exists(BaseDirectoryPath))
                {
                    Directory.Delete(BaseDirectoryPath, recursive: true);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestRunner] Failed to clear persisted test runs: {ex.Message}");
            }
        }

        private static TestRunRecord LoadRun(string runId)
        {
            var runFilePath = GetRunFilePath(runId);
            if (!File.Exists(runFilePath))
            {
                return null;
            }

            var runJson = File.ReadAllText(runFilePath);
            return JsonUtility.FromJson<TestRunRecord>(runJson);
        }

        private static void DeleteObsoleteRunFiles(HashSet<string> activeRunIds)
        {
            if (!Directory.Exists(BaseDirectoryPath))
            {
                return;
            }

            foreach (var filePath in Directory.GetFiles(BaseDirectoryPath, "*.json"))
            {
                if (string.Equals(Path.GetFileName(filePath), IndexFileName, StringComparison.Ordinal))
                {
                    continue;
                }

                var runId = Path.GetFileNameWithoutExtension(filePath);
                if (!activeRunIds.Contains(runId))
                {
                    File.Delete(filePath);
                }
            }
        }

        private static string GetRunFilePath(string runId)
        {
            return Path.Combine(BaseDirectoryPath, runId + ".json");
        }

        private static void WriteJsonAtomic(string path, string json)
        {
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(tempPath, path);
        }
    }

    /// <summary>
    /// Test result cache that survives domain reload using ScriptableSingleton.
    /// </summary>
    public class TestResultCache : ScriptableSingleton<TestResultCache>
    {
        internal const string DomainReloadAbortReason = "Interrupted by domain reload";
        internal const string EditorRestartAbortReason = "Interrupted by editor restart";

        private const int MaxRuns = 10;

        [NonSerialized]
        private bool _loaded;

        [NonSerialized]
        private string _loadedBaseDirectory;

        [SerializeField]
        private List<TestRunRecord> _runs = new List<TestRunRecord>();

        [SerializeField]
        private string _currentRunId;

        /// <summary>
        /// Get all stored runs
        /// </summary>
        public IReadOnlyList<TestRunRecord> Runs
        {
            get
            {
                EnsureLoaded();
                return _runs;
            }
        }

        /// <summary>
        /// Currently running test ID (null if not running)
        /// </summary>
        public string CurrentRunId
        {
            get
            {
                EnsureLoaded();
                return _currentRunId;
            }
        }

        /// <summary>
        /// Create a new run record
        /// </summary>
        public TestRunRecord CreateRun(string mode)
        {
            EnsureLoaded();

            var run = new TestRunRecord
            {
                runId = Guid.NewGuid().ToString("N").Substring(0, 8),
                mode = mode,
                startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                completed = false
            };

            _runs.Add(run);
            _currentRunId = run.runId;

            // Trim old runs
            while (_runs.Count > MaxRuns)
            {
                _runs.RemoveAt(0);
            }

            Persist();
            return run;
        }

        /// <summary>
        /// Get a run by ID
        /// </summary>
        public TestRunRecord GetRun(string runId)
        {
            EnsureLoaded();

            if (string.IsNullOrEmpty(runId))
            {
                return GetLastRun();
            }

            return _runs.Find(r => r.runId == runId);
        }

        /// <summary>
        /// Get the most recent run
        /// </summary>
        public TestRunRecord GetLastRun()
        {
            EnsureLoaded();
            return _runs.Count > 0 ? _runs[_runs.Count - 1] : null;
        }

        /// <summary>
        /// Add a test result to the current run
        /// </summary>
        public void AddResult(string runId, TestResultEntry result)
        {
            EnsureLoaded();
            var run = GetRun(runId);
            if (run == null || run.completed)
            {
                // 完了/中断済みの run に遅延して届いた結果は無視する
                return;
            }

            run.results.Add(result);

            // 結果ごとに全 run + index を書き直すと O(N^2) のメインスレッド I/O になるため、
            // 現在の run のファイルのみ書き込む（index には CreateRun 時点で登録済み）
            PersistRun(run);
        }

        /// <summary>
        /// Mark a run as completed
        /// </summary>
        public void CompleteRun(string runId, double durationSeconds)
        {
            EnsureLoaded();
            var run = GetRun(runId);
            if (run != null)
            {
                // timeout などで最終的に中断された run を遅延した RunFinished が
                // 上書きしないようにガードする
                // (domain reload による中断は実行再開後の完了で上書きされる想定なので除外)
                if (IsFinallyAborted(run))
                {
                    return;
                }

                run.completed = true;
                run.endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                run.durationSeconds = durationSeconds;
                run.aborted = false;
                run.abortedReason = null;
                run.UpdateCounts();

                if (_currentRunId == runId)
                {
                    _currentRunId = null;
                }

                Persist();
            }
        }

        /// <summary>
        /// Mark a run as aborted/interrupted.
        /// </summary>
        public void AbortRun(string runId, string reason, double durationSeconds = 0)
        {
            EnsureLoaded();
            var run = GetRun(runId);
            if (run != null)
            {
                run.completed = true;
                run.endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                run.durationSeconds = durationSeconds;
                run.aborted = true;
                run.abortedReason = reason;
                run.UpdateCounts();
                run.success = false;

                if (_currentRunId == runId)
                {
                    _currentRunId = null;
                }

                Persist();
            }
        }

        /// <summary>
        /// RunFinished のスイートレベル集計（OneTimeSetUp 失敗など leaf 結果に現れない失敗を含む）で
        /// run の件数と成否を補正し、永続化する。
        /// </summary>
        public void ApplyRunSummary(string runId, int passCount, int failCount, int skipCount, bool success)
        {
            EnsureLoaded();
            var run = GetRun(runId);
            if (run == null || run.aborted)
            {
                // 中断済みの run の集計は補正しない（timeout 中断後の遅延 RunFinished 対策）
                return;
            }

            run.passCount = passCount;
            run.failCount = failCount;
            run.skipCount = skipCount;
            run.totalCount = passCount + failCount + skipCount;
            run.success = success;

            PersistRun(run);
        }

        /// <summary>
        /// Clear all cached runs
        /// </summary>
        public void Clear()
        {
            EnsureLoaded();
            _runs.Clear();
            _currentRunId = null;
            TestRunPersistence.ClearAll();
        }

        internal TestResultCacheSnapshot CaptureState()
        {
            EnsureLoaded();

            var snapshot = new TestResultCacheSnapshot
            {
                currentRunId = _currentRunId
            };

            foreach (var run in _runs)
            {
                snapshot.runs.Add(CloneRun(run));
            }

            return snapshot;
        }

        internal void RestoreState(TestResultCacheSnapshot snapshot)
        {
            EnsureLoaded();

            _runs = new List<TestRunRecord>();
            if (snapshot?.runs != null)
            {
                foreach (var run in snapshot.runs)
                {
                    _runs.Add(CloneRun(run));
                }
            }

            _currentRunId = snapshot?.currentRunId;
            Persist();
        }

        /// <summary>
        /// Check if a test run is currently in progress
        /// </summary>
        public bool IsRunning
        {
            get
            {
                EnsureLoaded();
                return !string.IsNullOrEmpty(_currentRunId);
            }
        }

        private void EnsureLoaded()
        {
            var baseDirectory = TestRunPersistence.BaseDirectoryPath;
            if (_loaded && string.Equals(_loadedBaseDirectory, baseDirectory, StringComparison.Ordinal))
            {
                return;
            }

            if (TestRunPersistence.TryLoad(out var runs, out var currentRunId))
            {
                _runs = runs ?? new List<TestRunRecord>();
                _currentRunId = currentRunId;
            }
            else
            {
                _runs = new List<TestRunRecord>();
                _currentRunId = null;
            }

            _loaded = true;
            _loadedBaseDirectory = baseDirectory;
        }

        private void Persist()
        {
            _loaded = true;
            _loadedBaseDirectory = TestRunPersistence.BaseDirectoryPath;
            TestRunPersistence.Save(_runs, _currentRunId);
        }

        /// <summary>
        /// 対象の run のファイルのみ永続化する（index や他の run は書き換えない）
        /// </summary>
        private void PersistRun(TestRunRecord run)
        {
            _loaded = true;
            _loadedBaseDirectory = TestRunPersistence.BaseDirectoryPath;
            TestRunPersistence.SaveRun(run);
        }

        private static bool IsFinallyAborted(TestRunRecord run)
        {
            return run.aborted &&
                   !string.Equals(run.abortedReason, DomainReloadAbortReason, StringComparison.Ordinal);
        }

        private static TestRunRecord CloneRun(TestRunRecord run)
        {
            if (run == null)
            {
                return null;
            }

            var clone = new TestRunRecord
            {
                runId = run.runId,
                mode = run.mode,
                startTime = run.startTime,
                endTime = run.endTime,
                completed = run.completed,
                success = run.success,
                aborted = run.aborted,
                abortedReason = run.abortedReason,
                passCount = run.passCount,
                failCount = run.failCount,
                skipCount = run.skipCount,
                totalCount = run.totalCount,
                durationSeconds = run.durationSeconds,
                results = new List<TestResultEntry>()
            };

            foreach (var result in run.results)
            {
                clone.results.Add(CloneResult(result));
            }

            return clone;
        }

        private static TestResultEntry CloneResult(TestResultEntry result)
        {
            if (result == null)
            {
                return null;
            }

            return new TestResultEntry
            {
                fullName = result.fullName,
                displayName = result.displayName,
                status = result.status,
                durationSeconds = result.durationSeconds,
                message = result.message,
                stackTrace = result.stackTrace,
                assembly = result.assembly,
                categories = result.categories != null ? (string[])result.categories.Clone() : null
            };
        }
    }
}
