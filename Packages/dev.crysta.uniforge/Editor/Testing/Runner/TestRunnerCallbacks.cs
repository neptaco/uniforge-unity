using System;
using UnityEngine;

#if UNITY_INCLUDE_TESTS
using UnityEditor.TestTools.TestRunner.Api;
#endif

namespace UniForge.TestRunner
{
#if UNITY_INCLUDE_TESTS
    /// <summary>
    /// Unity Test Runner callbacks implementation.
    /// Receives test events and stores results in TestResultCache.
    /// </summary>
    public class TestRunnerCallbacks : ICallbacks
    {
        private readonly string _runId;
        private readonly TestResultCache _cache;
        private DateTime _runStartTime;

        public string RunId => _runId;
        public bool IsCompleted { get; private set; }

        public event Action<string> OnRunCompleted;

        public TestRunnerCallbacks(string runId, long runStartTimeUnixMs = 0)
        {
            _runId = runId;
            _cache = TestResultCache.instance;
            if (runStartTimeUnixMs > 0)
            {
                _runStartTime = DateTimeOffset.FromUnixTimeMilliseconds(runStartTimeUnixMs).UtcDateTime;
            }
        }

        public void RunStarted(ITestAdaptor testsToRun)
        {
            _runStartTime = DateTime.UtcNow;
            Debug.Log($"[TestRunner] Run started: {_runId}, tests to run: {testsToRun.TestCaseCount}");
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            var duration = (DateTime.UtcNow - _runStartTime).TotalSeconds;
            _cache.CompleteRun(_runId, duration);
            var run = _cache.GetRun(_runId);
            if (run != null)
            {
                run.passCount = result.PassCount;
                run.failCount = result.FailCount;
                run.skipCount = result.SkipCount;
                run.totalCount = result.PassCount + result.FailCount + result.SkipCount;
                run.success = result.FailCount == 0;
            }
            IsCompleted = true;

            Debug.Log($"[TestRunner] Run finished: {_runId}, " +
                     $"passed: {result.PassCount}, failed: {result.FailCount}, " +
                     $"skipped: {result.SkipCount}, duration: {duration:F2}s");

            OnRunCompleted?.Invoke(_runId);
        }

        public void TestStarted(ITestAdaptor test)
        {
            // Individual test started - could log for verbose mode
        }

        public void TestFinished(ITestResultAdaptor result)
        {
            // Only record leaf test results (not suites)
            if (result.Test.IsSuite)
            {
                return;
            }

            var entry = new TestResultEntry
            {
                fullName = result.Test.FullName,
                displayName = result.Test.Name,
                status = result.TestStatus.ToString(),
                durationSeconds = result.Duration,
                message = result.Message,
                stackTrace = result.StackTrace,
                categories = result.Test.Categories ?? Array.Empty<string>()
            };

            // Extract assembly name from full name if available
            var fullName = result.Test.FullName;
            var dotIndex = fullName.IndexOf('.');
            if (dotIndex > 0)
            {
                entry.assembly = fullName.Substring(0, dotIndex);
            }

            _cache.AddResult(_runId, entry);
        }
    }
#else
    /// <summary>
    /// Stub implementation when Test Framework is not installed
    /// </summary>
    public class TestRunnerCallbacks
    {
        public string RunId => null;
        public bool IsCompleted => true;
        public event Action<string> OnRunCompleted;
    }
#endif
}
