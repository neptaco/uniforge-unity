using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// GameObject のプロパティを変更するツール（バッチ対応）
    /// </summary>
    [Tool("modify-gameobject",
        Description = "Modify GameObject properties like name, tag, layer, and active state",
        Title = "Modify GameObject",
        Category = ToolCategory.GameObject,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = true)]
    public class ModifyGameObjectHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Array of modify operations", Required = true)]
            public ModifyOperation[] operations;
        }

        /// <summary>変更操作</summary>
        public class ModifyOperation
        {
            public string path;
            public int? instance_id;
            public string scene;
            public string name;
            public string tag;
            public int? layer;
            public bool? active;
            public bool? is_static;
        }

        /// <summary>個別結果</summary>
        public class ModifyResult
        {
            public bool success;
            public int? instance_id;
            public Dictionary<string, object> modified;
            public string error;
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var operations = args.GetObjectArray<ModifyOperation>("operations");

            if (operations == null || operations.Length == 0)
            {
                return ToolResult.Fail("Parameter 'operations' is required and must be a non-empty array");
            }

            var builder = new BatchResultBuilder<ModifyResult>();

            foreach (var op in operations)
            {
                var result = ProcessOperation(op);
                builder.Add(result, result.success);
            }

            return ToolResult.Ok(builder.Build("modify"));
        }

        private ModifyResult ProcessOperation(ModifyOperation op)
        {
            // GameObject 解決
            var resolveResult = GameObjectResolver.Resolve(op.path, op.instance_id, op.scene);
            if (!resolveResult.Success)
            {
                return new ModifyResult { success = false, error = resolveResult.Error };
            }

            var go = resolveResult.GameObject;
            var modified = new Dictionary<string, object>();

            // 変更前の状態を記録
            Undo.RecordObject(go, "Modify GameObject");

            // name
            if (!string.IsNullOrEmpty(op.name) && go.name != op.name)
            {
                go.name = op.name;
                modified["name"] = op.name;
            }

            // tag
            if (!string.IsNullOrEmpty(op.tag))
            {
                // タグが存在するか確認
                try
                {
                    if (go.tag != op.tag)
                    {
                        go.tag = op.tag;
                        modified["tag"] = op.tag;
                    }
                }
                catch (UnityException)
                {
                    return new ModifyResult
                    {
                        success = false,
                        instance_id = go.GetInstanceID(),
                        error = $"Invalid tag: {op.tag}. Tag must be defined in Tags and Layers settings."
                    };
                }
            }

            // layer
            if (op.layer.HasValue)
            {
                if (op.layer.Value < 0 || op.layer.Value > 31)
                {
                    return new ModifyResult
                    {
                        success = false,
                        instance_id = go.GetInstanceID(),
                        error = $"Layer must be between 0 and 31. Got: {op.layer.Value}"
                    };
                }
                if (go.layer != op.layer.Value)
                {
                    go.layer = op.layer.Value;
                    modified["layer"] = op.layer.Value;
                }
            }

            // active
            if (op.active.HasValue && go.activeSelf != op.active.Value)
            {
                go.SetActive(op.active.Value);
                modified["active"] = op.active.Value;
            }

            // isStatic
            if (op.is_static.HasValue && go.isStatic != op.is_static.Value)
            {
                go.isStatic = op.is_static.Value;
                modified["is_static"] = op.is_static.Value;
            }

            if (modified.Count > 0)
            {
                EditorUtility.SetDirty(go);
            }

            return new ModifyResult
            {
                success = true,
                instance_id = go.GetInstanceID(),
                modified = modified.Count > 0 ? modified : null
            };
        }
    }
}
