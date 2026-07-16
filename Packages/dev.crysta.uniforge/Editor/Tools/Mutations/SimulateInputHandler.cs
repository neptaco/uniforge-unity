using System.Collections.Generic;
using UnityEngine;
using UniForge.Services;
using UniForge.Tools.Queries;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// キー入力・マウス操作をシミュレートするツール。
    /// コアロジックは AutoPlayService に委譲。
    /// </summary>
    [Tool("simulate-input",
        Description = "Simulate keyboard, mouse, and UI input during play mode without activating the Unity Editor or moving the physical cursor. Keyboard and coordinate mouse actions inject Input System events; tap_ui dispatches through EventSystem; input_text sets InputField/TMP_InputField text directly. Legacy Input Manager injection is intentionally unsupported. Use wait_ms to wait after the action and collect game logs in the response.",
        Title = "Simulate Input",
        Category = ToolCategory.Input,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = false)]
    [ToolOutput(typeof(Output))]
    public partial class SimulateInputHandler : MutationHandler
    {
        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var waitMs = args.GetNullableInt("wait_ms") ?? 0;
            var action = (args.GetString("action") ?? "").ToLowerInvariant();

            if (action == "wait" || waitMs > 0)
            {
                return ToolResult.Fail(
                    "simulate-input wait and wait_ms require async execution. Execute the tool through UniForgeService or ToolDispatcher.DispatchAsync.");
            }

            var result = AutoPlayService.Instance.ExecuteStep(args.Json);
            return ToToolResult(result);
        }

        protected internal override async Awaitable<ToolResult> ExecuteAsync(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var result = await AutoPlayService.Instance.ExecuteStepAsync(args.Json);
            return ToToolResult(result);
        }

        private static ToolResult ToToolResult(AutoPlayService.StepResult result)
        {
            if (!result.Success)
                return ToolResult.Fail(result.Error);

            var output = new Output
            {
                success = true,
                action = result.Action,
                details = result.Details,
                message = result.Message,
                simulator_type = result.SimulatorType,
                hit_ui = result.hit_ui,
                ui_hits = result.ui_hits
            };

            if (result.WaitedMs > 0)
            {
                var merged = MergePayloads(output, result);
                return ToolResult.Complete(merged);
            }

            return ToolResult.Ok(output);
        }

        private static Dictionary<string, object> MergePayloads(Output output, AutoPlayService.StepResult result)
        {
            var baseDict = SimpleJson.Parse(SimpleJson.Serialize(output));

            baseDict["waited_ms"] = result.WaitedMs;
            baseDict["log_count"] = result.LogCount;

            var logsList = new List<object>();
            if (result.Logs != null)
            {
                foreach (var log in result.Logs)
                {
                    logsList.Add(SimpleJson.Parse(SimpleJson.Serialize(log)));
                }
            }
            baseDict["logs"] = logsList;

            return baseDict;
        }
    }
}
