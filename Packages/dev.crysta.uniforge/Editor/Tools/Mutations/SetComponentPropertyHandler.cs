using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// コンポーネントのプロパティを設定するツール（バッチ対応）
    /// </summary>
    [Tool("set-component-property",
        Description = "Set properties on a component",
        Title = "Set Component Property",
        Category = ToolCategory.GameObject,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = true)]
    public partial class SetComponentPropertyHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Array of property operations", Required = true)]
            public PropertyOperation[] operations;
        }

        /// <summary>プロパティ操作</summary>
        public class PropertyOperation
        {
            public string path;
            public int? instance_id;
            public string scene;
            public string component_type;

            [ToolParameter("Properties to set. Vector2: [x,y], Vector3: [x,y,z], Color: [r,g,b,a] (0-1), Enum: index or name string, ObjectReference: asset path string or instance_id int")]
            public Dictionary<string, object> properties;
        }

        /// <summary>個別結果</summary>
        public class PropertyResult
        {
            public bool success;
            public int? instance_id;
            public string component_type;
            public Dictionary<string, object> set_properties;
            public string[] errors;
            public string error;
        }

        private ToolDefinition _definition;

        public override ToolDefinition Definition
            => _definition ??= ToolDefinitionBuilder.FromHandler<SetComponentPropertyHandler>();

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var operations = args.GetObjectArray<PropertyOperation>("operations");

            if (operations == null || operations.Length == 0)
            {
                return ToolResult.Fail("Parameter 'operations' is required and must be a non-empty array");
            }

            var builder = new BatchResultBuilder<PropertyResult>();

            foreach (var op in operations)
            {
                try
                {
                    var result = ProcessOperation(op);
                    builder.Add(result, result.success);
                }
                catch (Exception ex)
                {
                    builder.Add(new PropertyResult
                    {
                        success = false,
                        error = $"Unexpected error: {ex.Message}"
                    }, false);
                }
            }

            return ToolResult.Ok(builder.Build("set-component-property"));
        }

        private PropertyResult ProcessOperation(PropertyOperation op)
        {
            // コンポーネント型チェック
            if (string.IsNullOrEmpty(op.component_type))
            {
                return new PropertyResult { success = false, error = "Parameter 'component_type' is required" };
            }

            // プロパティチェック
            if (op.properties == null || op.properties.Count == 0)
            {
                return new PropertyResult { success = false, error = "Parameter 'properties' is required and must not be empty" };
            }

            // GameObject 解決
            var resolveResult = GameObjectResolver.Resolve(op.path, op.instance_id, op.scene);
            if (!resolveResult.Success)
            {
                return new PropertyResult { success = false, error = resolveResult.Error };
            }

            var go = resolveResult.GameObject;

            // コンポーネントを検索
            var targetComponent = ComponentPropertySetter.FindComponent(go, op.component_type);

            if (targetComponent == null)
            {
                return new PropertyResult
                {
                    success = false,
                    instance_id = go.GetInstanceID(),
                    error = $"Component not found: {op.component_type}"
                };
            }

            var actualTypeName = targetComponent.GetType().Name;

            // プロパティを設定
            var propResult = ComponentPropertySetter.SetProperties(targetComponent, op.properties);

            // 成功判定: 少なくとも1つのプロパティが設定されたら成功
            bool success = propResult.set_properties.Count > 0;

            return new PropertyResult
            {
                success = success,
                instance_id = go.GetInstanceID(),
                component_type = actualTypeName,
                set_properties = propResult.set_properties.Count > 0 ? propResult.set_properties : null,
                errors = propResult.errors.Count > 0 ? propResult.errors.ToArray() : null,
                error = !success && propResult.errors.Count > 0 ? string.Join("; ", propResult.errors) : null
            };
        }
    }
}
