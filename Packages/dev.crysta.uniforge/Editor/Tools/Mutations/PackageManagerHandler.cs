using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// Unity Package Manager を操作するツール
    /// </summary>
    [Tool("package-manager",
        Description = "Manage Unity packages: list installed packages, add or remove packages by name",
        Title = "Package Manager",
        Category = ToolCategory.Editor,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = false)]
    public class PackageManagerHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Action to perform", Required = true, Enum = "list,add,remove")]
            public string action;

            [ToolParameter("Package identifier for add/remove (e.g., 'com.unity.inputsystem', 'com.unity.textmeshpro@3.0.6')", Required = false)]
            public string package_id;
        }

        /// <summary>出力定義</summary>
        public class ListOutput
        {
            public bool success;
            public string action;
            public List<PackageInfo> packages;
            public int count;
        }

        public class PackageInfo
        {
            public string name;
            public string version;
            public string display_name;
            public string source;
        }

        public class ActionOutput
        {
            public bool success;
            public string action;
            public string package_id;
            public string message;
        }

        protected internal override async Awaitable<ToolResult> ExecuteAsync(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var action = args.GetString("action");
            var packageId = args.GetString("package_id");

            if (string.IsNullOrEmpty(action))
                return ToolResult.Fail("Parameter 'action' is required");

            switch (action.ToLowerInvariant())
            {
                case "list":
                    return await ExecuteListAsync();

                case "add":
                    if (string.IsNullOrEmpty(packageId))
                        return ToolResult.Fail("Parameter 'package_id' is required for 'add' action");
                    return await ExecuteAddAsync(packageId);

                case "remove":
                    if (string.IsNullOrEmpty(packageId))
                        return ToolResult.Fail("Parameter 'package_id' is required for 'remove' action");
                    return await ExecuteRemoveAsync(packageId);

                default:
                    return ToolResult.Fail($"Invalid action: {action}. Valid actions: list, add, remove");
            }
        }

        private async Awaitable<ToolResult> ExecuteListAsync()
        {
            var request = Client.List(true);
            if (!await WaitForRequestAsync(request, 30000))
                return ToolResult.Fail("Package list request timed out");

            if (request.Status == StatusCode.Failure)
                return ToolResult.Fail($"Failed to list packages: {request.Error?.message ?? "Unknown error"}");

            var packages = request.Result
                .Where(p => p.source != PackageSource.BuiltIn)
                .OrderBy(p => p.name)
                .Select(p => new PackageInfo
                {
                    name = p.name,
                    version = p.version,
                    display_name = p.displayName,
                    source = p.source.ToString()
                })
                .ToList();

            return ToolResult.Ok(new ListOutput
            {
                success = true,
                action = "list",
                packages = packages,
                count = packages.Count
            });
        }

        private async Awaitable<ToolResult> ExecuteAddAsync(string packageId)
        {
            var request = Client.Add(packageId);
            if (!await WaitForRequestAsync(request, 120000))
                return ToolResult.Fail($"Package add request timed out for '{packageId}'");

            if (request.Status == StatusCode.Failure)
                return ToolResult.Fail($"Failed to add package '{packageId}': {request.Error?.message ?? "Unknown error"}");

            var result = request.Result;
            return ToolResult.Ok(new ActionOutput
            {
                success = true,
                action = "add",
                package_id = result.packageId,
                message = $"Successfully added {result.displayName} ({result.name}@{result.version})"
            });
        }

        private async Awaitable<ToolResult> ExecuteRemoveAsync(string packageId)
        {
            var request = Client.Remove(packageId);
            if (!await WaitForRequestAsync(request, 60000))
                return ToolResult.Fail($"Package remove request timed out for '{packageId}'");

            if (request.Status == StatusCode.Failure)
                return ToolResult.Fail($"Failed to remove package '{packageId}': {request.Error?.message ?? "Unknown error"}");

            return ToolResult.Ok(new ActionOutput
            {
                success = true,
                action = "remove",
                package_id = packageId,
                message = $"Successfully removed '{packageId}'"
            });
        }

        private static async Awaitable<bool> WaitForRequestAsync(Request request, int timeoutMs)
        {
            var startTime = DateTime.UtcNow;
            while (!request.IsCompleted)
            {
                if ((DateTime.UtcNow - startTime).TotalMilliseconds > timeoutMs)
                    return false;
                await Awaitable.WaitForSecondsAsync(0.1f);
            }
            return true;
        }
    }
}
