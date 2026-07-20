using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

#if UNITY_INCLUDE_TESTS
using UnityEditor.TestTools.TestRunner.Api;
#endif

namespace UniForge.TestRunner
{
    /// <summary>
    /// Test info for list-tests result
    /// </summary>
    [Serializable]
    public class TestInfo
    {
        public string fullName;
        public string displayName;
        public string assembly;
        public string[] categories;
        public string mode;         // "EditMode" or "PlayMode"
        public bool hasChildren;
        public int childCount;
    }

    /// <summary>
    /// Test execution settings
    /// </summary>
    public class TestExecutionSettings
    {
        public string Mode { get; set; } = "EditMode";      // "EditMode", "PlayMode", "Both"
        public string[] TestNames { get; set; }              // Specific test names to run
        public string[] Categories { get; set; }             // Category filter
        public string[] Assemblies { get; set; }             // Assembly filter
        public bool RunSynchronously { get; set; } = false;  // Only works for EditMode
    }

#if UNITY_INCLUDE_TESTS
    /// <summary>
    /// Service for interacting with Unity Test Runner API.
    /// </summary>
    public class TestRunnerService : ScriptableSingleton<TestRunnerService>
    {
        private TestRunnerApi _api;
        private TestRunnerCallbacks _currentCallbacks;

        // 実行中の TestRunnerApi run の guid（キャンセル用、domain reload を跨いで保持）
        [SerializeField]
        private string _currentExecutionGuid;

        [NonSerialized]
        private string _startingRunId;

        // Cached test lists
        private List<TestInfo> _cachedEditModeTests;
        private List<TestInfo> _cachedPlayModeTests;
        private bool _cacheInitialized;
        private bool _cacheRefreshing;

        [InitializeOnLoadMethod]
        private static void OnEditorLoad()
        {
            EditorApplication.delayCall += () => instance.RecoverCallbacksAfterDomainReload();
        }

        /// <summary>
        /// Is a test run currently in progress?
        /// </summary>
        public bool IsRunning
        {
            get
            {
                var cache = TestResultCache.instance;
                if (cache.IsRunning && _currentCallbacks == null)
                {
                    RecoverCallbacksAfterDomainReload();
                }
                return cache.IsRunning;
            }
        }

        /// <summary>
        /// Current run ID
        /// </summary>
        public string CurrentRunId => TestResultCache.instance.CurrentRunId;

        /// <summary>
        /// Event fired when a test run completes
        /// </summary>
        public event Action<string> OnRunCompleted;

        private void EnsureApiInitialized()
        {
            if (_api == null)
            {
                _api = ScriptableObject.CreateInstance<TestRunnerApi>();
            }
        }

        internal void RecoverCallbacksAfterDomainReload()
        {
            var cache = TestResultCache.instance;
            if (!cache.IsRunning || _currentCallbacks != null || string.IsNullOrEmpty(cache.CurrentRunId))
            {
                return;
            }

            if (string.IsNullOrEmpty(_currentExecutionGuid))
            {
                Debug.LogWarning(
                    $"[TestRunner] Cannot recover callbacks for run '{cache.CurrentRunId}' because its execution guid is unavailable");
                return;
            }

            EnsureApiInitialized();
            var run = cache.GetRun(cache.CurrentRunId);
            RegisterCallbacks(
                cache.CurrentRunId,
                run != null ? run.startTime : 0,
                _currentExecutionGuid);
            Debug.Log($"[TestRunner] Re-registered callbacks for run '{cache.CurrentRunId}' after domain reload");
        }

        private void RegisterCallbacks(
            string runId,
            long runStartTimeUnixMs,
            string executionGuid = null)
        {
            if (_currentCallbacks != null)
            {
                UnregisterCallbacks(_currentCallbacks.RunId);
            }

            _currentCallbacks = new TestRunnerCallbacks(
                runId,
                runStartTimeUnixMs,
                IsCurrentExecution);
            _currentCallbacks.BindExecutionGuid(executionGuid);
            _currentCallbacks.OnRunCompleted += HandleRunCompleted;
            _api.RegisterCallbacks(_currentCallbacks);
        }

