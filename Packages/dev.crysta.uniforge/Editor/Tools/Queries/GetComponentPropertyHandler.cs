using System;
using System.Collections.Generic;
using UnityEngine;
using UniForge.Tools.Mutations;

namespace UniForge.Tools.Queries
{
    /// <summary>
    /// コンポーネントのプロパティを取得するツール（バッチ対応）
    /// </summary>
    [Tool("component-property",
        Description = "Get properties from a component",
        Title = "Get Component Property",
        Kind = ToolKind.Query,
        Idempotent = true)]
    public partial class GetComponentPropertyHandler : QueryHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Array of property query operations", Required = true)]
            public PropertyQueryOperation[] operations;
        }

        /// <summary>プロパティ取得操作</summary>
        public class PropertyQueryOperation
        {
            [ToolParameter("Hierarchy path (e.g., 'Canvas/Panel')")]
            public string path;

            [ToolParameter("GameObject instance ID")]
            public int? instance_id;

            [ToolParameter("Scene name or path (for path search)")]
            public string scene;

            [ToolParameter("Component type name (short or fully qualified)", Required = true)]
            public string component_type;

            [ToolParameter("Property names to retrieve. Omit or pass an empty array to return all serialized properties.")]
            public string[] property_names;
        }

        /// <summary>個別結果</summary>
        public class PropertyQueryResult
        {
            public bool success;
            public int? instance_id;
            public string component_type;
            public int property_count;
            public Dictionary<string, object> properties;
            public string[] errors;
            public string error;
        }

        private ToolDefinition _definition;

        public override ToolDefinition Definition
            => _definition ??= ToolDefinitionBuilder.FromHandler<GetComponentPropertyHandler>();

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var operations = args.GetObjectArray<PropertyQueryOperation>("operations");

            if (operations == null || operations.Length == 0)
            {
                return ToolResult.Fail("Parameter 'operations' is required and must be a non-empty array");
            }

            var builder = new BatchResultBuilder<PropertyQueryResult>();

            foreach (var op in operations)
            {
                try
                {
                    var result = ProcessOperation(op);
                    builder.Add(result, result.success);
                }
                catch (Exception ex)
                {
                    builder.Add(new PropertyQueryResult
                    {
                        success = false,
                        error = $"Unexpected error: {ex.Message}"
                    }, false);
                }
            }

            return ToolResult.Ok(builder.Build("component-property"));
        }

        private PropertyQueryResult ProcessOperation(PropertyQueryOperation op)
        {
            // コンポーネント型チェック
            if (string.IsNullOrEmpty(op.component_type))
            {
                return new PropertyQueryResult { success = false, error = "Parameter 'component_type' is required" };
            }

            // GameObject 解決
            var resolveResult = GameObjectResolver.Resolve(op.path, op.instance_id, op.scene);
            if (!resolveResult.Success)
            {
                return new PropertyQueryResult { success = false, error = resolveResult.Error };
            }

            var go = resolveResult.GameObject;

            // コンポーネントを検索
            var targetComponent = ComponentPropertySetter.FindComponent(go, op.component_type);

            if (targetComponent == null)
            {
                return new PropertyQueryResult
                {
                    success = false,
                    instance_id = go.GetInstanceID(),
                    error = $"Component not found: {op.component_type}"
                };
            }

            var actualTypeName = targetComponent.GetType().Name;

            // プロパティを取得
            var propResult = ComponentPropertyGetter.GetProperties(targetComponent, op.property_names);

            return new PropertyQueryResult
            {
                success = propResult.AllSucceeded,
                instance_id = go.GetInstanceID(),
                component_type = actualTypeName,
                property_count = propResult.properties.Count,
                properties = propResult.properties,
                errors = propResult.errors.Count > 0 ? propResult.errors.ToArray() : null,
                error = !propResult.AllSucceeded && propResult.errors.Count > 0 ? string.Join("; ", propResult.errors) : null
            };
        }
    }
}
