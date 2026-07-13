using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UniForge.Tests
{
    [TestFixture]
    public class ConsoleLogCaptureTests
    {
        private ConsoleLogCapture _capture;
        private List<LogEntry> _testLogs;

        [SetUp]
        public void SetUp()
        {
            _capture = ConsoleLogCapture.instance;
            _capture.Clear();

            // Create test log entries
            _testLogs = new List<LogEntry>
            {
                CreateLogEntry("Info message 1", LogType.Log, 1000),
                CreateLogEntry("Warning message", LogType.Warning, 2000),
                CreateLogEntry("Error message", LogType.Error, 3000),
                CreateLogEntry("Info message 2", LogType.Log, 4000),
                CreateLogEntry("Exception occurred", LogType.Exception, 5000)
            };
        }

        [TearDown]
        public void TearDown()
        {
            _capture.Clear();
        }

        private LogEntry CreateLogEntry(string message, LogType type, long timestamp)
        {
            return new LogEntry
            {
                message = message,
                stackTrace = $"StackTrace for: {message}",
                type = type.ToString(),
                timestamp = timestamp
            };
        }

        #region GetLogs Tests

        [Test]
        public void GetLogs_EmptyCapture_ReturnsEmptyList()
        {
            var logs = _capture.GetLogs();
            Assert.IsNotNull(logs);
            Assert.AreEqual(0, logs.Count);
        }

        [Test]
        public void GetLogs_WithLimit_RespectsLimit()
        {
            // Add logs via Debug.Log (captured by the system)
            Debug.Log("Test log 1");
            Debug.Log("Test log 2");
            Debug.Log("Test log 3");

            var logs = _capture.GetLogs("all", 2);
            Assert.LessOrEqual(logs.Count, 2);
        }

        #endregion

        #region LogFilterOptions Tests

        [Test]
        public void LogFilterOptions_DefaultValues()
        {
            var options = new LogFilterOptions();
            Assert.AreEqual("all", options.TypeFilter);
            Assert.IsNull(options.Since);
            Assert.IsNull(options.Until);
            Assert.IsNull(options.Pattern);
            Assert.IsTrue(options.IgnoreCase);
            Assert.IsFalse(options.SearchStackTrace);
            Assert.AreEqual(100, options.Limit);
        }

        [Test]
        public void LogFilterOptions_CanSetAllProperties()
        {
            var options = new LogFilterOptions
            {
                TypeFilter = "errors",
                Since = 1000,
                Until = 5000,
                Pattern = "test.*pattern",
                IgnoreCase = false,
                SearchStackTrace = true,
                Limit = 50
            };

            Assert.AreEqual("errors", options.TypeFilter);
            Assert.AreEqual(1000, options.Since);
            Assert.AreEqual(5000, options.Until);
            Assert.AreEqual("test.*pattern", options.Pattern);
            Assert.IsFalse(options.IgnoreCase);
            Assert.IsTrue(options.SearchStackTrace);
            Assert.AreEqual(50, options.Limit);
        }

        #endregion

        #region GetLogsFiltered Tests

        [Test]
        public void GetLogsFiltered_NullOptions_UsesDefaults()
        {
            Debug.Log("Test message");
            var logs = _capture.GetLogsFiltered(null);
            Assert.IsNotNull(logs);
        }

        [Test]
        public void GetLogsFiltered_TypeFilter_Errors_FiltersCorrectly()
        {
            // Expect the warning and error logs we're about to generate
            LogAssert.Expect(LogType.Warning, "Warning");
            LogAssert.Expect(LogType.Error, "Error");

            // Generate various log types
            Debug.Log("Info");
            Debug.LogWarning("Warning");
            Debug.LogError("Error");

            var options = new LogFilterOptions { TypeFilter = "errors" };
            var logs = _capture.GetLogsFiltered(options);

            foreach (var log in logs)
            {
                Assert.IsTrue(
                    log.type == "Error" || log.type == "Exception" || log.type == "Assert",
                    $"Expected error type but got {log.type}");
            }
        }

        [Test]
        public void GetLogsFiltered_TypeFilter_Warnings_FiltersCorrectly()
        {
            // Expect the warning and error logs we're about to generate
            LogAssert.Expect(LogType.Warning, "Warning");
            LogAssert.Expect(LogType.Error, "Error");

            Debug.Log("Info");
            Debug.LogWarning("Warning");
            Debug.LogError("Error");

            var options = new LogFilterOptions { TypeFilter = "warnings" };
            var logs = _capture.GetLogsFiltered(options);

            foreach (var log in logs)
            {
                Assert.AreEqual("Warning", log.type);
            }
        }

        [Test]
        public void GetLogsFiltered_TypeFilter_Info_FiltersCorrectly()
        {
            // Expect the warning and error logs we're about to generate
            LogAssert.Expect(LogType.Warning, "Warning");
            LogAssert.Expect(LogType.Error, "Error");

            Debug.Log("Info");
            Debug.LogWarning("Warning");
            Debug.LogError("Error");

            var options = new LogFilterOptions { TypeFilter = "info" };
            var logs = _capture.GetLogsFiltered(options);

            foreach (var log in logs)
            {
                Assert.AreEqual("Log", log.type);
            }
        }

        [Test]
        public void GetLogsFiltered_Pattern_MatchesMessage()
        {
            Debug.Log("Test message with keyword");
            Debug.Log("Another message");
            Debug.Log("Message with keyword again");

            var options = new LogFilterOptions
            {
                Pattern = "keyword",
                Limit = 100
            };
            var logs = _capture.GetLogsFiltered(options);

            foreach (var log in logs)
            {
                Assert.IsTrue(
                    log.message.ToLower().Contains("keyword"),
                    $"Expected message to contain 'keyword': {log.message}");
            }
        }

        [Test]
        public void GetLogsFiltered_Pattern_CaseInsensitive()
        {
            Debug.Log("Message with KEYWORD");
            Debug.Log("message with keyword");
            Debug.Log("MESSAGE WITH Keyword");

            var options = new LogFilterOptions
            {
                Pattern = "keyword",
                IgnoreCase = true,
                Limit = 100
            };
            var logs = _capture.GetLogsFiltered(options);

            Assert.GreaterOrEqual(logs.Count, 1);
        }

        [Test]
        public void GetLogsFiltered_InvalidRegex_TreatsAsLiteral()
        {
            Debug.Log("Message with [invalid regex");

            var options = new LogFilterOptions
            {
                Pattern = "[invalid regex",  // Invalid regex pattern
                Limit = 100
            };

            // Should not throw, should treat as literal
            Assert.DoesNotThrow(() => _capture.GetLogsFiltered(options));
        }

        [Test]
        public void GetLogsFiltered_Limit_RespectsLimit()
        {
            for (int i = 0; i < 10; i++)
            {
                Debug.Log($"Log message {i}");
            }

            var options = new LogFilterOptions { Limit = 5 };
            var logs = _capture.GetLogsFiltered(options);

            Assert.LessOrEqual(logs.Count, 5);
        }

        #endregion

        #region Clear Tests

        [Test]
        public void Clear_RemovesAllLogs()
        {
            Debug.Log("Test 1");
            Debug.Log("Test 2");

            _capture.Clear();
            var logs = _capture.GetLogs();

            Assert.AreEqual(0, logs.Count);
        }

        #endregion

        #region Count Tests

        [Test]
        public void Count_AfterClear_ReturnsZero()
        {
            Debug.Log("Test");
            _capture.Clear();

            Assert.AreEqual(0, _capture.Count);
        }

        [Test]
        public void Count_AfterAddingLogs_ReturnsCorrectCount()
        {
            _capture.Clear();
            int initialCount = _capture.Count;

            Debug.Log("Test 1");
            Debug.Log("Test 2");

            Assert.AreEqual(initialCount + 2, _capture.Count);
        }

        #endregion

        #region MaxLogs Tests

        [Test]
        public void MaxLogs_DefaultValue_IsReasonable()
        {
            Assert.GreaterOrEqual(_capture.MaxLogs, 100);
        }

        [Test]
        public void MaxLogs_CanBeSet()
        {
            int originalMaxLogs = _capture.MaxLogs;

            try
            {
                _capture.MaxLogs = 500;
                Assert.AreEqual(500, _capture.MaxLogs);
            }
            finally
            {
                _capture.MaxLogs = originalMaxLogs;
            }
        }

        [Test]
        public void MaxLogs_EnforcesMinimum()
        {
            int originalMaxLogs = _capture.MaxLogs;

            try
            {
                _capture.MaxLogs = 10;  // Below minimum
                Assert.GreaterOrEqual(_capture.MaxLogs, 100);  // Should be at least 100
            }
            finally
            {
                _capture.MaxLogs = originalMaxLogs;
            }
        }

        #endregion

        #region Threaded Capture Tests

        [Test]
        public void LogFromBackgroundThread_IsCaptured()
        {
            _capture.EnsureSubscribed();

            var marker = "BgThreadLog_" + Guid.NewGuid().ToString("N");
            var task = Task.Run(() => Debug.Log(marker));
            Assert.IsTrue(task.Wait(TimeSpan.FromSeconds(5)), "Background log task did not complete");

            // logMessageReceivedThreaded はログ発行スレッドで同期実行されるはずだが、余裕を持ってポーリングする
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            var logs = _capture.GetLogsFiltered(new LogFilterOptions { Pattern = marker, Limit = 10 });
            while (logs.Count == 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(10);
                logs = _capture.GetLogsFiltered(new LogFilterOptions { Pattern = marker, Limit = 10 });
            }

            Assert.AreEqual(1, logs.Count, "Background thread log was not captured");
            Assert.AreEqual(marker, logs[0].message);
        }

        #endregion

        #region BuildPatternRegex Cache Tests

        [Test]
        public void BuildPatternRegex_SamePatternAndCase_ReturnsCachedInstance()
        {
            var r1 = ConsoleLogCapture.BuildPatternRegex(
                new LogFilterOptions { Pattern = "cache_pattern_same", IgnoreCase = true });
            var r2 = ConsoleLogCapture.BuildPatternRegex(
                new LogFilterOptions { Pattern = "cache_pattern_same", IgnoreCase = true });

            Assert.AreSame(r1, r2, "Same pattern/ignoreCase must return the cached Regex instance");
        }

        [Test]
        public void BuildPatternRegex_DifferentIgnoreCase_ReturnsDifferentInstances()
        {
            var r1 = ConsoleLogCapture.BuildPatternRegex(
                new LogFilterOptions { Pattern = "cache_pattern_case", IgnoreCase = true });
            var r2 = ConsoleLogCapture.BuildPatternRegex(
                new LogFilterOptions { Pattern = "cache_pattern_case", IgnoreCase = false });

            Assert.AreNotSame(r1, r2);
            Assert.IsTrue(r1.Options.HasFlag(RegexOptions.IgnoreCase));
            Assert.IsFalse(r2.Options.HasFlag(RegexOptions.IgnoreCase));
        }

        [Test]
        public void BuildPatternRegex_InvalidPattern_ReturnsLiteralAndCaches()
        {
            Regex r1 = null;
            Assert.DoesNotThrow(() => r1 = ConsoleLogCapture.BuildPatternRegex(
                new LogFilterOptions { Pattern = "[invalid(", IgnoreCase = true }));
            Assert.IsNotNull(r1);
            Assert.IsTrue(r1.IsMatch("prefix [invalid( suffix"), "Invalid pattern must fall back to literal match");

            var r2 = ConsoleLogCapture.BuildPatternRegex(
                new LogFilterOptions { Pattern = "[invalid(", IgnoreCase = true });
            Assert.AreSame(r1, r2, "Fallback literal Regex must also be cached");
        }

        #endregion

        #region Singleton Tests

        [Test]
        public void Instance_ReturnsSameInstance()
        {
            var instance1 = ConsoleLogCapture.instance;
            var instance2 = ConsoleLogCapture.instance;

            Assert.AreSame(instance1, instance2);
        }

        [Test]
        public void Instance_IsNotNull()
        {
            Assert.IsNotNull(ConsoleLogCapture.instance);
        }

        #endregion
    }
}