        private bool IsCurrentExecution(string runId, string executionGuid)
        {
            var callbacks = _currentCallbacks;
            if (callbacks == null ||
                !string.Equals(callbacks.RunId, runId, StringComparison.Ordinal) ||
                !string.Equals(TestResultCache.instance.CurrentRunId, runId, StringComparison.Ordinal))
            {
                return false;
            }

            if (string.IsNullOrEmpty(executionGuid))
            {
                return string.Equals(_startingRunId, runId, StringComparison.Ordinal) &&
                       string.IsNullOrEmpty(_currentExecutionGuid);
            }

            return string.Equals(_currentExecutionGuid, executionGuid, StringComparison.Ordinal);
        }

        private bool UnregisterCallbacks(string runId)
        {
            var callbacks = _currentCallbacks;
            if (callbacks == null ||
                !string.Equals(callbacks.RunId, runId, StringComparison.Ordinal))
            {
                return false;
            }

            callbacks.OnRunCompleted -= HandleRunCompleted;
            if (_api != null)
            {
                try
                {
                    _api.UnregisterCallbacks(callbacks);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[TestRunner] Failed to unregister test callbacks: {ex.Message}");
                }
            }

            if (ReferenceEquals(_currentCallbacks, callbacks))
            {
                _currentCallbacks = null;
            }

            return true;
        }

        /// <summary>
        /// Refresh the test cache. Call this after compilation or when tests might have changed.
        /// </summary>
        public void RefreshCache()
        {
            if (_cacheRefreshing) return;

            EnsureApiInitialized();
            _cacheRefreshing = true;
            _cachedEditModeTests = new List<TestInfo>();
            _cachedPlayModeTests = new List<TestInfo>();

            int pending = 2;

            _api.RetrieveTestList(TestMode.EditMode, rootTest =>
            {
                CollectTests(rootTest, _cachedEditModeTests, "EditMode");
                pending--;
                if (pending == 0)
                {
                    _cacheInitialized = true;
                    _cacheRefreshing = false;
                    Debug.Log($"[TestRunner] Cache refreshed: {_cachedEditModeTests.Count} EditMode, {_cachedPlayModeTests.Count} PlayMode tests");
                }
            });

            _api.RetrieveTestList(TestMode.PlayMode, rootTest =>
            {
                CollectTests(rootTest, _cachedPlayModeTests, "PlayMode");
                pending--;
                if (pending == 0)
                {
                    _cacheInitialized = true;
                    _cacheRefreshing = false;
                    Debug.Log($"[TestRunner] Cache refreshed: {_cachedEditModeTests.Count} EditMode, {_cachedPlayModeTests.Count} PlayMode tests");
                }
            });
        }

        /// <summary>
        /// Get list of available tests (uses cache)
        /// </summary>
        public List<TestInfo> GetTestsCached(string mode)
        {
            if (!_cacheInitialized)
            {
                return null; // Cache not ready
            }

            var results = new List<TestInfo>();
            var testMode = ParseTestMode(mode);

            if ((testMode & TestMode.EditMode) != 0 && _cachedEditModeTests != null)
            {
                results.AddRange(_cachedEditModeTests);
            }
            if ((testMode & TestMode.PlayMode) != 0 && _cachedPlayModeTests != null)
            {
                results.AddRange(_cachedPlayModeTests);
            }

            return results;
        }

        /// <summary>
        /// Is cache initialized and ready?
        /// </summary>
        public bool IsCacheReady => _cacheInitialized && !_cacheRefreshing;

