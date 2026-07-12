using System;
using UnityEngine;

namespace UniForge.Tools
{
    /// <summary>
    /// ツール実行を管理するディスパッチャー
    /// </summary>
    public class ToolDispatcher
    {
        private readonly ToolRegistry _registry;

        public ToolDispatcher(ToolRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>
        /// ツールを実行
        /// </summary>
        public async Awaitable<ToolResult> DispatchAsync(string toolName, string argsJson)
        {
            var handler = _registry.GetHandler(toolName);
            if (handler == null)
            {
                return ToolResult.Fail($"Unknown tool: {toolName}");
            }

            try
            {
                return await handler.ExecuteAsync(argsJson ?? "{}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ToolDispatcher] Error executing {toolName}: {ex}");
                return ToolResult.FromException(ex);
            }
        }

        /// <summary>
        /// Resume a tool after the Unity domain has reloaded.
        /// </summary>
        public ToolResult ResumeAfterDomainReload(
            string toolName,
            string stateJson,
            DomainReloadResumeContext context)
        {
            var handler = _registry.GetDomainReloadResumableHandler(toolName);
            if (handler == null)
            {
                return ToolResult.Fail($"Tool does not support domain reload resume: {toolName}");
            }

            try
            {
                return handler.ResumeAfterDomainReload(stateJson ?? "{}", context);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ToolDispatcher] Error resuming {toolName} after domain reload: {ex}");
                return ToolResult.FromException(ex);
            }
        }
    }
}
