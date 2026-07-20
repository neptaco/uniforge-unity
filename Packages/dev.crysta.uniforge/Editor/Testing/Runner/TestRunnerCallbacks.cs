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
        private readonly Func<string, string, bool> _isCurrentExecution;
        private readonly DateTime _runRequestedAtUtc;
        private DateTime _runStartTime;
        private string _executionGuid;
        private bool _runStarted;

        public string RunId => _runId;
        public bool IsCompleted { get; private set; }

        public event Action<string> OnRunCompleted;

        public TestRunnerCallbacks(
            string runId,
            long runStartTimeUnixMs = 0,
            Func<string, string, bool> isCurrentExecution = null)
        {
            _runId = runId;
            _cache = TestResultCache.instance;
            _isCurrentExecution = isCurrentExecution;
            _runStarted = _cache.GetRun(runId)?.runStarted ?? false;
            if (runStartTimeUnixMs > 0)
            {
                _runRequestedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(runStartTimeUnixMs).UtcDateTime;
                _runStartTime = _runRequestedAtUtc;
            }
            else
            {
                _runRequestedAtUtc = DateTime.UtcNow;
                _runStartTime = _runRequestedAtUtc;
            }
        }

        internal void BindExecutionGuid(string executionGuid)
        {
            _executionGuid = executionGuid;
        }

        public void RunStarted(ITestAdaptor testsToRun)
        {
            if (!IsCurrentExecution() || _runStarted)
            {
                return;
            }

            _runStartTime = DateTime.UtcNow;
            _runStarted = true;
            _cache.MarkRunStarted(_runId);
            Debug.Log($"[TestRunner] Run started: {_runId}, tests to run: {testsToRun.TestCaseCount}");
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            if (!IsCurrentExecution() ||
                !_runStarted ||
                IsCompleted ||
                !IsResultFromCurrentRun(result))
            {
                return;
            }

            var duration = (DateTime.UtcNow - _runStartTime).TotalSeconds;
            _cache.CompleteRun(_runId, duration);

            // スイートレベルの失敗 (OneTimeSetUp 失敗など) は leaf 結果に現れないため、
            // RunFinished の集計で件数と成否を補正して永続化する
            _cache.ApplyRunSummary(
                _runId,
                result.PassCount,
                result.FailCount,
                result.SkipCount,
                result.FailCount == 0);

            IsCompleted = true;

            Debug.Log($"[TestRunner] Run finished: {_runId}, " +
                     $"passed: {result.PassCount}, failed: {result.FailCount}, " +
                     $"skipped: {result.SkipCount}, duration: {duration:F2}s");

            OnRunCompleted?.Invoke(_runId);
        }

        public void TestStarted(ITestAdaptor test)
        {
            if (!IsCurrentExecution())
            {
                return;
            }

            // Individual test started - could log for verbose mode
        }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (!IsCurrentExecution() || !_runStarted || !IsResultFromCurrentRun(result))
            {
                return;
            }

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

        private bool IsCurrentExecution()
        {
            return _isCurrentExecution == null || _isCurrentExecution(_runId, _executionGuid);
        }

        private bool IsResultFromCurrentRun(ITestResultAdaptor result)
        {
            // UTF broadcasts callbacks globally without an incoming execution guid. Reject results
            // whose NUnit epoch predates this UniForge run so a delayed prior run cannot complete it.
            if (result == null)
            {
                return false;
            }

            if (result.StartTime == default)
            {
                // Player results pass DateTime through JsonUtility, which may not preserve it.
                return true;
            }

            var resultStartTimeUtc = result.StartTime.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(result.StartTime, DateTimeKind.Utc)
                : result.StartTime.ToUniversalTime();
            return resultStartTimeUtc >= _runRequestedAtUtc;
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