        /// <summary>
        /// Get list of available tests asynchronously.
        /// </summary>
        public void GetTests(string mode, Action<List<TestInfo>> callback)
        {
            EnsureApiInitialized();

            var testMode = ParseTestMode(mode);
            var results = new List<TestInfo>();
            var pendingModes = new List<TestMode>();

            if ((testMode & TestMode.EditMode) != 0)
            {
                pendingModes.Add(TestMode.EditMode);
            }
            if ((testMode & TestMode.PlayMode) != 0)
            {
                pendingModes.Add(TestMode.PlayMode);
            }

            var completedModes = 0;

            foreach (var singleMode in pendingModes)
            {
                _api.RetrieveTestList(singleMode, rootTest =>
                {
                    CollectTests(rootTest, results, singleMode == TestMode.EditMode ? "EditMode" : "PlayMode");
                    completedModes++;

                    if (completedModes >= pendingModes.Count)
                    {
                        callback?.Invoke(results);
                    }
                });
            }

            // Handle case where no modes are requested
            if (pendingModes.Count == 0)
            {
                callback?.Invoke(results);
            }
        }

        private void CollectTests(ITestAdaptor test, List<TestInfo> results, string mode, string parentAssembly = null)
        {
            if (test == null) return;

            var assemblyName = ResolveAssemblyName(test, parentAssembly);

            // Add non-assembly, non-root nodes
            if (!test.IsTestAssembly && test.Parent != null)
            {
                results.Add(new TestInfo
                {
                    fullName = test.FullName,
                    displayName = test.Name,
                    assembly = assemblyName,
                    mode = mode,
                    hasChildren = test.HasChildren,
                    childCount = test.TestCaseCount,
                    categories = test.Categories ?? Array.Empty<string>()
                });
            }

            // Recurse into children
            if (test.HasChildren)
            {
                foreach (var child in test.Children)
                {
                    CollectTests(child, results, mode, assemblyName);
                }
            }
        }

        /// <summary>
        /// テストノードのアセンブリ名を解決する。
        /// 型情報があればそのアセンブリ、アセンブリノード自身は名前から導出、
        /// それ以外（namespace suite など）は親から継承する。
        /// </summary>
        internal static string ResolveAssemblyName(ITestAdaptor test, string parentAssembly)
        {
            var typeAssembly = test.TypeInfo?.Assembly?.GetName()?.Name;
            if (!string.IsNullOrEmpty(typeAssembly))
            {
                return typeAssembly;
            }

            if (test.IsTestAssembly && !string.IsNullOrEmpty(test.Name))
            {
                var name = test.Name;
                return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    ? name.Substring(0, name.Length - 4)
                    : name;
            }

            return parentAssembly;
        }

        /// <summary>
        /// Start test execution
        /// </summary>
        /// <returns>Run ID for tracking</returns>
        public string StartTests(TestExecutionSettings settings)
        {
            return StartTests(settings, out _);
        }

