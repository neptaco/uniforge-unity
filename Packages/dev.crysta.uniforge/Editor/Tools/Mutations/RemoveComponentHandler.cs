using System;
using UnityEditor;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// GameObject からコンポーネントを削除するツール（バッチ対応）
    /// </summary>
    [Tool("remove-component",
        Description = "Remove a component from a GameObject",
        Title = "Remove Component",
        Category = ToolCategory.GameObject,
        Kind = ToolKind.Mutation,
        Destructive = true,
        Idempotent = true)]
    public class RemoveComponentHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Array of remove operations", Required = true)]
            public RemoveOperation[] operations;
        }

        /// <summary>削除操作</summary>
        public class RemoveOperation
        {
            public string path;
            public int? instance_id;
            public string scene;
            public string component_type;
            public bool remove_all;
        }

        /// <summary>個別結果</summary>
        public class RemoveResult
        {
            public bool success;
            public int? instance_id;
            public string component_type;
            public int removed_count;
            public string error;
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var operations = args.GetObjectArray<RemoveOperation>("operations");

            if (operations == null || operations.Length == 0)
            {
                return ToolResult.Fail("Parameter 'operations' is required and must be a non-empty array");
            }

            var builder = new BatchResultBuilder<RemoveResult>();

            foreach (var op in operations)
            {
                var result = ProcessOperation(op);
                builder.Add(result, result.success);
            }

            return ToolResult.Ok(builder.Build("remove-component"));
        }

        private RemoveResult ProcessOperation(RemoveOperation op)
        {
            // コンポーネント型チェック
            if (string.IsNullOrEmpty(op.component_type))
            {
                return new RemoveResult { success = false, error = "Parameter 'component_type' is required" };
            }

            // Transform は削除不可
            if (op.component_type.Equals("Transform", StringComparison.OrdinalIgnoreCase) ||
                op.component_type.Equals("RectTransform", StringComparison.OrdinalIgnoreCase))
            {
                return new RemoveResult
                {
                    success = false,
                    error = "Cannot remove Transform or RectTransform component"
                };
            }

            // GameObject 解決
            var resolveResult = GameObjectResolver.Resolve(op.path, op.instance_id, op.scene);
            if (!resolveResult.Success)
            {
                return new RemoveResult { success = false, error = resolveResult.Error };
            }

            var go = resolveResult.GameObject;

            // コンポーネントを検索（名前照合は ComponentLookup に集約）
            var matches = ComponentLookup.FindComponents(go, op.component_type);
            int removedCount = 0;
            string actualTypeName = null;

            foreach (var component in matches)
            {
                var typeName = component.GetType().Name;
                actualTypeName = typeName;

                try
                {
                    Undo.DestroyObjectImmediate(component);
                    removedCount++;

                    if (!op.remove_all)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    // 依存関係のエラーなど
                    return new RemoveResult
                    {
                        success = false,
                        instance_id = go.GetInstanceID(),
                        component_type = typeName,
                        removed_count = removedCount,
                        error = $"Failed to remove {typeName}: {ex.Message}. " +
                            "This may be due to other components depending on it (RequireComponent)."
                    };
                }
            }

            if (removedCount == 0)
            {
                return new RemoveResult
                {
                    success = false,
                    instance_id = go.GetInstanceID(),
                    error = $"Component not found: {op.component_type}"
                };
            }

            return new RemoveResult
            {
                success = true,
                instance_id = go.GetInstanceID(),
                component_type = actualTypeName,
                removed_count = removedCount
            };
        }
    }
}
