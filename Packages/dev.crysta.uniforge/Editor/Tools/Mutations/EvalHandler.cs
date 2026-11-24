using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    [Tool(EvalHandler.ToolName,
        Description = "Compile and run C# in the open Unity Editor, or compile only with dry_run",
        Title = "Evaluate C#",
        Category = ToolCategory.Editor,
        Kind = ToolKind.Mutation,
        Destructive = true,
        Idempotent = false)]
    public class EvalHandler : IToolHandler
    {
        private const string ToolName = "eval";
        private const int DefaultTimeoutMs = 30000;
        private const int MaxTimeoutMs = 600000;
        private const string Compiling = "compiling";
        private const string Running = "running";
        private static readonly string TempDirectory = Path.Combine("Temp", "UniForgeEval");
        private static readonly string RequestRecordDirectory = Path.Combine("Library", "UniForgeEval");
        private static readonly Regex RequestIdPattern = new Regex(@"\A[A-Za-z0-9_-]{1,128}\z");

        private static bool _cleanedTempDirectory;
        private static string _activeRequestId;
        private static TaskCompletionSource<ToolResult> _activeCompletion;
        private static string _lastRequestId;
        private static ToolResult _lastResult;
        private static bool _finishingTimedOutRun;
        private ToolDefinition _definition;

        public class Args
        {
            [ToolParameter("C# code to evaluate", Required = true)]
            public string code;

            [ToolParameter("Time limit for each of compilation and execution in milliseconds (1–600000, default: 30000)")]
            public int? timeout_ms;

            [ToolParameter("Compile without running the code (default: false)")]
            public bool? dry_run;

            [ToolParameter("Unique request ID used to prevent re-execution after a domain reload or reconnection")]
            public string request_id;
        }

        internal class Diagnostic
        {
            public string severity;
            public int line;
            public int column;
            public string message;
        }

        public ToolDefinition Definition => _definition ??= ToolDefinitionBuilder.FromHandler<EvalHandler>();

        internal static string RequestRecordPath(string requestId)
        {
            if (!RequestIdPattern.IsMatch(requestId))
                throw new ArgumentException("Invalid eval request ID.", nameof(requestId));

            // Hash the validated ID to preserve case distinctions on all Editor filesystems.
            using var hash = SHA256.Create();
            var fileName = BitConverter.ToString(hash.ComputeHash(Encoding.ASCII.GetBytes(requestId))).Replace("-", "");
            return Path.Combine(RequestRecordDirectory, fileName + ".state");
        }

        public async Awaitable<ToolResult> ExecuteAsync(string argsJson)
        {
            var output = new Dictionary<string, object>
            {
                ["diagnostics"] = Array.Empty<Diagnostic>(),
                ["compile_ms"] = 0,
                ["run_ms"] = 0
            };

            try
            {
                await Awaitable.MainThreadAsync();
                var args = new ToolArgsParser(argsJson);
                var code = args.GetString("code");
                if (args.HasParseError || string.IsNullOrWhiteSpace(code))
                    return Complete(output, "INVALID_ARGUMENT", "code must be a non-empty string.");

                var fields = args.Json.ToDictionary();
                var timeoutMs = DefaultTimeoutMs;
                if (fields.TryGetValue("timeout_ms", out var timeout))
                {
                    if (!(timeout is long milliseconds) || milliseconds < 1 || milliseconds > MaxTimeoutMs)
                        return Complete(output, "INVALID_ARGUMENT", "timeout_ms must be an integer between 1 and 600000.");
                    timeoutMs = (int)milliseconds;
                }

                if (fields.TryGetValue("dry_run", out var dryRunValue) && !(dryRunValue is bool))
                    return Complete(output, "INVALID_ARGUMENT", "dry_run must be a boolean.");
                var requestId = default(string);
                if (fields.TryGetValue("request_id", out var requestIdValue))
                {
                    if (!(requestIdValue is string id) || !RequestIdPattern.IsMatch(id))
                        return Complete(output, "INVALID_ARGUMENT",
                            "request_id must contain 1 to 128 ASCII letters, digits, hyphens, or underscores.");
                    requestId = id;
                }

                if (requestId != null)
                {
                    if (requestId == _activeRequestId)
                        return await _activeCompletion.Task;
                    if (requestId == _lastRequestId)
                        return _lastResult;
                }

                if (!_cleanedTempDirectory)
                {
                    CleanTempDirectoryOnce(TempDirectory, ref _cleanedTempDirectory);
                    CleanExpiredRequestRecords(RequestRecordDirectory, DateTime.UtcNow);
                }

                var recordPath = requestId == null ? null : RequestRecordPath(requestId);
                if (ReadRequestRecord(recordPath) == Running)
                {
                    return Complete(output, "INTERRUPTED",
                        "This request already started executing and its result was lost (domain reload or reconnection). Side effects may already have occurred.");
                }
                if (!ToolSettings.instance.IsToolEnabled(ToolName))
                {
                    EraseCompilingRequest(recordPath);
                    return Complete(output, "DISABLED", "The eval tool is disabled in Tool Settings.");
                }
                if (_finishingTimedOutRun)
                {
                    EraseCompilingRequest(recordPath);
                    return Complete(output, "BUSY",
                        "A previous eval timed out and its code has not finished. Wait for it, or trigger a script reload (domain reload) to clear it.");
                }
                if (_activeCompletion != null)
                {
                    EraseCompilingRequest(recordPath);
                    return Complete(output, "BUSY", "Another eval is still in progress.");
                }

                var dryRun = args.GetBool("dry_run");
                var completion = new TaskCompletionSource<ToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                _activeRequestId = requestId;
                _activeCompletion = completion;
                var result = await ExecuteNewAsync(code, dryRun, timeoutMs, recordPath, output);

                _lastRequestId = requestId;
                _lastResult = requestId == null ? default : result;
                _activeRequestId = null;
                _activeCompletion = null;
                completion.SetResult(result);
                return result;
            }
            catch (Exception ex)
            {
                return Complete(output, "INTERNAL_ERROR", ex.Message);
            }
        }

        private static string ReadRequestRecord(string recordPath)
        {
            if (recordPath == null)
                return null;

            try
            {
                return File.ReadAllText(recordPath);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
        }

        private static void WriteRequestRecord(string recordPath, string phase)
        {
            if (recordPath == null)
                return;

            Directory.CreateDirectory(RequestRecordDirectory);
            var temporaryPath = recordPath + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, phase);
                if (File.Exists(recordPath))
                    File.Replace(temporaryPath, recordPath, null);
                else
                    File.Move(temporaryPath, recordPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private static void EraseCompilingRequest(string recordPath)
        {
            // Keep the running record across reloads and Editor restarts.
            if (ReadRequestRecord(recordPath) == Compiling)
                File.Delete(recordPath);
        }

        internal static void CleanExpiredRequestRecords(string directory, DateTime nowUtc)
        {
            if (!Directory.Exists(directory))
                return;

            var cutoff = nowUtc.AddHours(-1);
            foreach (var path in Directory.GetFiles(directory, "*.state*"))
            {
                if ((path.EndsWith(".state", StringComparison.Ordinal) || path.EndsWith(".state.tmp", StringComparison.Ordinal))
                    && File.GetLastWriteTimeUtc(path) < cutoff)
                    File.Delete(path);
            }
        }

        private static async Awaitable<ToolResult> ExecuteNewAsync(
            string code, bool dryRun, int timeoutMs, string recordPath, Dictionary<string, object> output)
        {
            try
            {
                try
                {
                    WriteRequestRecord(recordPath, Compiling);
                    return await EvaluateAsync(code, dryRun, timeoutMs, recordPath, output);
                }
                finally
                {
                    EraseCompilingRequest(recordPath);
                }
            }
            catch (Exception ex)
            {
                return Complete(output, "INTERNAL_ERROR", EvalValueConverter.LimitString(ex.Message));
            }
        }

        private static async Awaitable<ToolResult> EvaluateAsync(
            string code, bool dryRun, int timeoutMs, string recordPath, Dictionary<string, object> output)
        {
            var clock = Stopwatch.StartNew();
            MethodInfo run;
            try
            {
                var (first, retry, preferStatementDiagnostics) = EvalSourceBuilder.GetForms(code);
                var attempt = await CompileAsync(code, first, dryRun, clock, timeoutMs);

                if (attempt.error == "COMPILATION_ERROR" && retry.HasValue)
                {
                    var next = await CompileAsync(code, retry.Value, dryRun, clock, timeoutMs);
                    attempt = SelectCompilationResult(attempt, next, preferStatementDiagnostics);
                }
                output["diagnostics"] = attempt.diagnostics;

                if (attempt.error != null)
                {
                    var message = attempt.error switch
                    {
                        "BUSY" => "Unity is already compiling.",
                        "TIMEOUT" => $"Compilation timed out after {timeoutMs} ms.",
                        _ => "C# compilation failed."
                    };
                    return Complete(output, attempt.error, message);
                }

                run = attempt.run;
            }
            finally
            {
                output["compile_ms"] = (int)clock.ElapsedMilliseconds;
            }

            if (dryRun)
            {
                output["value"] = null;
                output["value_type"] = "null";
                return Complete(output);
            }

            if (!ToolSettings.instance.IsToolEnabled(ToolName))
                return Complete(output, "DISABLED", "The eval tool is disabled in Tool Settings.");

            WriteRequestRecord(recordPath, Running);

            return await RunAsync(run, timeoutMs, output);
        }

        internal static (string error, Diagnostic[] diagnostics, MethodInfo run) SelectCompilationResult(
            (string error, Diagnostic[] diagnostics, MethodInfo run) first,
            (string error, Diagnostic[] diagnostics, MethodInfo run) retry,
            bool preferStatementDiagnostics)
        {
            return retry.error switch
            {
                "COMPILATION_ERROR" => preferStatementDiagnostics ? retry : first,
                null => retry,
                _ => (retry.error, Array.Empty<Diagnostic>(), null)
            };
        }

        private static async Awaitable<(string error, Diagnostic[] diagnostics, MethodInfo run)> CompileAsync(
            string code, EvalSourceBuilder.Form form, bool dryRun, Stopwatch clock, int timeoutMs)
        {
            var id = "Eval_" + Guid.NewGuid().ToString("N");
            var directory = Path.Combine(TempDirectory, id);
            var sourcePath = Path.Combine(directory, id + ".cs");
            var assemblyPath = Path.Combine(directory, id + ".dll");
            CompilerMessage[] messages = null;
            var buildStarted = false;
            var deleteWhenFinished = false;

            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(sourcePath, EvalSourceBuilder.Build(code, form));
#pragma warning disable CS0618 // AssemblyBuilder is the Editor compilation API available in Unity 6.0.
                var builder = new AssemblyBuilder(assemblyPath, sourcePath)
                {
                    referencesOptions = ReferencesOptions.UseEngineModules,
                    flags = AssemblyBuilderFlags.EditorAssembly,
                    additionalReferences = CompilationPipeline.GetAssemblies(AssembliesType.Editor)
                        .Select(assembly => assembly.outputPath)
                        .Where(File.Exists)
                        .ToArray()
                };
#pragma warning restore CS0618
                builder.buildFinished += (_, result) =>
                {
                    messages = result;
                    if (deleteWhenFinished)
                        DeleteTempDirectory(directory);
                };

                if (clock.ElapsedMilliseconds >= timeoutMs)
                    return ("TIMEOUT", Array.Empty<Diagnostic>(), null);
                buildStarted = builder.Build();
                if (!buildStarted)
                    return ("BUSY", Array.Empty<Diagnostic>(), null);

                await WaitForEditorUpdateAsync(() => messages != null || clock.ElapsedMilliseconds >= timeoutMs);
                if (clock.ElapsedMilliseconds >= timeoutMs)
                    return ("TIMEOUT", Array.Empty<Diagnostic>(), null);

                var diagnostics = messages.Select(ToDiagnostic).ToArray();
                if (messages.Any(message => message.type == CompilerMessageType.Error))
                    return ("COMPILATION_ERROR", diagnostics, null);
                if (dryRun)
                    return (null, diagnostics, null);

                var assembly = System.Reflection.Assembly.Load(File.ReadAllBytes(assemblyPath));
                var run = assembly.GetType("UniForgeEvalScript", throwOnError: true)
                    .GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                return (null, diagnostics, run);
            }
            finally
            {
                // AssemblyBuilder has no cancellation API. On timeout it still owns these files.
                if (buildStarted && messages == null)
                    deleteWhenFinished = true;
                else
                    DeleteTempDirectory(directory);
            }
        }

        private static Diagnostic ToDiagnostic(CompilerMessage message)
        {
            var fromUserCode = Path.GetFileName(message.file) == "eval";
            return new Diagnostic
            {
                severity = message.type == CompilerMessageType.Error ? "error" : "warning",
                line = fromUserCode ? message.line : 0,
                column = fromUserCode ? message.column : 0,
                message = StripDiagnosticLocation(message.message)
            };
        }

        // Unity prefixes compiler messages with "path(line,column): error "; line and column are reported separately.
        internal static string StripDiagnosticLocation(string message)
        {
            return System.Text.RegularExpressions.Regex.Replace(
                message ?? "", @"^.*?\(\d+,\d+\):\s*(?:error|warning)\s+", "");
        }

        // Drops the frames of this handler below the evaluated code.
        internal static string TrimStackTrace(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace))
                return stackTrace;

            var lines = stackTrace.Split('\n');
            var end = Array.FindIndex(lines, line => line.Contains(typeof(EvalHandler).FullName + "."));
            return end <= 0 ? stackTrace : string.Join("\n", lines, 0, end).TrimEnd();
        }

        private static async Awaitable<ToolResult> RunAsync(
            MethodInfo run, int timeoutMs, Dictionary<string, object> output)
        {
            var cancellation = new CancellationTokenSource();
            var clock = Stopwatch.StartNew();
            try
            {
                var task = (Task<object>)run.Invoke(null, new object[] { cancellation.Token });
                await WaitForEditorUpdateAsync(() => task.IsCompleted || clock.ElapsedMilliseconds >= timeoutMs);
                if (!task.IsCompleted)
                {
                    try
                    {
                        cancellation.Cancel();
                    }
                    catch (AggregateException)
                    {
                        // Exceptions in user cancellation callbacks do not change the timeout result.
                    }

                    if (task.IsCompleted)
                        _ = task.Exception;
                    else
                        FinishTimedOutRun(task, cancellation);

                    return Complete(output, "TIMEOUT",
                        $"Execution timed out after {timeoutMs} ms. The code may still be running.");
                }

                var (value, type) = EvalValueConverter.Convert(task.GetAwaiter().GetResult());
                output["value"] = value;
                output["value_type"] = type;
                return Complete(output);
            }
            catch (Exception ex)
            {
                output["exception"] = new Dictionary<string, object>
                {
                    ["type"] = ex.GetType().FullName,
                    ["message"] = EvalValueConverter.LimitString(ex.Message),
                    ["stack_trace"] = EvalValueConverter.LimitString(TrimStackTrace(ex.StackTrace))
                };
                return Complete(output, "RUNTIME_EXCEPTION", EvalValueConverter.LimitString(ex.Message));
            }
            finally
            {
                output["run_ms"] = (int)clock.ElapsedMilliseconds;
                if (!_finishingTimedOutRun)
                    cancellation.Dispose();
            }
        }

        private static void FinishTimedOutRun(Task task, CancellationTokenSource cancellation)
        {
            _finishingTimedOutRun = true;
            EditorApplication.update += Finish;

            void Finish()
            {
                if (!task.IsCompleted)
                    return;

                EditorApplication.update -= Finish;
                _ = task.Exception;
                cancellation.Dispose();
                _finishingTimedOutRun = false;
            }
        }

        private static async Awaitable WaitForEditorUpdateAsync(Func<bool> ready)
        {
            if (ready())
                return;

            var completion = new AwaitableCompletionSource<bool>();
            EditorApplication.update += Check;
            await completion.Awaitable;

            void Check()
            {
                if (!ready())
                    return;

                EditorApplication.update -= Check;
                completion.SetResult(true);
            }
        }

        internal static void CleanTempDirectoryOnce(string directory, ref bool cleaned)
        {
            if (cleaned)
                return;

            cleaned = true;
            DeleteTempDirectory(directory, ignoreErrors: true);
        }

        private static void DeleteTempDirectory(string directory, bool ignoreErrors = false)
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex)
            {
                if (!ignoreErrors)
                    UnityEngine.Debug.LogWarning($"[UniForge] Could not clean up eval temporary files: {ex.Message}");
            }
        }

        private static ToolResult Complete(
            Dictionary<string, object> output, string error = null, string message = null)
        {
            var success = error == null;
            output["success"] = success;
            if (!success)
            {
                output["error_code"] = error;
                output["message"] = (message ?? error).Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            }

            return ToolResult.Complete(output, success);
        }
    }
}
