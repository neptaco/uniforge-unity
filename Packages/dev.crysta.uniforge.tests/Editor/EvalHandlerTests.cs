using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UniForge.Tools;
using UniForge.Tools.Mutations;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace UniForge.Tests
{
    [TestFixture]
    public class EvalHandlerTests
    {
        private const string ToolName = "eval";
        private const string EvalTempPath = "Temp/UniForgeEval";

        private bool _wasEnabled;

        [SetUp]
        public void SetUp()
        {
            _wasEnabled = ToolSettings.instance.IsToolEnabled(ToolName);
            ToolSettings.instance.SetToolEnabled(ToolName, true);
        }

        [TearDown]
        public void TearDown()
        {
            ToolSettings.instance.SetToolEnabled(ToolName, _wasEnabled);
        }

        [UnityTest]
        public IEnumerator ExecutesReturnStatement()
        {
            var task = EvaluateAsync("return 2 + 2;");
            yield return WaitFor(task);
            var output = ReadResult(task);
            Assert.That(output["value"], Is.EqualTo(4));
            Assert.That(output["value_type"], Is.EqualTo(typeof(int).FullName));
        }

        [UnityTest]
        public IEnumerator ExecutesExpression()
        {
            var task = EvaluateAsync("1 + 1");
            yield return WaitFor(task);
            Assert.That(ReadResult(task)["value"], Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator ExecutesArrayInitializerAsExpression()
        {
            var task = EvaluateAsync("new[] { 1, 2 } // array");
            yield return WaitFor(task);
            Assert.That(ReadResult(task)["value"], Is.EqualTo(new[] { 1, 2 }));
        }

        [UnityTest]
        public IEnumerator ReturnsValueFromCollectionInitializer()
        {
            var task = EvaluateAsync("new List<int> { 1, 2 }");
            yield return WaitFor(task);
            Assert.That(ReadResult(task)["value"], Is.EqualTo(new[] { 1, 2 }));
        }

        [UnityTest]
        public IEnumerator RetriesVoidExpressionAsStatement()
        {
            LogAssert.Expect(LogType.Log, "x");
            var task = EvaluateAsync("Debug.Log(\"x\") // void expression");
            yield return WaitFor(task, allowCompilerErrors: true);
            var output = ReadResult(task);
            Assert.That(output["value"], Is.Null);
            Assert.That(output["value_type"], Is.EqualTo("null"));
        }

        [UnityTest]
        public IEnumerator ExecutesWithLeadingUsingDirectives()
        {
            var task = EvaluateAsync("// imports\nusing static System.Math;\n\nusing Text = System.Text.StringBuilder;\nnew Text().Append(Abs(-12)).ToString()");
            yield return WaitFor(task);
            Assert.That(ReadResult(task)["value"], Is.EqualTo("12"));
        }

        [UnityTest]
        public IEnumerator AwaitsTaskOnEditorMainThread()
        {
            var thread = Environment.CurrentManagedThreadId;
            var task = EvaluateAsync(
                $"if (Environment.CurrentManagedThreadId != {thread}) throw new Exception(\"Unexpected thread\");\n" +
                $"await Task.Yield();\nif (Environment.CurrentManagedThreadId != {thread}) throw new Exception(\"Unexpected continuation thread\");\nreturn 1;");
            yield return WaitFor(task);
            Assert.That(ReadResult(task)["value"], Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ReportsUserLineAndColumnOnCompilationError()
        {
            var task = EvaluateAsync("using System.Text;\n    return MissingEvalSymbol;");
            yield return WaitFor(task, allowCompilerErrors: true);
            var output = ReadResult(task, "COMPILATION_ERROR");
            var diagnostics = (List<object>)output["diagnostics"];
            var error = diagnostics.Cast<Dictionary<string, object>>()
                .Single(diagnostic => (string)diagnostic["severity"] == "error");
            Assert.That(error["line"], Is.EqualTo(2));
            Assert.That(error["column"], Is.EqualTo(12));
            Assert.That(error["message"], Does.Contain("MissingEvalSymbol"));
        }

        [UnityTest]
        public IEnumerator PreservesAggregateRuntimeException()
        {
            var requestId = Guid.NewGuid().ToString("N");
            var key = EvalHandler.RequestRecordPath(requestId);
            try
            {
                var task = EvaluateAsync(
                    "throw new AggregateException(\"eval failed\", new InvalidOperationException(\"first\"), " +
                    "new ArgumentException(\"second\"));", requestId: requestId);
                yield return WaitFor(task);
                var output = ReadResult(task, "RUNTIME_EXCEPTION");
                var exception = (Dictionary<string, object>)output["exception"];
                Assert.That(exception["type"], Is.EqualTo(typeof(AggregateException).FullName));
                Assert.That(exception["message"], Does.StartWith("eval failed").And.Contain("first").And.Contain("second"));
                Assert.That((string)exception["stack_trace"], Does.Contain("UniForgeEvalScript"));
                Assert.That(ReadRecord(key), Is.EqualTo("running"));
            }
            finally
            {
                File.Delete(key);
            }
        }

        [UnityTest]
        public IEnumerator DryRunDoesNotExecuteCode()
        {
            var key = NewSessionKey();
            var previous = SessionState.GetString(key, "");
            var requestId = Guid.NewGuid().ToString("N");
            var requestKey = EvalHandler.RequestRecordPath(requestId);
            try
            {
                var task = EvaluateAsync($"SessionState.SetString(\"{key}\", \"ran\");", dryRun: true, requestId: requestId);
                yield return WaitFor(task);
                Assert.That(ReadResult(task)["value"], Is.Null);
                Assert.That(SessionState.GetString(key, ""), Is.EqualTo(previous));
                Assert.That(ReadRecord(requestKey), Is.Empty);
            }
            finally
            {
                RestoreSessionString(key, previous);
                File.Delete(requestKey);
            }
        }

        [UnityTest]
        public IEnumerator ReturnsTimeoutWith200MillisecondBudget()
        {
            var existingDirectories = new HashSet<string>(GetEvalDirectories());
            var task = EvaluateAsync("await Task.Delay(10000, cancellationToken);", timeoutMs: 200);
            yield return WaitFor(task);
            var output = ReadResult(task, "TIMEOUT");
            var cleanup = Stopwatch.StartNew();
            while (GetEvalDirectories().Any(path => !existingDirectories.Contains(path)))
            {
                Assert.That(cleanup.ElapsedMilliseconds, Is.LessThan(30000), "Timed-out compilation did not clean up its files.");
                yield return null;
            }

            Assert.That(output["message"], Does.Contain("200 ms"));
        }

        [UnityTest]
        public IEnumerator CancelsCodeThatIsRunningAtTheDeadline()
        {
            var key = NewSessionKey();
            var previous = SessionState.GetString(key, "");
            var finishedKey = NewSessionKey();
            var previousFinished = SessionState.GetString(finishedKey, "");
            var requestId = Guid.NewGuid().ToString("N");
            var requestKey = EvalHandler.RequestRecordPath(requestId);
            try
            {
                var task = EvaluateAsync(
                    $"cancellationToken.Register(() => SessionState.SetString(\"{key}\", \"cancelled\"));\n" +
                    "try { await Task.Delay(10000, cancellationToken); }\n" +
                    $"finally {{ SessionState.SetString(\"{finishedKey}\", \"finished\"); }};",
                    timeoutMs: 5000, requestId: requestId);
                yield return WaitFor(task);
                var output = ReadResult(task, "TIMEOUT");
                Assert.That(output["message"], Does.Contain("The code may still be running."));
                Assert.That(SessionState.GetString(key, ""), Is.EqualTo("cancelled"));

                var cleanup = Stopwatch.StartNew();
                while (SessionState.GetString(finishedKey, "") != "finished")
                {
                    Assert.That(cleanup.ElapsedMilliseconds, Is.LessThan(5000), "Cancelled code did not finish.");
                    yield return null;
                }
                yield return null;
                var replay = EvaluateAsync("return 1;", requestId: requestId);
                yield return WaitFor(replay);
                ReadResult(replay, "TIMEOUT");
                Assert.That(replay.Result.ResultText, Is.EqualTo(task.Result.ResultText));
                Assert.That(ReadRecord(requestKey), Is.EqualTo("running"));
            }
            finally
            {
                RestoreSessionString(key, previous);
                RestoreSessionString(finishedKey, previousFinished);
                File.Delete(requestKey);
            }
        }

        [UnityTest]
        public IEnumerator RunningRequestIsInterruptedOnReplay()
        {
            var requestId = Guid.NewGuid().ToString("N");
            var key = EvalHandler.RequestRecordPath(requestId);
            try
            {
                WriteRecord(key, "running");
                for (var i = 0; i < 2; i++)
                {
                    var task = EvaluateAsync("throw new Exception();", requestId: requestId);
                    yield return WaitFor(task);
                    var output = ReadResult(task, "INTERRUPTED");
                    Assert.That(output["message"], Is.EqualTo(
                        "This request already started executing and its result was lost (domain reload or reconnection). Side effects may already have occurred."));
                }

                Assert.That(ReadRecord(key), Is.EqualTo("running"));
            }
            finally
            {
                File.Delete(key);
            }
        }

        [UnityTest]
        public IEnumerator CompilingRequestCanBeRetried()
        {
            var requestId = Guid.NewGuid().ToString("N");
            var key = EvalHandler.RequestRecordPath(requestId);
            try
            {
                WriteRecord(key, "compiling");
                var task = EvaluateAsync("return 7;", requestId: requestId);
                yield return WaitFor(task);
                Assert.That(ReadResult(task)["value"], Is.EqualTo(7));
                Assert.That(ReadRecord(key), Is.EqualTo("running"));
            }
            finally
            {
                File.Delete(key);
            }
        }

        [UnityTest]
        public IEnumerator CompletedRequestReplaysResultWithoutExecutingAgain()
        {
            var requestId = Guid.NewGuid().ToString("N");
            var key = EvalHandler.RequestRecordPath(requestId);
            var counter = NewSessionKey();
            var code = $"var count = SessionState.GetInt(\"{counter}\", 0) + 1; " +
                $"SessionState.SetInt(\"{counter}\", count); return count;";
            try
            {
                var first = EvaluateAsync(code, requestId: requestId);
                yield return WaitFor(first);
                Assert.That(ReadResult(first)["value"], Is.EqualTo(1));

                var replay = EvaluateAsync(code, requestId: requestId);
                yield return WaitFor(replay);
                Assert.That(ReadResult(replay)["value"], Is.EqualTo(1));
                Assert.That(replay.Result.ResultText, Is.EqualTo(first.Result.ResultText));
                Assert.That(SessionState.GetInt(counter, 0), Is.EqualTo(1));
            }
            finally
            {
                SessionState.EraseInt(counter);
                File.Delete(key);
            }
        }

        [UnityTest]
        public IEnumerator CompilationErrorIsCachedWithoutLeavingARequestRecord()
        {
            var requestId = Guid.NewGuid().ToString("N");
            var key = EvalHandler.RequestRecordPath(requestId);
            try
            {
                var first = EvaluateAsync("return MissingEvalSymbol;", requestId: requestId);
                yield return WaitFor(first, allowCompilerErrors: true);
                ReadResult(first, "COMPILATION_ERROR");
                Assert.That(ReadRecord(key), Is.Empty);

                var retry = EvaluateAsync("return 2;", requestId: requestId);
                yield return WaitFor(retry);
                ReadResult(retry, "COMPILATION_ERROR");
                Assert.That(retry.Result.ResultText, Is.EqualTo(first.Result.ResultText));
            }
            finally
            {
                File.Delete(key);
            }
        }

        [UnityTest]
        public IEnumerator RejectsInvalidRequestIds()
        {
            foreach (var requestId in new[] { "", "../outside", "a/b", "a\\b", " a", "a\n", "é", new string('a', 129) })
            {
                var task = EvaluateAsync("return 1;", requestId: requestId);
                yield return WaitFor(task);
                ReadResult(task, "INVALID_ARGUMENT");
            }
        }

        [Test]
        public void CleansRecordsOlderThanOneHour()
        {
            var root = Path.Combine(Path.GetTempPath(), "UniForgeEvalRecords-" + Guid.NewGuid().ToString("N"));
            var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var old = Path.Combine(root, "old.state");
            var recent = Path.Combine(root, "recent.state");
            var boundary = Path.Combine(root, "boundary.state");
            try
            {
                WriteRecord(old, "running");
                WriteRecord(recent, "compiling");
                WriteRecord(boundary, "running");
                File.SetLastWriteTimeUtc(old, now.AddMinutes(-61));
                File.SetLastWriteTimeUtc(recent, now.AddMinutes(-59));
                File.SetLastWriteTimeUtc(boundary, now.AddHours(-1));

                EvalHandler.CleanExpiredRequestRecords(root, now);

                Assert.That(File.Exists(old), Is.False);
                Assert.That(ReadRecord(recent), Is.EqualTo("compiling"));
                Assert.That(ReadRecord(boundary), Is.EqualTo("running"));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [UnityTest]
        public IEnumerator ReturnsCompletedSynchronousResultEvenAfterDeadline()
        {
            var task = EvaluateAsync("System.Threading.Thread.Sleep(5250); return 5;", timeoutMs: 5000);
            yield return WaitFor(task);
            var output = ReadResult(task);
            Assert.That(output["value"], Is.EqualTo(5));
            Assert.That(output["run_ms"], Is.GreaterThanOrEqualTo(5000));
        }

        [UnityTest]
        public IEnumerator UsesStatementDiagnosticsWhenBothFormsFailAndCodeEndsWithBrace()
        {
            var task = EvaluateAsync("foreach (var x in new[] { 1 }) { Undefined(x); } // statement");
            yield return WaitFor(task, allowCompilerErrors: true);
            var output = ReadResult(task, "COMPILATION_ERROR");
            var diagnostics = ((List<object>)output["diagnostics"]).Cast<Dictionary<string, object>>();
            Assert.That(diagnostics.Select(diagnostic => (string)diagnostic["message"]),
                Has.Some.Contains("CS0103"));
        }

        [TestCase("BUSY")]
        [TestCase("TIMEOUT")]
        [TestCase("INTERNAL_ERROR")]
        public void RetryFailureDoesNotReturnDiagnosticsFromTheFirstAttempt(string error)
        {
            var firstDiagnostics = new[] { new EvalHandler.Diagnostic { severity = "error" } };
            var first = ("COMPILATION_ERROR", firstDiagnostics, (System.Reflection.MethodInfo)null);
            var retry = (error, Array.Empty<EvalHandler.Diagnostic>(), (System.Reflection.MethodInfo)null);

            var result = EvalHandler.SelectCompilationResult(first, retry, preferStatementDiagnostics: true);

            Assert.That(result.error, Is.EqualTo(error));
            Assert.That(result.diagnostics, Is.Empty);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ChoosesDiagnosticsWhenBothCompilationAttemptsFail(bool preferStatementDiagnostics)
        {
            var expressionDiagnostics = new[] { new EvalHandler.Diagnostic { message = "expression" } };
            var statementDiagnostics = new[] { new EvalHandler.Diagnostic { message = "statement" } };
            var first = ("COMPILATION_ERROR", expressionDiagnostics, (System.Reflection.MethodInfo)null);
            var retry = ("COMPILATION_ERROR", statementDiagnostics, (System.Reflection.MethodInfo)null);

            var result = EvalHandler.SelectCompilationResult(first, retry, preferStatementDiagnostics);

            Assert.That(result.diagnostics,
                Is.SameAs(preferStatementDiagnostics ? statementDiagnostics : expressionDiagnostics));
        }

        [Test]
        public void CleansStaleTempFilesOnlyOnce()
        {
            var root = Path.Combine(Path.GetTempPath(), "UniForgeEvalTest-" + Guid.NewGuid().ToString("N"));
            var stale = Path.Combine(root, "stale");
            var cleaned = false;
            try
            {
                Directory.CreateDirectory(stale);
                File.WriteAllText(Path.Combine(stale, "eval.cs"), "");
                EvalHandler.CleanTempDirectoryOnce(root, ref cleaned);
                Assert.That(Directory.Exists(root), Is.False);

                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "current.cs"), "");
                EvalHandler.CleanTempDirectoryOnce(root, ref cleaned);
                Assert.That(File.Exists(Path.Combine(root, "current.cs")), Is.True);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [UnityTest]
        public IEnumerator RejectsMissingCodeAndInvalidTimeout()
        {
            var missingCode = EvaluateAsync(null);
            yield return WaitFor(missingCode);
            ReadResult(missingCode, "INVALID_ARGUMENT");

            foreach (var timeoutMs in new[] { 0, 600001, long.MaxValue })
            {
                var task = EvaluateAsync("return 1;", timeoutMs);
                yield return WaitFor(task);
                ReadResult(task, "INVALID_ARGUMENT");
            }
        }

        [UnityTest]
        public IEnumerator RejectsDisabledTool()
        {
            ToolSettings.instance.SetToolEnabled(ToolName, false);
            var task = EvaluateAsync("throw new Exception();");
            yield return WaitFor(task);
            ReadResult(task, "DISABLED");
        }

        [UnityTest]
        public IEnumerator RejectsSecondRequestWhileFirstIsRunning()
        {
            var requestId = Guid.NewGuid().ToString("N");
            var key = EvalHandler.RequestRecordPath(requestId);
            var gate = NewSessionKey();
            var previousGate = SessionState.GetString(gate, "");
            var secondRequestId = Guid.NewGuid().ToString("N");
            var secondKey = EvalHandler.RequestRecordPath(secondRequestId);
            try
            {
                SessionState.SetString(gate, "waiting");
                var first = EvaluateAsync(
                    $"while (SessionState.GetString(\"{gate}\", \"\") == \"waiting\") await Task.Delay(10, cancellationToken);\nreturn 1;",
                    requestId: requestId);
                Assert.That(ReadRecord(key), Is.EqualTo("compiling"));
                var compilingReplay = EvaluateAsync("return 2;", requestId: requestId);
                Assert.That(compilingReplay.IsCompleted, Is.False);
                var clock = Stopwatch.StartNew();
                while (!first.IsCompleted && ReadRecord(key) != "running")
                {
                    Assert.That(clock.ElapsedMilliseconds, Is.LessThan(30000), "The first request did not start running.");
                    yield return null;
                }

                Assert.That(first.IsCompleted, Is.False, "The first request already returned.");
                WriteRecord(secondKey, "compiling");
                var second = EvaluateAsync("return 2;", requestId: secondRequestId);
                yield return WaitFor(second);
                ReadResult(second, "BUSY");
                Assert.That(ReadRecord(secondKey), Is.Empty);

                var replay = EvaluateAsync("return 2;", requestId: requestId);
                Assert.That(replay.IsCompleted, Is.False);

                SessionState.SetString(gate, "done");
                yield return WaitFor(first);
                Assert.That(ReadResult(first)["value"], Is.EqualTo(1));
                yield return WaitFor(compilingReplay);
                yield return WaitFor(replay);
                Assert.That(compilingReplay.Result.ResultText, Is.EqualTo(first.Result.ResultText));
                Assert.That(replay.Result.ResultText, Is.EqualTo(first.Result.ResultText));
            }
            finally
            {
                RestoreSessionString(gate, previousGate);
                File.Delete(key);
                File.Delete(secondKey);
            }
        }

        private static async Task<ToolResult> EvaluateAsync(
            string code, long? timeoutMs = null, bool dryRun = false, string requestId = null)
        {
            var args = new Dictionary<string, object> { ["dry_run"] = dryRun };
            if (code != null)
                args["code"] = code;
            if (timeoutMs.HasValue)
                args["timeout_ms"] = timeoutMs.Value;
            if (requestId != null)
                args["request_id"] = requestId;
            return await new EvalHandler().ExecuteAsync(SimpleJson.Serialize(args));
        }

        private static IEnumerator WaitFor(Task<ToolResult> task, bool allowCompilerErrors = false)
        {
            var previous = LogAssert.ignoreFailingMessages;
            try
            {
                if (allowCompilerErrors)
                    LogAssert.ignoreFailingMessages = true;

                var clock = Stopwatch.StartNew();
                while (!task.IsCompleted)
                {
                    Assert.That(clock.ElapsedMilliseconds, Is.LessThan(35000), "Eval did not return a result.");
                    yield return null;
                }
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        private static Dictionary<string, object> ReadResult(Task<ToolResult> task, string errorCode = null)
        {
            var result = task.GetAwaiter().GetResult();
            Assert.That(result.Kind, Is.EqualTo(ToolResultKind.Complete));
            var json = result.ResultText;
            var output = SimpleJson.Parse(json);
            Assert.That(result.Success, Is.EqualTo(errorCode == null), json);
            Assert.That(output["success"], Is.EqualTo(result.Success), json);
            Assert.That(output.ContainsKey("value"), Is.EqualTo(result.Success), json);
            Assert.That(output.ContainsKey("value_type"), Is.EqualTo(result.Success), json);
            if (errorCode != null)
                Assert.That(output["error_code"], Is.EqualTo(errorCode), json);
            return output;
        }

        private static string[] GetEvalDirectories() =>
            Directory.Exists(EvalTempPath) ? Directory.GetDirectories(EvalTempPath) : Array.Empty<string>();

        private static string NewSessionKey() => "UniForge.Tests.Eval." + Guid.NewGuid().ToString("N");

        private static string ReadRecord(string path) => File.Exists(path) ? File.ReadAllText(path) : "";

        private static void WriteRecord(string path, string phase)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, phase);
        }

        private static void RestoreSessionString(string key, string previous)
        {
            if (previous.Length == 0)
                SessionState.EraseString(key);
            else
                SessionState.SetString(key, previous);
        }

        [Test]
        public void StripDiagnosticLocation_RemovesPathAndSeverityPrefix()
        {
            Assert.That(
                EvalHandler.StripDiagnosticLocation("Temp/UniForgeEval/x/eval(2,8): error CS0103: The name 'y' does not exist"),
                Is.EqualTo("CS0103: The name 'y' does not exist"));
            Assert.That(
                EvalHandler.StripDiagnosticLocation("a.cs(1,5): warning CS0219: unused (x,y): error text"),
                Is.EqualTo("CS0219: unused (x,y): error text"));
            Assert.That(EvalHandler.StripDiagnosticLocation("plain message"), Is.EqualTo("plain message"));
        }

        [Test]
        public void TrimStackTrace_DropsHandlerFrames()
        {
            var trace = "  at UniForgeEvalScript.Run () [0x0] in <x>:0 \n" +
                        "  at UniForge.Tools.Mutations.EvalHandler.RunAsync () [0x1] in EvalHandler.cs:1 \n" +
                        "  at Other.Frame ()";
            Assert.That(EvalHandler.TrimStackTrace(trace), Is.EqualTo("  at UniForgeEvalScript.Run () [0x0] in <x>:0"));
            Assert.That(EvalHandler.TrimStackTrace("  at Only.User ()"), Is.EqualTo("  at Only.User ()"));
            Assert.That(EvalHandler.TrimStackTrace(null), Is.Null);
        }
    }
}
