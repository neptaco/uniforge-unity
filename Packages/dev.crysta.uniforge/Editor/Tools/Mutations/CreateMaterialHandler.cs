using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// マテリアルを作成するツール
    /// </summary>
    [Tool("create-material",
        Description = "Create a new material asset",
        Title = "Create Material",
        Category = ToolCategory.Material,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = false)]
    public class CreateMaterialHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Material save path (e.g., 'Assets/Materials/Red.mat')", Required = true)]
            public string path;

            [ToolParameter("Shader name (default: 'Standard')", Required = false, Default = "Standard")]
            public string shader;

            [ToolParameter("Main color as [r, g, b] or [r, g, b, a] (0-1 range). Overridden by _Color in properties if both specified.", Required = false)]
            public List<object> color;

            [ToolParameter("Overwrite existing material if it exists", Required = false, Default = false)]
            public bool overwrite;

            [ToolParameter("Material properties to set. Key: property name (_MainTex, _Metallic, etc). Value: float, [r,g,b,a] for color, [x,y,z,w] for vector, or asset path string for texture")]
            public Dictionary<string, object> properties;
        }

        /// <summary>出力定義</summary>
        public class Output
        {
            public bool success;
            public string path;
            public int? instance_id;
            public bool overwritten;
            public string message;
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var path = args.GetString("path");
            var shaderName = args.GetString("shader", "Standard");
            var colorArray = args.GetFloatArray("color");
            var overwrite = args.GetBool("overwrite", false);
            var properties = args.GetDictionary("properties");

            if (string.IsNullOrEmpty(path))
            {
                return ToolResult.Fail("Parameter 'path' is required");
            }

            // パスの正規化
            path = path.Replace('\\', '/');
            if (!path.EndsWith(".mat"))
            {
                path += ".mat";
            }
            if (!path.StartsWith("Assets/"))
            {
                path = "Assets/" + path;
            }

            // 既存のマテリアルを確認
            bool exists = AssetDatabase.LoadAssetAtPath<Material>(path) != null;
            if (exists && !overwrite)
            {
                return ToolResult.Fail($"Material already exists at {path}. Set overwrite=true to replace.");
            }

            // シェーダーを検索
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                return ToolResult.Fail($"Shader not found: {shaderName}");
            }

            // 親フォルダを作成
            var folderPath = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            AssetHelper.CreateFolderRecursive(folderPath);

            // マテリアルを作成
            var material = new Material(shader);

            // 色を設定
            if (colorArray != null && colorArray.Length >= 3)
            {
                float r = colorArray[0];
                float g = colorArray[1];
                float b = colorArray[2];
                float a = colorArray.Length >= 4 ? colorArray[3] : 1f;
                material.color = new Color(r, g, b, a);
            }

            // プロパティ設定
            if (properties != null)
            {
                ApplyProperties(material, properties);
            }

            // アセットとして保存
            if (exists)
            {
                var existingMat = AssetDatabase.LoadAssetAtPath<Material>(path);
                EditorUtility.CopySerialized(material, existingMat);
                AssetDatabase.SaveAssets();
                UnityEngine.Object.DestroyImmediate(material);
                material = existingMat;
            }
            else
            {
                AssetDatabase.CreateAsset(material, path);
            }

            AssetDatabase.Refresh();

            return ToolResult.Ok(new Output
            {
                success = true,
                path = path,
                instance_id = material.GetInstanceID(),
                overwritten = exists,
                message = exists ? $"Material overwritten: {path}" : $"Material created: {path}"
            });
        }

        private static readonly Dictionary<string, string> ShaderKeywordMap = new()
        {
            { "_EmissionColor", "_EMISSION" },
            { "_BumpMap", "_NORMALMAP" },
            { "_MetallicGlossMap", "_METALLICGLOSSMAP" },
            { "_ParallaxMap", "_PARALLAXMAP" },
            { "_DetailAlbedoMap", "_DETAIL_MULX2" },
        };

        private static void ApplyProperties(Material material, Dictionary<string, object> properties)
        {
            foreach (var kvp in properties)
            {
                var propName = kvp.Key;
                var value = kvp.Value;

                var propIndex = material.shader.FindPropertyIndex(propName);
                if (propIndex < 0)
                {
                    Debug.LogWarning($"[CreateMaterial] Property '{propName}' not found on shader '{material.shader.name}'");
                    continue;
                }

                var propType = material.shader.GetPropertyType(propIndex);

                try
                {
                    switch (propType)
                    {
                        case UnityEngine.Rendering.ShaderPropertyType.Float:
                        case UnityEngine.Rendering.ShaderPropertyType.Range:
                            material.SetFloat(propName, Convert.ToSingle(value));
                            break;

                        case UnityEngine.Rendering.ShaderPropertyType.Int:
                            material.SetInteger(propName, Convert.ToInt32(value));
                            break;

                        case UnityEngine.Rendering.ShaderPropertyType.Color:
                            if (ComponentPropertySetter.TryParseColor(value, out var color))
                                material.SetColor(propName, color);
                            else
                                Debug.LogWarning($"[CreateMaterial] Invalid color value for '{propName}'");
                            break;

                        case UnityEngine.Rendering.ShaderPropertyType.Vector:
                            if (ComponentPropertySetter.TryParseVector4(value, out var vec))
                                material.SetVector(propName, vec);
                            else
                                Debug.LogWarning($"[CreateMaterial] Invalid vector value for '{propName}'");
                            break;

                        case UnityEngine.Rendering.ShaderPropertyType.Texture:
                            if (value is string texPath)
                            {
                                var tex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
                                if (tex != null)
                                    material.SetTexture(propName, tex);
                                else
                                    Debug.LogWarning($"[CreateMaterial] Texture not found: {texPath}");
                            }
                            else
                            {
                                Debug.LogWarning($"[CreateMaterial] Invalid texture value for '{propName}': expected asset path string");
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CreateMaterial] Failed to set '{propName}': {ex.Message}");
                    continue;
                }

                // シェーダーキーワードの自動有効化
                if (ShaderKeywordMap.TryGetValue(propName, out var keyword))
                {
                    material.EnableKeyword(keyword);
                }
            }
        }

    }
}