        /// <summary>
        /// Start test execution
        /// </summary>
        /// <returns>Run ID for tracking (null on failure, with error message in <paramref name="error"/>)</returns>
        public string StartTests(TestExecutionSettings settings, out string error)
        {
            error = null;

            if (IsRunning)
            {
                error = "A test run is already in progress";
                Debug.LogWarning($"[TestRunner] {error}");
                return null;
            }

            EnsureApiInitialized();

            var testMode = ParseTestMode(settings.Mode);

            // test_names 指定時は run 作成前に展開・検証する
            string[] expandedNames = null;
            if (settings.TestNames != null && settings.TestNames.Length > 0)
            {
                expandedNames = ExpandTestNames(settings.TestNames, testMode, out var unmatchedNames, out var cacheHasTests);
                if (expandedNames == null || expandedNames.Length == 0)
                {
                    error = "Failed to expand test names - test cache not ready or no matching tests";
                    Debug.LogError($"[TestRunner] {error}");
                    return null;
                }

                // キャッシュにテストがあるのに一致しない名前が残っている場合は実行前に失敗させる
                // (存在しない名前をそのまま渡すと 0 件実行が success として報告されるため)
                if (unmatchedNames.Count > 0 && cacheHasTests)
                {
                    error = $"No tests matched the specified test_names: {string.Join(", ", unmatchedNames)}";
                    Debug.LogError($"[TestRunner] {error}");
                    return null;
                }
            }

            var cache = TestResultCache.instance;
            var run = cache.CreateRun(settings.Mode);

            _currentExecutionGuid = null;
            RegisterCallbacks(run.runId, run.startTime);
            var callbacks = _currentCallbacks;

            // Build filter
            var filter = new Filter
            {
                testMode = testMode
            };

            if (expandedNames != null)
            {
                filter.testNames = expandedNames;
            }

            if (settings.Categories != null && settings.Categories.Length > 0)
            {
                filter.categoryNames = settings.Categories;
            }

            if (settings.Assemblies != null && settings.Assemblies.Length > 0)
            {
                filter.assemblyNames = settings.Assemblies;
            }

            // Create execution settings
            var executionSettings = new ExecutionSettings(filter)
            {
                // Only EditMode tests can run synchronously
                runSynchronously = settings.RunSynchronously && testMode == TestMode.EditMode
            };

            Debug.Log($"[TestRunner] Starting test run: {run.runId}, mode: {settings.Mode}, testNames: {(settings.TestNames != null ? string.Join(",", settings.TestNames) : "null")}");

            _startingRunId = run.runId;
            try
            {
                var executionGuid = _api.Execute(executionSettings);
                if (ReferenceEquals(_currentCallbacks, callbacks) &&
                    string.Equals(cache.CurrentRunId, run.runId, StringComparison.Ordinal))
                {
                    _currentExecutionGuid = executionGuid;
                    callbacks.BindExecutionGuid(executionGuid);
                }
            }
            catch (Exception ex)
            {
                error = $"Failed to start tests: {ex.Message}";
                Debug.LogError($"[TestRunner] {error}");
                try
                {
                    cache.CompleteRun(run.runId, 0);
                }
                finally
                {
                    _currentExecutionGuid = null;
                    UnregisterCallbacks(run.runId);
                }
                return null;
            }
            finally
            {
                _startingRunId = null;
            }

            EditorApplication.QueuePlayerLoopUpdate();

            return run.runId;
        }

