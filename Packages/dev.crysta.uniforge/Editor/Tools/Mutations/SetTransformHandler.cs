using UnityEditor;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// Transform の Position/Rotation/Scale を設定するツール（バッチ対応）
    /// </summary>
    [Tool("set-transform",
        Description = "Set GameObject's position, rotation, and/or scale",
        Title = "Set Transform",
        Category = ToolCategory.GameObject,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = true)]
    public class SetTransformHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Array of transform operations", Required = true)]
            public TransformOperation[] operations;

            [ToolParameter("Use local coordinates instead of world", Default = true)]
            public bool local;
        }

        /// <summary>Transform 操作</summary>
        public class TransformOperation
        {
            public string path;
            public int? instance_id;
            public string scene;
            public float[] position;
            public float[] rotation;
            public float[] scale;
        }

        /// <summary>個別結果</summary>
        public class TransformResult
        {
            public bool success;
            public int? instance_id;
            public float[] position;
            public float[] rotation;
            public float[] scale;
            public string error;
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var operations = args.GetObjectArray<TransformOperation>("operations");
            var local = args.GetBool("local", true);

            if (operations == null || operations.Length == 0)
            {
                return ToolResult.Fail("Parameter 'operations' is required and must be a non-empty array");
            }

            var builder = new BatchResultBuilder<TransformResult>();

            foreach (var op in operations)
            {
                var result = ProcessOperation(op, local);
                builder.Add(result, result.success);
            }

            return ToolResult.Ok(builder.Build("transform"));
        }

        private TransformResult ProcessOperation(TransformOperation op, bool local)
        {
            // GameObject 解決
            var resolveResult = GameObjectResolver.Resolve(op.path, op.instance_id, op.scene);
            if (!resolveResult.Success)
            {
                return new TransformResult { success = false, error = resolveResult.Error };
            }

            var go = resolveResult.GameObject;
            var t = go.transform;

            // 少なくとも1つは指定が必要
            if (op.position == null && op.rotation == null && op.scale == null)
            {
                return new TransformResult
                {
                    success = false,
                    instance_id = go.GetInstanceID(),
                    error = "At least one of 'position', 'rotation', or 'scale' is required"
                };
            }

            // Transform パラメータのバリデーション
            if (!GameObjectResolver.TryParseOptionalVector3(op.position, "position", out var position, out var posError))
            {
                return new TransformResult { success = false, instance_id = go.GetInstanceID(), error = posError };
            }
            if (!GameObjectResolver.TryParseOptionalVector3(op.rotation, "rotation", out var rotation, out var rotError))
            {
                return new TransformResult { success = false, instance_id = go.GetInstanceID(), error = rotError };
            }
            if (!GameObjectResolver.TryParseOptionalVector3(op.scale, "scale", out var scale, out var scaleError))
            {
                return new TransformResult { success = false, instance_id = go.GetInstanceID(), error = scaleError };
            }

            // 変更前の状態を記録
            Undo.RecordObject(t, "Set Transform");

            // Position
            if (position.HasValue)
            {
                if (local) t.localPosition = position.Value;
                else t.position = position.Value;
            }

            // Rotation
            if (rotation.HasValue)
            {
                if (local) t.localEulerAngles = rotation.Value;
                else t.eulerAngles = rotation.Value;
            }

            // Scale (常にlocal)
            if (scale.HasValue)
            {
                t.localScale = scale.Value;
            }

            EditorUtility.SetDirty(go);

            return new TransformResult
            {
                success = true,
                instance_id = go.GetInstanceID(),
                position = local
                    ? new[] { t.localPosition.x, t.localPosition.y, t.localPosition.z }
                    : new[] { t.position.x, t.position.y, t.position.z },
                rotation = local
                    ? new[] { t.localEulerAngles.x, t.localEulerAngles.y, t.localEulerAngles.z }
                    : new[] { t.eulerAngles.x, t.eulerAngles.y, t.eulerAngles.z },
                scale = new[] { t.localScale.x, t.localScale.y, t.localScale.z }
            };
        }
    }
}
