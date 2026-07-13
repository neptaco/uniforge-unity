using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UniForge.Tools
{
    /// <summary>
    /// プロパティ取得の結果
    /// </summary>
    public class PropertyGetResult
    {
        /// <summary>取得したプロパティ</summary>
        public Dictionary<string, object> properties = new Dictionary<string, object>();

        /// <summary>エラーリスト</summary>
        public List<string> errors = new List<string>();

        /// <summary>全て成功したか</summary>
        public bool AllSucceeded => errors.Count == 0;

        /// <summary>プロパティが1つでも取得できたか</summary>
        public bool HasAnyProperty => properties.Count > 0;
    }

    /// <summary>
    /// コンポーネントのプロパティを SerializedObject 経由で取得するヘルパー
    /// </summary>
    public static class ComponentPropertyGetter
    {
        /// <summary>
        /// コンポーネントのプロパティを取得
        /// </summary>
        /// <param name="component">対象コンポーネント</param>
        /// <param name="propertyNames">取得するプロパティ名のリスト（null または空の場合は全プロパティ）</param>
        /// <returns>取得結果</returns>
        public static PropertyGetResult GetProperties(Component component, string[] propertyNames = null)
        {
            var result = new PropertyGetResult();

            if (component == null)
            {
                result.errors.Add("Component is null");
                return result;
            }

            using (var so = new SerializedObject(component))
            {
                if (propertyNames == null || propertyNames.Length == 0)
                {
                    // 全プロパティを取得
                    var iterator = so.GetIterator();
                    if (iterator.NextVisible(true))
                    {
                        do
                        {
                            // スクリプト参照はスキップ
                            if (iterator.name == "m_Script") continue;

                            // null（未割り当ての ObjectReference 等）もキーとして含める
                            // （「未割り当て」と「存在しないプロパティ」を区別できるようにする。名前指定モードと同じ挙動）
                            result.properties[iterator.name] = GetPropertyValue(iterator);
                        }
                        while (iterator.NextVisible(false));
                    }
                }
                else
                {
                    // 指定されたプロパティのみ取得
                    foreach (var propName in propertyNames)
                    {
                        if (string.IsNullOrEmpty(propName))
                        {
                            result.errors.Add("Property name is empty");
                            continue;
                        }

                        var prop = FindProperty(so, propName);
                        if (prop == null)
                        {
                            result.errors.Add($"Property not found: {propName}");
                            continue;
                        }

                        var value = GetPropertyValue(prop);
                        result.properties[propName] = value;
                    }
                }
            }

            return result;
        }

        private static SerializedProperty FindProperty(SerializedObject so, string propName)
            => SerializedPropertyFinder.FindPropertyWithAliases(so, propName);

        /// <summary>
        /// SerializedProperty から値を取得
        /// </summary>
        private static object GetPropertyValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return prop.intValue;

                case SerializedPropertyType.Float:
                    return prop.floatValue;

                case SerializedPropertyType.Boolean:
                    return prop.boolValue;

                case SerializedPropertyType.String:
                    return prop.stringValue;

                case SerializedPropertyType.Vector2:
                    return new[] { prop.vector2Value.x, prop.vector2Value.y };

                case SerializedPropertyType.Vector3:
                    return new[] { prop.vector3Value.x, prop.vector3Value.y, prop.vector3Value.z };

                case SerializedPropertyType.Vector4:
                    return new[] { prop.vector4Value.x, prop.vector4Value.y, prop.vector4Value.z, prop.vector4Value.w };

                case SerializedPropertyType.Color:
                    return new[] { prop.colorValue.r, prop.colorValue.g, prop.colorValue.b, prop.colorValue.a };

                case SerializedPropertyType.Enum:
                {
                    var enumIndex = prop.enumValueIndex;
                    var enumNames = prop.enumNames;
                    // シリアライズされた int がどの enum メンバーとも一致しない場合、
                    // Unity は enumValueIndex を -1 にする。範囲外の場合は生の int 値をそのまま返す
                    if (enumIndex < 0 || enumIndex >= enumNames.Length)
                    {
                        return prop.intValue;
                    }
                    return new Dictionary<string, object>
                    {
                        { "index", enumIndex },
                        { "name", enumNames[enumIndex] }
                    };
                }

                case SerializedPropertyType.LayerMask:
                    return prop.intValue;

                case SerializedPropertyType.Quaternion:
                    var q = prop.quaternionValue;
                    var euler = q.eulerAngles;
                    return new Dictionary<string, object>
                    {
                        { "quaternion", new[] { q.x, q.y, q.z, q.w } },
                        { "euler", new[] { euler.x, euler.y, euler.z } }
                    };

                case SerializedPropertyType.Rect:
                    var rect = prop.rectValue;
                    return new[] { rect.x, rect.y, rect.width, rect.height };

                case SerializedPropertyType.Bounds:
                    var bounds = prop.boundsValue;
                    return new Dictionary<string, object>
                    {
                        { "center", new[] { bounds.center.x, bounds.center.y, bounds.center.z } },
                        { "size", new[] { bounds.size.x, bounds.size.y, bounds.size.z } }
                    };

                case SerializedPropertyType.ObjectReference:
                    return GetObjectReferenceValue(prop);

                case SerializedPropertyType.Vector2Int:
                    return new[] { prop.vector2IntValue.x, prop.vector2IntValue.y };

                case SerializedPropertyType.Vector3Int:
                    return new[] { prop.vector3IntValue.x, prop.vector3IntValue.y, prop.vector3IntValue.z };

                case SerializedPropertyType.RectInt:
                    var rectInt = prop.rectIntValue;
                    return new[] { rectInt.x, rectInt.y, rectInt.width, rectInt.height };

                case SerializedPropertyType.BoundsInt:
                    var boundsInt = prop.boundsIntValue;
                    return new Dictionary<string, object>
                    {
                        { "position", new[] { boundsInt.position.x, boundsInt.position.y, boundsInt.position.z } },
                        { "size", new[] { boundsInt.size.x, boundsInt.size.y, boundsInt.size.z } }
                    };

                case SerializedPropertyType.AnimationCurve:
                    return GetAnimationCurveValue(prop);

                case SerializedPropertyType.ArraySize:
                    return prop.intValue;

                case SerializedPropertyType.Generic:
                    // 配列やネストされたオブジェクトの場合
                    if (prop.isArray)
                    {
                        return GetArrayValue(prop);
                    }
                    return $"<{prop.type}>";

                default:
                    return $"<unsupported: {prop.propertyType}>";
            }
        }

        /// <summary>
        /// ObjectReference の値を取得
        /// </summary>
        private static object GetObjectReferenceValue(SerializedProperty prop)
        {
            var obj = prop.objectReferenceValue;
            if (obj == null)
            {
                // Missing 参照の検出: instanceID が設定されているが実オブジェクトが存在しない
                if (prop.objectReferenceInstanceIDValue != 0)
                {
                    return new Dictionary<string, object>
                    {
                        { "missing", true },
                        { "instance_id", prop.objectReferenceInstanceIDValue }
                    };
                }
                return null;
            }

            var result = new Dictionary<string, object>
            {
                { "instance_id", obj.GetInstanceID() },
                { "name", obj.name },
                { "type", obj.GetType().Name }
            };

            // アセットの場合はパスも追加
            var assetPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(assetPath))
            {
                result["asset_path"] = assetPath;
            }

            // GameObject の場合はシーン内パスを追加
            if (obj is GameObject go)
            {
                result["path"] = GetGameObjectPath(go);
            }
            else if (obj is Component comp && comp.gameObject != null)
            {
                result["gameobject_path"] = GetGameObjectPath(comp.gameObject);
            }

            return result;
        }

        private static string GetGameObjectPath(GameObject go)
            => Mutations.GameObjectResolver.GetHierarchyPath(go);

        /// <summary>
        /// AnimationCurve の値を取得
        /// </summary>
        private static object GetAnimationCurveValue(SerializedProperty prop)
        {
            var curve = prop.animationCurveValue;
            var keys = new List<Dictionary<string, object>>();

            foreach (var key in curve.keys)
            {
                keys.Add(new Dictionary<string, object>
                {
                    { "time", key.time },
                    { "value", key.value },
                    { "inTangent", key.inTangent },
                    { "outTangent", key.outTangent }
                });
            }

            return new Dictionary<string, object>
            {
                { "keys", keys },
                { "preWrapMode", curve.preWrapMode.ToString() },
                { "postWrapMode", curve.postWrapMode.ToString() }
            };
        }

        /// <summary>
        /// 配列の値を取得
        /// </summary>
        private static object GetArrayValue(SerializedProperty prop)
        {
            var size = prop.arraySize;
            var items = new List<object>();

            for (int i = 0; i < size && i < 100; i++) // 最大100要素に制限
            {
                var element = prop.GetArrayElementAtIndex(i);
                items.Add(GetPropertyValue(element));
            }

            if (size > 100)
            {
                items.Add($"... and {size - 100} more items");
            }

            return items;
        }
    }
}