        /// <summary>
        /// 実行中の run を中断する（timeout 時など）。
        /// TestRunnerApi 側の run が残っていればキャンセルし、cache の run 状態を破棄して
        /// 以後の run-tests が「already in progress」で拒否され続けないようにする。
        /// </summary>
        public void CancelRun(string runId, string reason, double durationSeconds = 0)
        {
            if (string.IsNullOrEmpty(runId))
            {
                return;
            }

            var cache = TestResultCache.instance;

            // 実行中の guid が対象の run のものであるときのみ TestRunnerApi のキャンセルを試みる
            var isCurrentRun = string.Equals(cache.CurrentRunId, runId, StringComparison.Ordinal) &&
                               _currentCallbacks != null &&
                               string.Equals(_currentCallbacks.RunId, runId, StringComparison.Ordinal);
            if (isCurrentRun)
            {
                try
                {
                    if (!string.IsNullOrEmpty(_currentExecutionGuid))
                    {
                        TestRunnerApi.CancelTestRun(_currentExecutionGuid);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[TestRunner] Failed to cancel TestRunnerApi run: {ex.Message}");
                }
                finally
                {
                    _currentExecutionGuid = null;
                    UnregisterCallbacks(runId);
                }
            }

            cache.AbortRun(runId, reason, durationSeconds);
        }

        private void HandleRunCompleted(string runId)
        {
            if (_currentCallbacks == null ||
                !string.Equals(_currentCallbacks.RunId, runId, StringComparison.Ordinal))
            {
                return;
            }

            _currentExecutionGuid = null;
            UnregisterCallbacks(runId);

            OnRunCompleted?.Invoke(runId);
        }

        /// <summary>
        /// Check if test framework is available
        /// </summary>
        public static bool IsTestFrameworkAvailable => true;

        private static TestMode ParseTestMode(string mode)
        {
            return mode?.ToLowerInvariant() switch
            {
                "editmode" => TestMode.EditMode,
                "playmode" => TestMode.PlayMode,
                "both" => TestMode.EditMode | TestMode.PlayMode,
                _ => TestMode.EditMode
            };
        }

        /// <summary>
        /// Expand test names - if a name is a class prefix, expand to all matching test full names
        /// </summary>
        private string[] ExpandTestNames(string[] testNames, TestMode mode, out List<string> unmatchedNames, out bool cacheHasTests)
        {
            unmatchedNames = new List<string>();
            cacheHasTests = false;

            if (!IsCacheReady)
            {
                Debug.LogWarning("[TestRunner] Cache not ready, cannot expand test names");
                return null;
            }

            var allTests = GetTestsCached(mode == TestMode.EditMode ? "EditMode" :
                                          mode == TestMode.PlayMode ? "PlayMode" : "Both");

            cacheHasTests = allTests != null && allTests.Count > 0;
            return ExpandTestNames(testNames, allTests, out unmatchedNames);
        }

        /// <summary>
        /// テスト名を展開する。クラス名 prefix はキャッシュ上の一致する full name 群に展開し、
        /// 一致しない名前はそのまま通しつつ <paramref name="unmatchedNames"/> に記録する
        /// （キャッシュにデータがある場合の失敗判定は呼び出し側で行う）。
        /// </summary>
        internal static string[] ExpandTestNames(string[] testNames, List<TestInfo> allTests, out List<string> unmatchedNames)
        {
            unmatchedNames = new List<string>();
            var expanded = new List<string>();
            allTests ??= new List<TestInfo>();

            foreach (var name in testNames)
            {
                // Check if it's already a full test name (actual test method, not a class/fixture)
                bool isFullName = false;
                foreach (var test in allTests)
                {
                    // Only match actual test methods (hasChildren=false), not test classes
                    if (test.fullName == name && !test.hasChildren)
                    {
                        expanded.Add(name);
                        isFullName = true;
                        break;
                    }
                }

                if (!isFullName)
                {
                    // Treat as class prefix - find all tests that start with this name
                    bool foundAny = false;
                    foreach (var test in allTests)
                    {
                        if (test.fullName.StartsWith(name + "."))
                        {
                            expanded.Add(test.fullName);
                            foundAny = true;
                        }
                    }

                    // If no matches, add as-is and record as unmatched
                    if (!foundAny)
                    {
                        expanded.Add(name);
                        unmatchedNames.Add(name);
                    }
                }
            }

            return expanded.ToArray();
        }

        private void OnDisable()
        {
            // Cleanup TestRunnerApi to prevent memory leak
            if (_currentCallbacks != null)
            {
                UnregisterCallbacks(_currentCallbacks.RunId);
            }

            _startingRunId = null;

            if (_api != null)
            {
                DestroyImmediate(_api);
                _api = null;
            }

            _cachedEditModeTests = null;
            _cachedPlayModeTests = null;
            _cacheInitialized = false;
        }
    }
#else
    /// <summary>
    /// Stub implementation when Test Framework is not installed
    /// </summary>
    public class TestRunnerService : ScriptableSingleton<TestRunnerService>
    {
        public bool IsRunning => false;
        public string CurrentRunId => null;
        public event Action<string> OnRunCompleted;

        public void GetTests(string mode, Action<List<TestInfo>> callback)
        {
            callback?.Invoke(new List<TestInfo>());
        }

        public string StartTests(TestExecutionSettings settings)
        {
            return StartTests(settings, out _);
        }

        public string StartTests(TestExecutionSettings settings, out string error)
        {
            error = "Test Framework package is not installed";
            Debug.LogWarning($"[TestRunner] {error}");
            return null;
        }

        public void CancelRun(string runId, string reason, double durationSeconds = 0)
        {
        }

        public static bool IsTestFrameworkAvailable => false;
    }
#endif
}
