using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// 新しい GameObject を作成するツール（バッチ対応）
    /// </summary>
    [Tool("create-gameobject",
        Description = "Create a new empty GameObject or primitive in the scene",
        Title = "Create GameObject",
        Category = ToolCategory.GameObject,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = false)]
    [ToolOutput(typeof(Output))]
    public class CreateGameObjectHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Array of GameObjects to create", Required = true)]
            public CreateOperation[] objects;
        }

        /// <summary>作成操作</summary>
        public class CreateOperation
        {
            public string name;
            public string parent;
            public int? parent_id;
            public string primitive;

            [ToolParameter("Prefab asset path to instantiate (e.g., 'Assets/Prefabs/Player.prefab')")]
            public string prefab;

            public float[] position;
            public float[] rotation;
            public float[] scale;
            public ComponentSpec[] components;

            // ネスト構文・テンプレート展開用
            public CreateOperation[] children;
            public int? count;
        }

        /// <summary>コンポーネント指定</summary>
        public class ComponentSpec
        {
            public string type;

            [ToolParameter("Properties to set after adding. Vector2: [x,y], Vector3: [x,y,z], Color: [r,g,b,a] (0-1), Enum: index or name string")]
            public Dictionary<string, object> properties;
        }

        /// <summary>2D プリミティブの種類</summary>
        internal enum Primitive2DType
        {
            None,
            Sprite_Square,
            Sprite_Circle,
            Sprite_Triangle,
            Sprite_Hexagon,
        }

        /// <summary>個別結果</summary>
        public class CreateResult
        {
            public bool success;
            public string name;
            public int? instance_id;
            public string path;
            public string[] components_added;
            public string error;
        }

        public class CreatedObjectRef
        {
            public string name;
            public int instance_id;
            public string path;
            public int? parent_instance_id;
            public string parent_path;
            public int depth;
        }

        public class Output : BatchOutput<CreateResult>
        {
            public CreatedObjectRef[] created_roots;
            public CreatedObjectRef[] created_objects;
        }

        private const int MaxDepth = 10;
        private static readonly Regex TemplateRegex = new Regex(@"\$\{([^}]+)\}", RegexOptions.Compiled);

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var objects = args.GetObjectArray<CreateOperation>("objects");

            if (objects == null || objects.Length == 0)
            {
                return ToolResult.Fail("Parameter 'objects' is required and must be a non-empty array");
            }

            var builder = new BatchResultBuilder<CreateResult>();
            var createdRoots = new List<CreatedObjectRef>();
            var createdObjects = new List<CreatedObjectRef>();

            foreach (var op in objects)
            {
                ProcessOperationRecursive(op, null, builder, createdRoots, createdObjects, 0);
            }

            var batch = builder.Build("creation");
            return ToolResult.Ok(new Output
            {
                success = batch.success,
                summary = batch.summary,
                results = batch.results,
                message = batch.message,
                retry_payload = batch.retry_payload,
                created_roots = createdRoots.ToArray(),
                created_objects = createdObjects.ToArray()
            });
        }

        /// <summary>
        /// 再帰的にGameObjectを作成（ネスト構文・テンプレート展開対応）
        /// </summary>
        private void ProcessOperationRecursive(
            CreateOperation op,
            Transform parentOverride,
            BatchResultBuilder<CreateResult> builder,
            List<CreatedObjectRef> createdRoots,
            List<CreatedObjectRef> createdObjects,
            int depth)
        {
            if (depth > MaxDepth)
            {
                builder.Add(new CreateResult { success = false, error = $"Maximum nesting depth ({MaxDepth}) exceeded" }, false);
                return;
            }

            int count = op.count ?? 1;
            if (count < 1) count = 1;
            if (count > 100) count = 100; // 安全制限

            for (int i = 0; i < count; i++)
            {
                // テンプレート展開
                var expanded = ExpandTemplate(op, i);

                // GameObject作成
                var result = ProcessOperation(expanded, parentOverride);
                builder.Add(result, result.success);

                if (result.success && result.instance_id.HasValue)
                {
                    var createdRef = new CreatedObjectRef
                    {
                        name = result.name,
                        instance_id = result.instance_id.Value,
                        path = result.path,
                        parent_instance_id = parentOverride != null ? parentOverride.gameObject.GetInstanceID() : null,
                        parent_path = parentOverride != null ? GameObjectResolver.GetHierarchyPath(parentOverride.gameObject) : null,
                        depth = depth
                    };

                    createdObjects.Add(createdRef);
                    if (depth == 0)
                        createdRoots.Add(createdRef);
                }

                // 子オブジェクトの処理
                if (result.success && op.children != null && op.children.Length > 0)
                {
                    var createdGO = EditorUtility.InstanceIDToObject(result.instance_id.Value) as GameObject;
                    if (createdGO != null)
                    {
                        foreach (var child in op.children)
                        {
                            if (child == null) continue;
                            ProcessOperationRecursive(child, createdGO.transform, builder, createdRoots, createdObjects, depth + 1);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// テンプレート変数を展開
        /// </summary>
        private CreateOperation ExpandTemplate(CreateOperation op, int index)
        {
            return new CreateOperation
            {
                name = ExpandTemplateString(op.name, index),
                parent = op.parent,
                parent_id = op.parent_id,
                primitive = op.primitive,
                prefab = op.prefab,
                position = ExpandTemplateArray(op.position, index),
                rotation = ExpandTemplateArray(op.rotation, index),
                scale = ExpandTemplateArray(op.scale, index),
                components = op.components,
                children = op.children,
                count = null // 展開済みなのでcountは不要
            };
        }

        /// <summary>
        /// 文字列内のテンプレート変数を展開
        /// </summary>
        private string ExpandTemplateString(string template, int index)
        {
            if (string.IsNullOrEmpty(template)) return template;

            return TemplateRegex.Replace(template, match =>
            {
                var expr = match.Groups[1].Value.Trim();
                try
                {
                    return EvaluateSimpleExpression(expr, index).ToString(CultureInfo.InvariantCulture);
                }
                catch
                {
                    return match.Value; // 評価失敗時は元の文字列を保持
                }
            });
        }

        /// <summary>
        /// float配列内のテンプレート変数を展開
        /// </summary>
        private float[] ExpandTemplateArray(float[] array, int index)
        {
            // float配列は直接テンプレートを含まないが、将来的な拡張用
            return array;
        }

        /// <summary>
        /// 簡易数式評価（i, i+N, i*N, i-N をサポート）
        /// </summary>
        private float EvaluateSimpleExpression(string expr, int i)
        {
            // "i" を実際の値に置換
            expr = expr.Replace("i", i.ToString(CultureInfo.InvariantCulture));

            // 演算子で分割して評価
            if (expr.Contains("*"))
            {
                var parts = expr.Split('*');
                if (parts.Length == 2 &&
                    float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var left) &&
                    float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var right))
                {
                    return left * right;
                }
            }

            if (expr.Contains("+"))
            {
                var parts = expr.Split('+');
                if (parts.Length == 2 &&
                    float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var left) &&
                    float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var right))
                {
                    return left + right;
                }
            }

            // 減算は負数と区別するため最後に処理
            var lastMinus = expr.LastIndexOf('-');
            if (lastMinus > 0)
            {
                var leftStr = expr.Substring(0, lastMinus).Trim();
                var rightStr = expr.Substring(lastMinus + 1).Trim();
                if (float.TryParse(leftStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var left) &&
                    float.TryParse(rightStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var right))
                {
                    return left - right;
                }
            }

            // 単純な数値
            if (float.TryParse(expr.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            throw new FormatException($"Cannot evaluate expression: {expr}");
        }

        private CreateResult ProcessOperation(CreateOperation op, Transform parentOverride)
        {
            // 名前チェック
            if (string.IsNullOrEmpty(op.name))
            {
                return new CreateResult { success = false, error = "Parameter 'name' is required" };
            }

            // Transform パラメータのバリデーション
            if (!GameObjectResolver.TryParseOptionalVector3(op.position, "position", out var position, out var posError))
            {
                return new CreateResult { success = false, error = posError };
            }
            if (!GameObjectResolver.TryParseOptionalVector3(op.rotation, "rotation", out var rotation, out var rotError))
            {
                return new CreateResult { success = false, error = rotError };
            }
            if (!GameObjectResolver.TryParseOptionalVector3(op.scale, "scale", out var scale, out var scaleError))
            {
                return new CreateResult { success = false, error = scaleError };
            }

            // 親オブジェクトの解決（parentOverrideが優先）
            Transform parentTransform = parentOverride;
            if (parentTransform == null && (!string.IsNullOrEmpty(op.parent) || op.parent_id.HasValue))
            {
                var parentResult = GameObjectResolver.Resolve(op.parent, op.parent_id);
                if (!parentResult.Success)
                {
                    return new CreateResult { success = false, error = $"Parent not found: {parentResult.Error}" };
                }
                parentTransform = parentResult.GameObject.transform;
            }

            // GameObject 作成
            GameObject go;
            try
            {
                if (!string.IsNullOrEmpty(op.prefab))
                {
                    // Prefab インスタンス化
                    var prefabPath = op.prefab.Replace('\\', '/');
                    var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (prefabAsset == null)
                    {
                        return new CreateResult { success = false, error = $"Prefab not found at '{prefabPath}'" };
                    }

                    go = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset);
                    if (go == null)
                    {
                        return new CreateResult { success = false, error = $"Failed to instantiate prefab: {prefabPath}" };
                    }

                    if (!string.IsNullOrEmpty(op.name))
                    {
                        go.name = op.name;
                    }
                }
                else
                {
                    // Primitive タイプの解析
                    var primitiveStr = op.primitive ?? "None";
                    if (!string.IsNullOrEmpty(primitiveStr) && primitiveStr != "None")
                    {
                        // 2D プリミティブをチェック
                        if (Enum.TryParse<Primitive2DType>(primitiveStr, true, out var prim2D) && prim2D != Primitive2DType.None)
                        {
                            go = Create2DPrimitive(op.name, prim2D);
                        }
                        else if (Enum.TryParse<PrimitiveType>(primitiveStr, true, out var primitiveType))
                        {
                            go = GameObject.CreatePrimitive(primitiveType);
                            go.name = op.name;
                        }
                        else
                        {
                            return new CreateResult
                            {
                                success = false,
                                error = $"Invalid primitive type: {primitiveStr}. " +
                                    "Valid 3D types: Cube, Sphere, Capsule, Cylinder, Plane, Quad. " +
                                    "Valid 2D types: Sprite_Square, Sprite_Circle, Sprite_Triangle, Sprite_Hexagon"
                            };
                        }
                    }
                    else
                    {
                        go = new GameObject(op.name);
                    }
                }
            }
            catch (Exception ex)
            {
                return new CreateResult { success = false, error = $"Failed to create GameObject: {ex.Message}" };
            }

            // Undo 登録
            Undo.RegisterCreatedObjectUndo(go, $"Create {op.name}");

            // 親設定
            if (parentTransform != null)
            {
                Undo.SetTransformParent(go.transform, parentTransform, $"Set parent for {op.name}");
            }

            // Transform 設定
            var t = go.transform;
            if (position.HasValue) t.localPosition = position.Value;
            if (rotation.HasValue) t.localEulerAngles = rotation.Value;
            if (scale.HasValue) t.localScale = scale.Value;

            // コンポーネント追加
            var componentsAdded = new List<string>();
            if (op.components != null && op.components.Length > 0)
            {
                foreach (var compSpec in op.components)
                {
                    if (compSpec == null || string.IsNullOrEmpty(compSpec.type))
                    {
                        continue;
                    }

                    // 型解決（AddComponentHandler の静的キャッシュを使用）
                    var componentType = AddComponentHandler.ResolveComponentType(compSpec.type);
                    if (componentType == null)
                    {
                        // 型が見つからない場合はスキップ（エラーにはしない）
                        continue;
                    }

                    try
                    {
                        var component = Undo.AddComponent(go, componentType);
                        if (component != null)
                        {
                            componentsAdded.Add(componentType.Name);

                            // 2D コライダーのデフォルトサイズ正規化
                            AddComponentHandler.NormalizeCollider2DSize(component);

                            // プロパティ設定
                            if (compSpec.properties != null && compSpec.properties.Count > 0)
                            {
                                var propResult = ComponentPropertySetter.SetProperties(component, compSpec.properties);
                                if (propResult.errors.Count > 0)
                                {
                                    foreach (var propError in propResult.errors)
                                    {
                                        Debug.LogWarning($"[CreateGameObject] {componentType.Name}: {propError}");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // コンポーネント追加失敗はスキップ
                    }
                }
            }

            return new CreateResult
            {
                success = true,
                name = go.name,
                instance_id = go.GetInstanceID(),
                path = GameObjectResolver.GetHierarchyPath(go),
                components_added = componentsAdded.Count > 0 ? componentsAdded.ToArray() : null
            };
        }

        /// <summary>
        /// 2D プリミティブを作成する。
        /// SpriteRenderer + 白スプライト + 適切な 2D Collider を自動付与。
        /// </summary>
        private static GameObject Create2DPrimitive(string name, Primitive2DType type)
        {
            var go = new GameObject(name);
            var sr = go.AddComponent<SpriteRenderer>();
            var sprite = SpritePrimitiveCache.GetSprite(type);
            if (sprite == null)
            {
                // スプライトなしで success を返さない（呼び出し側の per-item catch でエラー化される）
                UnityEngine.Object.DestroyImmediate(go);
                throw new InvalidOperationException(
                    $"Sprite asset could not be created or loaded at '{SpritePrimitiveCache.GetAssetPath(type)}'");
            }
            sr.sprite = sprite;

            switch (type)
            {
                case Primitive2DType.Sprite_Square:
                    var box = go.AddComponent<BoxCollider2D>();
                    box.size = Vector2.one;
                    break;
                case Primitive2DType.Sprite_Circle:
                    var circle = go.AddComponent<CircleCollider2D>();
                    circle.radius = 0.5f;
                    break;
                case Primitive2DType.Sprite_Triangle:
                    var tri = go.AddComponent<PolygonCollider2D>();
                    tri.points = new[]
                    {
                        new Vector2(0f, 0.5f),
                        new Vector2(-0.5f, -0.5f),
                        new Vector2(0.5f, -0.5f)
                    };
                    break;
                case Primitive2DType.Sprite_Hexagon:
                    var hex = go.AddComponent<PolygonCollider2D>();
                    hex.points = GenerateRegularPolygonPoints(6, 0.5f);
                    break;
            }

            return go;
        }

        /// <summary>正多角形の頂点を生成</summary>
        private static Vector2[] GenerateRegularPolygonPoints(int sides, float radius)
        {
            var points = new Vector2[sides];
            float angleStep = 360f / sides;
            for (int i = 0; i < sides; i++)
            {
                float angle = (90f + i * angleStep) * Mathf.Deg2Rad;
                points[i] = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
            }
            return points;
        }

        /// <summary>
        /// 2D プリミティブ用のスプライトキャッシュ。
        /// Sprite.Create によるメモリ上のスプライトはシーン保存/再読み込みで参照が失われるため、
        /// PNG アセットとして Assets/UniForge/Generated に永続化し、そこからロードする。
        /// 静的フィールドは同一ドメイン内でのルックアップ高速化のためのキャッシュにすぎず、
        /// 真実のソースはアセットファイル。
        /// </summary>
        internal static class SpritePrimitiveCache
        {
            /// <summary>生成アセットの保存先フォルダ</summary>
            internal const string GeneratedFolder = "Assets/UniForge/Generated";

            internal const string SquareSpriteAssetPath = GeneratedFolder + "/UniForgeSquareSprite.png";
            internal const string CircleSpriteAssetPath = GeneratedFolder + "/UniForgeCircleSprite.png";

            private static Sprite _squareSprite;
            private static Sprite _circleSprite;

            public static Sprite GetSprite(Primitive2DType type)
            {
                switch (type)
                {
                    case Primitive2DType.Sprite_Circle:
                        return GetCircleSprite();
                    default:
                        return GetSquareSprite();
                }
            }

            public static string GetAssetPath(Primitive2DType type)
            {
                return type == Primitive2DType.Sprite_Circle ? CircleSpriteAssetPath : SquareSpriteAssetPath;
            }

            private static Sprite GetSquareSprite()
            {
                if (_squareSprite != null) return _squareSprite;

                _squareSprite = LoadOrCreateSpriteAsset(
                    SquareSpriteAssetPath,
                    GenerateSquareTexture,
                    pixelsPerUnit: 4f,
                    filterMode: FilterMode.Point);
                return _squareSprite;
            }

            private static Sprite GetCircleSprite()
            {
                if (_circleSprite != null) return _circleSprite;

                _circleSprite = LoadOrCreateSpriteAsset(
                    CircleSpriteAssetPath,
                    GenerateCircleTexture,
                    pixelsPerUnit: 64f,
                    filterMode: FilterMode.Bilinear);
                return _circleSprite;
            }

            /// <summary>
            /// スプライトアセットをロードし、存在しなければ PNG として生成してインポートする。
            /// </summary>
            private static Sprite LoadOrCreateSpriteAsset(
                string assetPath, Func<Texture2D> generateTexture, float pixelsPerUnit, FilterMode filterMode)
            {
                // 既存アセットを再利用（ファイル名は固定なので冪等）
                var existing = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (existing != null) return existing;

                // カレントディレクトリに依存しないよう、プロジェクトルート基準の絶対パスで扱う
                var projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
                var fullPath = System.IO.Path.Combine(projectRoot, assetPath);

                // ファイルは存在するが Sprite としてロードできない場合:
                // 未インポートの可能性があるため一度インポートを試み、それでも Sprite でなければ
                // ユーザー所有のアセットとみなして一切変更せず失敗させる（データ・設定の破壊防止）
                if (System.IO.File.Exists(fullPath))
                {
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                    var imported = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                    if (imported == null)
                    {
                        Debug.LogWarning(
                            $"[UniForge] '{assetPath}' に Sprite としてロードできない既存アセットがあるため、" +
                            "2D プリミティブ用スプライトを生成できません。ファイルを移動または削除してください。");
                    }
                    return imported;
                }

                // 初回利用時に生成先フォルダを作成
                AssetHelper.CreateFolderRecursive(GeneratedFolder);

                // テクスチャを PNG として書き出し（アセット化しないと参照がシーン保存で失われる）
                var tex = generateTexture();
                var png = tex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tex);

                System.IO.File.WriteAllBytes(fullPath, png);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

                ConfigureSpriteImporter(assetPath, pixelsPerUnit, filterMode);

                return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            }

            /// <summary>Sprite としてインポートされるようインポート設定を適用する</summary>
            private static void ConfigureSpriteImporter(string assetPath, float pixelsPerUnit, FilterMode filterMode)
            {
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null) return;

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = pixelsPerUnit;
                importer.filterMode = filterMode;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            private static Texture2D GenerateSquareTexture()
            {
                var tex = new Texture2D(4, 4);
                var pixels = new Color[16];
                for (int i = 0; i < 16; i++) pixels[i] = Color.white;
                tex.SetPixels(pixels);
                tex.Apply();
                return tex;
            }

            private static Texture2D GenerateCircleTexture()
            {
                int size = 64;
                var tex = new Texture2D(size, size);
                float center = size / 2f;
                float radius = center - 1f;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                        tex.SetPixel(x, y, dist <= radius ? Color.white : Color.clear);
                    }
                }

                tex.Apply();
                return tex;
            }
        }
    }
}
