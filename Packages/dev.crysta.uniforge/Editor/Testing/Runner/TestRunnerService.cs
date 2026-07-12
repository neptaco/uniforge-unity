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

            EnsureApiInitialized();
            var run = cache.GetRun(cache.CurrentRunId);
            RegisterCallbacks(cache.CurrentRunId, run != null ? run.startTime : 0);
            Debug.Log($"[TestRunner] Re-registered callbacks for run '{cache.CurrentRunId}' after domain reload");
        }

        private void RegisterCallbacks(string runId, long runStartTimeUnixMs)
        {
            if (_currentCallbacks != null && _api != null)
            {
                _api.UnregisterCallbacks(_currentCallbacks);
            }

            _currentCallbacks = new TestRunnerCallbacks(runId, runStartTimeUnixMs);
            _currentCallbacks.OnRunCompleted += HandleRunCompleted;
            _api.RegisterCallbacks(_currentCallbacks);
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

        private void CollectTests(ITestAdaptor test, List<TestInfo> results, string mode)
        {
            if (test == null) return;

            // Add non-assembly, non-root nodes
            if (!test.IsTestAssembly && test.Parent != null)
            {
                results.Add(new TestInfo
                {
                    fullName = test.FullName,
                    displayName = test.Name,
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
                    CollectTests(child, results, mode);
                }
            }
        }

        /// <summary>
        /// Start test execution
        /// </summary>
        /// <returns>Run ID for tracking</returns>
        public string StartTests(TestExecutionSettings settings)
        {
            if (IsRunning)
            {
                Debug.LogWarning("[TestRunner] A test run is already in progress");
                return null;
            }

            EnsureApiInitialized();

            var cache = TestResultCache.instance;
            var run = cache.CreateRun(settings.Mode);

            RegisterCallbacks(run.runId, run.startTime);

            // Build filter
            var testMode = ParseTestMode(settings.Mode);
            var filter = new Filter
            {
                testMode = testMode
            };

            if (settings.TestNames != null && settings.TestNames.Length > 0)
            {
                // Expand class names to full test names using cache
                var expandedNames = ExpandTestNames(settings.TestNames, testMode);
                if (expandedNames == null || expandedNames.Length == 0)
                {
                    Debug.LogError("[TestRunner] Failed to expand test names - cache not ready or no matching tests");
                    cache.CompleteRun(run.runId, 0);
                    return null;
                }
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

            try
            {
                _api.Execute(executionSettings);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TestRunner] Failed to start tests: {ex.Message}");
                cache.CompleteRun(run.runId, 0);
                return null;
            }

            return run.runId;
        }

        private void HandleRunCompleted(string runId)
        {
            if (_currentCallbacks != null)
            {
                _api.UnregisterCallbacks(_currentCallbacks);
                _currentCallbacks = null;
            }

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
        private string[] ExpandTestNames(string[] testNames, TestMode mode)
        {
            if (!IsCacheReady)
            {
                Debug.LogWarning("[TestRunner] Cache not ready, cannot expand test names");
                return null;
            }

            var expanded = new List<string>();
            var allTests = GetTestsCached(mode == TestMode.EditMode ? "EditMode" :
                                          mode == TestMode.PlayMode ? "PlayMode" : "Both");

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

                    // If no matches, add as-is (might be a valid full name not in cache)
                    if (!foundAny)
                    {
                        expanded.Add(name);
                    }
                }
            }

            return expanded.ToArray();
        }

        private void OnDisable()
        {
            // Cleanup TestRunnerApi to prevent memory leak
            if (_currentCallbacks != null && _api != null)
            {
                _api.UnregisterCallbacks(_currentCallbacks);
                _currentCallbacks = null;
            }

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
            Debug.LogWarning("[TestRunner] Test Framework package is not installed");
            return null;
        }

        public static bool IsTestFrameworkAvailable => false;
    }
#endif
}
