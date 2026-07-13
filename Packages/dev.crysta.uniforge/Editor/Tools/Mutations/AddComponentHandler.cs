using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// GameObject にコンポーネントを追加するツール（バッチ対応）
    /// </summary>
    [Tool("add-component",
        Description = "Add a component to a GameObject",
        Title = "Add Component",
        Category = ToolCategory.GameObject,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = false)]
    public class AddComponentHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Array of add operations", Required = true)]
            public AddOperation[] operations;
        }

        /// <summary>追加操作</summary>
        public class AddOperation
        {
            public string path;
            public int? instance_id;
            public string scene;
            public string component_type;

            [ToolParameter("Properties to set after adding. Vector2: [x,y], Vector3: [x,y,z], Color: [r,g,b,a] (0-1), Enum: index or name string, ObjectReference: asset path string or instance_id int")]
            public Dictionary<string, object> properties;
        }

        /// <summary>個別結果</summary>
        public class AddResult
        {
            public bool success;
            public int? instance_id;
            public string component_type;
            public int dependencies_added;
            public Dictionary<string, object> set_properties;
            public string[] property_errors;
            public string error;
        }

        /// <summary>
        /// Static Type Cache - 全 Component 型を名前でキャッシュ
        /// 初回アクセス時に一度だけ構築される
        /// よく使われる Unity 型は typeof() で直接登録し、確実に解決できるようにする
        /// </summary>
        private static class ComponentTypeCache
        {
            // 短縮名 → Type のマッピング (case-insensitive)
            private static Dictionary<string, Type> _shortNameCache;
            // フルネーム → Type のマッピング
            private static Dictionary<string, Type> _fullNameCache;

            public static Type Resolve(string typeName)
            {
                EnsureCacheBuilt();

                // 1. 短縮名で検索 (case-insensitive)
                if (_shortNameCache.TryGetValue(typeName, out var type))
                {
                    return type;
                }

                // 2. フルネームで検索
                if (_fullNameCache.TryGetValue(typeName, out type))
                {
                    return type;
                }

                return null;
            }

            private static void EnsureCacheBuilt()
            {
                if (_shortNameCache != null) return;

                _shortNameCache = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
                _fullNameCache = new Dictionary<string, Type>();

                // よく使われる Unity 型を typeof() で直接登録（確実に解決できる）
                RegisterCommonTypes();

                // 残りの型をアセンブリスキャンで登録
                ScanAssemblies();
            }

            /// <summary>
            /// よく使われる Unity コンポーネント型を直接登録
            /// typeof() を使用することで確実に型が解決される
            /// </summary>
            private static void RegisterCommonTypes()
            {
                // Core
                Register(typeof(Transform));
                Register(typeof(RectTransform));
                Register(typeof(Camera));
                Register(typeof(Light));

                // Physics
                Register(typeof(Rigidbody));
                Register(typeof(Rigidbody2D));
                Register(typeof(BoxCollider));
                Register(typeof(SphereCollider));
                Register(typeof(CapsuleCollider));
                Register(typeof(MeshCollider));
                Register(typeof(CharacterController));
                Register(typeof(BoxCollider2D));
                Register(typeof(CircleCollider2D));
                Register(typeof(PolygonCollider2D));

                // Rendering
                Register(typeof(MeshRenderer));
                Register(typeof(MeshFilter));
                Register(typeof(SkinnedMeshRenderer));
                Register(typeof(SpriteRenderer));
                Register(typeof(LineRenderer));
                Register(typeof(TrailRenderer));
                Register(typeof(ParticleSystem));
                Register(typeof(ParticleSystemRenderer));

                // Audio
                Register(typeof(AudioSource));
                Register(typeof(AudioListener));

                // Animation
                Register(typeof(Animator));
                Register(typeof(Animation));

                // UI - Legacy (フルネームでのみアクセス可能)
                Register(typeof(Canvas));
                Register(typeof(CanvasRenderer));
                Register(typeof(UnityEngine.UI.Image));
                Register(typeof(UnityEngine.UI.Text));
                Register(typeof(UnityEngine.UI.Button));
                Register(typeof(UnityEngine.UI.Toggle));
                Register(typeof(UnityEngine.UI.Slider));
                Register(typeof(UnityEngine.UI.Scrollbar));
                Register(typeof(UnityEngine.UI.Dropdown));
                Register(typeof(UnityEngine.UI.InputField));
                Register(typeof(UnityEngine.UI.ScrollRect));
                Register(typeof(UnityEngine.UI.Mask));
                Register(typeof(UnityEngine.UI.RawImage));
                Register(typeof(UnityEngine.UI.CanvasScaler));
                Register(typeof(UnityEngine.UI.GraphicRaycaster));
                Register(typeof(UnityEngine.UI.LayoutElement));
                Register(typeof(UnityEngine.UI.ContentSizeFitter));
                Register(typeof(UnityEngine.UI.AspectRatioFitter));
                Register(typeof(UnityEngine.UI.HorizontalLayoutGroup));
                Register(typeof(UnityEngine.UI.VerticalLayoutGroup));
                Register(typeof(UnityEngine.UI.GridLayoutGroup));

                // EventSystem
                Register(typeof(UnityEngine.EventSystems.EventSystem));
                Register(typeof(UnityEngine.EventSystems.StandaloneInputModule));

                // TextMeshPro - 存在する場合は優先登録（短縮名 "Text" を上書き）
                RegisterTextMeshProTypes();
            }

            /// <summary>
            /// TextMeshPro コンポーネントを登録（パッケージが存在する場合のみ）
            /// リフレクションで動的に取得するため、TextMeshPro未導入でもコンパイル可能
            /// </summary>
            private static void RegisterTextMeshProTypes()
            {
                // TextMeshProUGUI (Canvas用テキスト) - "Text" として優先登録
                var tmpTextType = Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
                if (tmpTextType != null)
                {
                    _shortNameCache["Text"] = tmpTextType;  // "Text" → TextMeshProUGUI を優先
                    _shortNameCache["TextMeshProUGUI"] = tmpTextType;
                    _fullNameCache["TMPro.TextMeshProUGUI"] = tmpTextType;
                }

                // TextMeshPro (3Dワールド空間用テキスト)
                var tmpText3DType = Type.GetType("TMPro.TextMeshPro, Unity.TextMeshPro");
                if (tmpText3DType != null)
                {
                    _shortNameCache["TextMeshPro"] = tmpText3DType;
                    _fullNameCache["TMPro.TextMeshPro"] = tmpText3DType;
                }

                // TMP_InputField
                var tmpInputType = Type.GetType("TMPro.TMP_InputField, Unity.TextMeshPro");
                if (tmpInputType != null)
                {
                    _shortNameCache["TMP_InputField"] = tmpInputType;
                    _fullNameCache["TMPro.TMP_InputField"] = tmpInputType;
                }

                // TMP_Dropdown
                var tmpDropdownType = Type.GetType("TMPro.TMP_Dropdown, Unity.TextMeshPro");
                if (tmpDropdownType != null)
                {
                    _shortNameCache["TMP_Dropdown"] = tmpDropdownType;
                    _fullNameCache["TMPro.TMP_Dropdown"] = tmpDropdownType;
                }
            }

            private static void Register(Type type)
            {
                if (type == null) return;
                _shortNameCache[type.Name] = type;
                _fullNameCache[type.FullName] = type;
            }

            /// <summary>
            /// アセンブリをスキャンして残りの Component 型を登録
            /// </summary>
            private static void ScanAssemblies()
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    // システムアセンブリをスキップ
                    var assemblyName = assembly.GetName().Name;
                    if (assemblyName.StartsWith("System") ||
                        assemblyName.StartsWith("mscorlib") ||
                        assemblyName.StartsWith("netstandard"))
                    {
                        continue;
                    }

                    try
                    {
                        foreach (var t in assembly.GetTypes())
                        {
                            if (!typeof(Component).IsAssignableFrom(t) || t.IsAbstract)
                                continue;

                            // フルネームは常に登録（上書き不可）
                            if (!_fullNameCache.ContainsKey(t.FullName))
                            {
                                _fullNameCache[t.FullName] = t;
                            }

                            // 短縮名は衝突がなければ登録（既に登録済みなら上書きしない）
                            if (!_shortNameCache.ContainsKey(t.Name))
                            {
                                _shortNameCache[t.Name] = t;
                            }
                        }
                    }
                    catch (ReflectionTypeLoadException)
                    {
                        // 一部のアセンブリは型をロードできない場合がある
                    }
                }
            }
        }

        /// <summary>
        /// コンポーネント型名から Type を解決（外部からも利用可能）
        /// </summary>
        /// <param name="typeName">型名（短縮名またはフルネーム）</param>
        /// <returns>解決された Type、見つからない場合は null</returns>
        public static Type ResolveComponentType(string typeName)
        {
            return ComponentTypeCache.Resolve(typeName);
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var operations = args.GetObjectArray<AddOperation>("operations");

            if (operations == null || operations.Length == 0)
            {
                return ToolResult.Fail("Parameter 'operations' is required and must be a non-empty array");
            }

            var builder = new BatchResultBuilder<AddResult>();

            // 依存チェーン: 同一オブジェクトで失敗した場合、後続をスキップ
            var failedObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var failedOperations = new List<AddOperation>();
            int skippedCount = 0;

            foreach (var op in operations)
            {
                var objectKey = ResolveObjectKey(op);

                if (objectKey != null && failedObjects.Contains(objectKey))
                {
                    // 前の操作が同一オブジェクトで失敗 → スキップ
                    skippedCount++;
                    builder.Add(new AddResult
                    {
                        success = false,
                        error = "Skipped: previous operation on same object failed"
                    }, false);
                    failedOperations.Add(op);
                    continue;
                }

                var result = ProcessOperation(op);
                builder.Add(result, result.success);

                if (!result.success)
                {
                    if (objectKey != null)
                    {
                        failedObjects.Add(objectKey);
                    }
                    failedOperations.Add(op);
                }
            }

            var output = builder.Build("add-component");

            // リトライペイロード: 失敗した操作を再実行用に返す
            if (failedOperations.Count > 0)
            {
                output.retry_payload = new { operations = failedOperations.ToArray() };
            }

            return ToolResult.Ok(output);
        }

        /// <summary>
        /// 操作のオブジェクトキーを解決（依存チェーン用）
        /// path または instance_id でオブジェクトを識別する
        /// </summary>
        private static string ResolveObjectKey(AddOperation op)
        {
            if (op.instance_id.HasValue) return $"id:{op.instance_id.Value}";
            if (!string.IsNullOrEmpty(op.path)) return $"path:{op.path}";
            return null;
        }

        /// <summary>
        /// コンポーネント追加失敗の原因を診断して詳細なエラーメッセージを返す
        /// </summary>
        private static string DiagnoseAddComponentFailure(GameObject go, Type componentType, string requestedName)
        {
            var reasons = new System.Collections.Generic.List<string>();

            // 2D/3D コンフリクト検出
            bool is2DComponent = typeof(Collider2D).IsAssignableFrom(componentType) ||
                                 componentType == typeof(Rigidbody2D);
            bool is3DComponent = typeof(Collider).IsAssignableFrom(componentType) ||
                                 componentType == typeof(Rigidbody);

            if (is2DComponent)
            {
                // 3D コンポーネントが存在するか
                if (go.GetComponent<Collider>() != null)
                    reasons.Add("a 3D Collider exists on this GameObject. Remove it first or use an empty GameObject");
                if (go.GetComponent<Rigidbody>() != null)
                    reasons.Add("a 3D Rigidbody exists on this GameObject");
            }
            else if (is3DComponent)
            {
                if (go.GetComponent<Collider2D>() != null)
                    reasons.Add("a 2D Collider exists on this GameObject");
                if (go.GetComponent<Rigidbody2D>() != null)
                    reasons.Add("a 2D Rigidbody exists on this GameObject");
            }

            // 重複チェック（DisallowMultipleComponent）
            if (go.GetComponent(componentType) != null)
            {
                reasons.Add($"{componentType.Name} already exists on this GameObject (multiple instances not allowed)");
            }

            // RequireComponent の依存関係チェック
            var requireAttrs = componentType.GetCustomAttributes(typeof(RequireComponent), true);
            foreach (RequireComponent req in requireAttrs)
            {
                if (req.m_Type0 != null && go.GetComponent(req.m_Type0) == null)
                    reasons.Add($"requires {req.m_Type0.Name} component");
                if (req.m_Type1 != null && go.GetComponent(req.m_Type1) == null)
                    reasons.Add($"requires {req.m_Type1.Name} component");
                if (req.m_Type2 != null && go.GetComponent(req.m_Type2) == null)
                    reasons.Add($"requires {req.m_Type2.Name} component");
            }

            if (reasons.Count > 0)
            {
                return $"Cannot add {requestedName}: {string.Join("; ", reasons)}";
            }

            return $"Failed to add component: {requestedName}. The component may be incompatible with this GameObject.";
        }

        /// <summary>
        /// 2D コライダーのデフォルトサイズを正規化する。
        /// SpriteRenderer にスプライトが未設定の場合、Unity はコライダーサイズをほぼゼロに設定するため、
        /// 妥当なデフォルト値に補正する。
        /// </summary>
        public static void NormalizeCollider2DSize(Component component)
        {
            const float threshold = 0.01f;

            if (component is BoxCollider2D box)
            {
                if (box.size.x < threshold && box.size.y < threshold)
                {
                    box.size = Vector2.one;
                }
            }
            else if (component is CircleCollider2D circle)
            {
                if (circle.radius < threshold)
                {
                    circle.radius = 0.5f;
                }
            }
            else if (component is CapsuleCollider2D capsule)
            {
                if (capsule.size.x < threshold && capsule.size.y < threshold)
                {
                    capsule.size = new Vector2(1f, 2f);
                }
            }
            else if (component is PolygonCollider2D polygon)
            {
                if (polygon.points.Length == 0)
                {
                    // デフォルトの正方形パスを設定
                    polygon.points = new[]
                    {
                        new Vector2(-0.5f, -0.5f),
                        new Vector2( 0.5f, -0.5f),
                        new Vector2( 0.5f,  0.5f),
                        new Vector2(-0.5f,  0.5f)
                    };
                }
            }
        }

        private AddResult ProcessOperation(AddOperation op)
        {
            // コンポーネント型チェック
            if (string.IsNullOrEmpty(op.component_type))
            {
                return new AddResult { success = false, error = "Parameter 'component_type' is required" };
            }

            // 型を解決
            var componentType = ResolveComponentType(op.component_type);
            if (componentType == null)
            {
                return new AddResult
                {
                    success = false,
                    error = $"Component type not found: {op.component_type}. " +
                        "Use full type name (e.g., 'UnityEngine.Rigidbody') or common names like 'Rigidbody', 'BoxCollider', 'AudioSource'."
                };
            }

            // GameObject 解決
            var resolveResult = GameObjectResolver.Resolve(op.path, op.instance_id, op.scene);
            if (!resolveResult.Success)
            {
                return new AddResult { success = false, error = resolveResult.Error };
            }

            var go = resolveResult.GameObject;

            try
            {
                var beforeCount = go.GetComponents<Component>().Length;
                var component = Undo.AddComponent(go, componentType);
                if (component == null)
                {
                    return new AddResult
                    {
                        success = false,
                        instance_id = go.GetInstanceID(),
                        error = DiagnoseAddComponentFailure(go, componentType, op.component_type)
                    };
                }

                var afterCount = go.GetComponents<Component>().Length;
                var dependenciesAdded = afterCount - beforeCount - 1;

                // 2D コライダーのデフォルトサイズ正規化
                // SpriteRenderer にスプライト未設定の場合、Unity はコライダーサイズをほぼゼロに設定する
                NormalizeCollider2DSize(component);

                // プロパティ設定
                Dictionary<string, object> setProperties = null;
                string[] propertyErrors = null;

                if (op.properties != null && op.properties.Count > 0)
                {
                    var propResult = ComponentPropertySetter.SetProperties(component, op.properties);
                    if (propResult.set_properties.Count > 0)
                    {
                        setProperties = propResult.set_properties;
                    }
                    if (propResult.errors.Count > 0)
                    {
                        propertyErrors = propResult.errors.ToArray();
                    }
                }

                return new AddResult
                {
                    success = true,
                    instance_id = go.GetInstanceID(),
                    component_type = componentType.Name,
                    dependencies_added = dependenciesAdded,
                    set_properties = setProperties,
                    property_errors = propertyErrors
                };
            }
            catch (Exception ex)
            {
                return new AddResult
                {
                    success = false,
                    instance_id = go.GetInstanceID(),
                    error = $"Failed to add component: {ex.Message}"
                };
            }
        }
    }
}
