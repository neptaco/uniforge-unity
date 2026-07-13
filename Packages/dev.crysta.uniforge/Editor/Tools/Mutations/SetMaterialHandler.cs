using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// GameObjectにマテリアルを設定するツール
    /// </summary>
    [Tool("set-material",
        Description = "Set material on a GameObject's Renderer component",
        Title = "Set Material",
        Category = ToolCategory.Material,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = true)]
    public class SetMaterialHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Array of material operations", Required = true)]
            public MaterialOperation[] operations;
        }

        /// <summary>マテリアル操作</summary>
        public class MaterialOperation
        {
            public string path;
            public int? instance_id;
            public string scene;
            public string material_path;
            public int? material_instance_id;
            public int? material_index;
        }

        /// <summary>個別結果</summary>
        public class MaterialResult
        {
            public bool success;
            public int? instance_id;
            public string material_path;
            public string error;
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var operations = args.GetObjectArray<MaterialOperation>("operations");

            if (operations == null || operations.Length == 0)
            {
                return ToolResult.Fail("Parameter 'operations' is required and must be a non-empty array");
            }

            var builder = new BatchResultBuilder<MaterialResult>();

            foreach (var op in operations)
            {
                try
                {
                    var result = ProcessOperation(op);
                    builder.Add(result, result.success);
                }
                catch (Exception ex)
                {
                    builder.Add(new MaterialResult
                    {
                        success = false,
                        error = $"Unexpected error: {ex.Message}"
                    }, false);
                }
            }

            return ToolResult.Ok(builder.Build("set-material"));
        }

        private MaterialResult ProcessOperation(MaterialOperation op)
        {
            // GameObject を解決
            var resolveResult = GameObjectResolver.Resolve(op.path, op.instance_id, op.scene);
            if (!resolveResult.Success)
            {
                return new MaterialResult { success = false, error = resolveResult.Error };
            }

            var go = resolveResult.GameObject;

            // Renderer を取得
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
            {
                return new MaterialResult
                {
                    success = false,
                    instance_id = go.GetInstanceID(),
                    error = "GameObject has no Renderer component"
                };
            }

            // マテリアルを解決
            Material material = null;
            string materialPath = null;

            if (!string.IsNullOrEmpty(op.material_path))
            {
                materialPath = op.material_path.Replace('\\', '/');
                if (!materialPath.StartsWith("Assets/"))
                {
                    materialPath = "Assets/" + materialPath;
                }
                if (!materialPath.EndsWith(".mat"))
                {
                    materialPath += ".mat";
                }

                material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    return new MaterialResult
                    {
                        success = false,
                        instance_id = go.GetInstanceID(),
                        error = $"Material not found: {materialPath}"
                    };
                }
            }
            else if (op.material_instance_id.HasValue)
            {
                var obj = EditorUtility.InstanceIDToObject(op.material_instance_id.Value);
                if (obj is Material mat)
                {
                    material = mat;
                    materialPath = AssetDatabase.GetAssetPath(material);
                }
                else
                {
                    return new MaterialResult
                    {
                        success = false,
                        instance_id = go.GetInstanceID(),
                        error = $"Object with instance_id {op.material_instance_id.Value} is not a Material"
                    };
                }
            }
            else
            {
                return new MaterialResult
                {
                    success = false,
                    instance_id = go.GetInstanceID(),
                    error = "Either 'material_path' or 'material_instance_id' is required"
                };
            }

            // マテリアルを設定
            int index = op.material_index ?? 0;
            var materials = renderer.sharedMaterials;

            // 範囲外の material_index は何も変更せずエラーを返す（スロット0への暗黙の書き込みはしない）
            if (op.material_index.HasValue && (index < 0 || index >= materials.Length))
            {
                return new MaterialResult
                {
                    success = false,
                    instance_id = go.GetInstanceID(),
                    error = $"material_index {index} out of range (0..{materials.Length - 1})"
                };
            }

            Undo.RecordObject(renderer, "Set Material");

            if (materials.Length == 0)
            {
                // スロットが存在しない場合（material_index 未指定時のみ到達）は先頭に設定
                renderer.sharedMaterial = material;
            }
            else
            {
                materials[index] = material;
                renderer.sharedMaterials = materials;
            }

            return new MaterialResult
            {
                success = true,
                instance_id = go.GetInstanceID(),
                material_path = materialPath
            };
        }
    }
}
