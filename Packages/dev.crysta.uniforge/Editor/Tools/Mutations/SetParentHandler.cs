using UnityEditor;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// GameObject の親を変更するツール（バッチ対応）
    /// </summary>
    [Tool("set-parent",
        Description = "Change the parent of a GameObject",
        Title = "Set Parent",
        Category = ToolCategory.GameObject,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = true)]
    public class SetParentHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Array of parent change operations", Required = true)]
            public ParentOperation[] operations;

            [ToolParameter("Maintain world position/rotation/scale for all", Default = true)]
            public bool world_position_stays;
        }

        /// <summary>親変更操作</summary>
        public class ParentOperation
        {
            public string path;
            public int? instance_id;
            public string scene;
            public string new_parent;
            public int? new_parent_id;
        }

        /// <summary>個別結果</summary>
        public class ParentResult
        {
            public bool success;
            public int? instance_id;
            public string old_parent;
            public string new_parent;
            public string new_path;
            public string error;
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var operations = args.GetObjectArray<ParentOperation>("operations");
            var worldPositionStays = args.GetBool("world_position_stays", true);

            if (operations == null || operations.Length == 0)
            {
                return ToolResult.Fail("Parameter 'operations' is required and must be a non-empty array");
            }

            var builder = new BatchResultBuilder<ParentResult>();

            foreach (var op in operations)
            {
                var result = ProcessOperation(op, worldPositionStays);
                builder.Add(result, result.success);
            }

            return ToolResult.Ok(builder.Build("set-parent"));
        }

        private ParentResult ProcessOperation(ParentOperation op, bool worldPositionStays)
        {
            // GameObject 解決
            var resolveResult = GameObjectResolver.Resolve(op.path, op.instance_id, op.scene);
            if (!resolveResult.Success)
            {
                return new ParentResult { success = false, error = resolveResult.Error };
            }

            var go = resolveResult.GameObject;
            var oldParentName = go.transform.parent != null ? go.transform.parent.name : "(root)";

            // 新しい親を解決
            Transform newParent = null;

            if (!string.IsNullOrEmpty(op.new_parent) || op.new_parent_id.HasValue)
            {
                var parentResult = GameObjectResolver.Resolve(op.new_parent, op.new_parent_id, op.scene);
                if (!parentResult.Success)
                {
                    return new ParentResult
                    {
                        success = false,
                        instance_id = go.GetInstanceID(),
                        error = $"New parent not found: {parentResult.Error}"
                    };
                }
                newParent = parentResult.GameObject.transform;

                // 循環参照チェック
                if (IsDescendantOf(newParent, go.transform))
                {
                    return new ParentResult
                    {
                        success = false,
                        instance_id = go.GetInstanceID(),
                        error = "Cannot set a descendant as parent (circular reference)"
                    };
                }
            }

            // 親変更
            Undo.SetTransformParent(go.transform, newParent, worldPositionStays, $"Set parent for {go.name}");

            var newParentName = newParent != null ? newParent.name : "(root)";
            var newPath = GameObjectResolver.GetHierarchyPath(go);

            return new ParentResult
            {
                success = true,
                instance_id = go.GetInstanceID(),
                old_parent = oldParentName,
                new_parent = newParentName,
                new_path = newPath
            };
        }

        /// <summary>
        /// potentialChild が target の子孫かどうかを判定
        /// </summary>
        private bool IsDescendantOf(Transform potentialChild, Transform target)
        {
            var current = potentialChild;
            while (current != null)
            {
                if (current == target)
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }
    }
}
