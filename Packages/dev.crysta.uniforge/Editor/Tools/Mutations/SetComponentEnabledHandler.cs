using UnityEditor;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// コンポーネントの有効/無効を切り替えるツール（バッチ対応）
    /// </summary>
    [Tool("set-component-enabled",
        Description = "Enable or disable a component",
        Title = "Set Component Enabled",
        Category = ToolCategory.GameObject,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = true)]
    public partial class SetComponentEnabledHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Array of enable operations", Required = true)]
            public EnableOperation[] operations;
        }

        /// <summary>有効化操作</summary>
        public class EnableOperation
        {
            public string path;
            public int? instance_id;
            public string scene;
            public string component_type;
            public bool enabled;
        }

        /// <summary>個別結果</summary>
        public class EnableResult
        {
            public bool success;
            public int? instance_id;
            public string component_type;
            public bool enabled;
            public string error;
        }

        private ToolDefinition _definition;

        public override ToolDefinition Definition
            => _definition ??= ToolDefinitionBuilder.FromHandler<SetComponentEnabledHandler>();

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var operations = args.GetObjectArray<EnableOperation>("operations");

            if (operations == null || operations.Length == 0)
            {
                return ToolResult.Fail("Parameter 'operations' is required and must be a non-empty array");
            }

            var builder = new BatchResultBuilder<EnableResult>();

            foreach (var op in operations)
            {
                var result = ProcessOperation(op);
                builder.Add(result, result.success);
            }

            return ToolResult.Ok(builder.Build("set-component-enabled"));
        }

        private EnableResult ProcessOperation(EnableOperation op)
        {
            // コンポーネント型チェック
            if (string.IsNullOrEmpty(op.component_type))
            {
                return new EnableResult { success = false, error = "Parameter 'component_type' is required" };
            }

            // GameObject 解決
            var resolveResult = GameObjectResolver.Resolve(op.path, op.instance_id, op.scene);
            if (!resolveResult.Success)
            {
                return new EnableResult { success = false, error = resolveResult.Error };
            }

            var go = resolveResult.GameObject;

            // コンポーネントを検索
            var targetComponent = ComponentPropertySetter.FindComponent(go, op.component_type);

            if (targetComponent == null)
            {
                return new EnableResult
                {
                    success = false,
                    instance_id = go.GetInstanceID(),
                    error = $"Component not found: {op.component_type}"
                };
            }

            // enabled プロパティを持つかチェック
            var actualTypeName = targetComponent.GetType().Name;

            if (targetComponent is Behaviour behaviour)
            {
                Undo.RecordObject(behaviour, $"Set {actualTypeName} Enabled");
                behaviour.enabled = op.enabled;
            }
            else if (targetComponent is Renderer renderer)
            {
                Undo.RecordObject(renderer, $"Set {actualTypeName} Enabled");
                renderer.enabled = op.enabled;
            }
            else if (targetComponent is Collider collider)
            {
                Undo.RecordObject(collider, $"Set {actualTypeName} Enabled");
                collider.enabled = op.enabled;
            }
            else if (targetComponent is Cloth cloth)
            {
                Undo.RecordObject(cloth, $"Set {actualTypeName} Enabled");
                cloth.enabled = op.enabled;
            }
            else if (targetComponent is LODGroup lodGroup)
            {
                Undo.RecordObject(lodGroup, $"Set {actualTypeName} Enabled");
                lodGroup.enabled = op.enabled;
            }
            else
            {
                return new EnableResult
                {
                    success = false,
                    instance_id = go.GetInstanceID(),
                    component_type = actualTypeName,
                    error = $"Component {actualTypeName} does not have an 'enabled' property"
                };
            }

            EditorUtility.SetDirty(go);

            return new EnableResult
            {
                success = true,
                instance_id = go.GetInstanceID(),
                component_type = actualTypeName,
                enabled = op.enabled
            };
        }
    }
}
