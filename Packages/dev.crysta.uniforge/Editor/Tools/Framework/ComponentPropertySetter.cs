using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UniForge.Tools.Mutations;

namespace UniForge.Tools
{
    /// <summary>
    /// プロパティ設定の結果
    /// </summary>
    public class PropertySetResult
    {
        /// <summary>設定に成功したプロパティ</summary>
        public Dictionary<string, object> set_properties = new Dictionary<string, object>();

        /// <summary>エラーリスト</summary>
        public List<string> errors = new List<string>();

        /// <summary>全て成功したか</summary>
        public bool AllSucceeded => errors.Count == 0;
    }

    /// <summary>
    /// コンポーネントのプロパティを SerializedObject 経由で設定するヘルパー
    /// </summary>
    public static class ComponentPropertySetter
    {
        /// <summary>
        /// コンポーネントのプロパティを設定
        /// </summary>
        /// <param name="component">対象コンポーネント</param>
        /// <param name="properties">設定するプロパティ（キー: プロパティ名、値: 設定値）</param>
        /// <returns>設定結果</returns>
        public static PropertySetResult SetProperties(Component component, Dictionary<string, object> properties)
        {
            var result = new PropertySetResult();

            if (component == null)
            {
                result.errors.Add("Component is null");
                return result;
            }

            if (properties == null || properties.Count == 0)
            {
                return result;
            }

            using (var so = new SerializedObject(component))
            {
                foreach (var kvp in properties)
                {
                    var propName = kvp.Key;
                    var value = kvp.Value;

                    // 空文字列チェック
                    if (string.IsNullOrEmpty(propName))
                    {
                        result.errors.Add("Property name is empty");
                        continue;
                    }

                    // SerializedProperty を検索（複数のエイリアスを試行）
                    var prop = FindPropertyWithAliases(so, propName);

                    if (prop == null)
                    {
                        result.errors.Add($"Property not found: {propName}");
                        continue;
                    }

                    if (TrySetPropertyValue(prop, value, out var error))
                    {
                        result.set_properties[propName] = value;
                    }
                    else
                    {
                        result.errors.Add($"{propName}: {error}");
                    }
                }

                so.ApplyModifiedProperties();
            }

            return result;
        }

        private static SerializedProperty FindPropertyWithAliases(SerializedObject so, string propName)
            => SerializedPropertyFinder.FindPropertyWithAliases(so, propName);

        /// <summary>
        /// SerializedProperty に値を設定
        /// </summary>
        private static bool TrySetPropertyValue(SerializedProperty prop, object value, out string error)
        {
            error = null;

            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        // 文字列は InvariantCulture でパース（CurrentCulture に依存させない）
                        prop.intValue = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                        return true;

                    case SerializedPropertyType.Float:
                        prop.floatValue = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                        return true;

                    case SerializedPropertyType.Boolean:
                        prop.boolValue = Convert.ToBoolean(value);
                        return true;

                    case SerializedPropertyType.String:
                        prop.stringValue = value?.ToString() ?? "";
                        return true;

                    case SerializedPropertyType.Vector2:
                        if (TryParseVector2(value, out var v2))
                        {
                            prop.vector2Value = v2;
                            return true;
                        }
                        error = "Invalid Vector2 format (expected [x, y])";
                        return false;

                    case SerializedPropertyType.Vector3:
                        if (TryParseVector3(value, out var v3))
                        {
                            prop.vector3Value = v3;
                            return true;
                        }
                        error = "Invalid Vector3 format (expected [x, y, z])";
                        return false;

                    case SerializedPropertyType.Vector4:
                        if (TryParseVector4(value, out var v4))
                        {
                            prop.vector4Value = v4;
                            return true;
                        }
                        error = "Invalid Vector4 format (expected [x, y, z, w])";
                        return false;

                    case SerializedPropertyType.Color:
                        if (TryParseColor(value, out var color))
                        {
                            prop.colorValue = color;
                            return true;
                        }
                        error = "Invalid Color format (expected [r, g, b] or [r, g, b, a])";
                        return false;

                    case SerializedPropertyType.Enum:
                        if (TryParseEnum(prop, value, out var enumIndex))
                        {
                            prop.enumValueIndex = enumIndex;
                            return true;
                        }
                        error = $"Invalid enum value: {value}";
                        return false;

                    case SerializedPropertyType.LayerMask:
                        prop.intValue = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                        return true;

                    case SerializedPropertyType.Quaternion:
                        if (TryParseQuaternion(value, out var q))
                        {
                            prop.quaternionValue = q;
                            return true;
                        }
                        error = "Invalid Quaternion format (expected [x, y, z, w] or euler [x, y, z])";
                        return false;

                    case SerializedPropertyType.Rect:
                        if (TryParseRect(value, out var rect))
                        {
                            prop.rectValue = rect;
                            return true;
                        }
                        error = "Invalid Rect format (expected [x, y, width, height])";
                        return false;

                    case SerializedPropertyType.Bounds:
                        if (TryParseBounds(value, out var bounds))
                        {
                            prop.boundsValue = bounds;
                            return true;
                        }
                        error = "Invalid Bounds format (expected {center: [x,y,z], size: [x,y,z]})";
                        return false;

                    case SerializedPropertyType.ObjectReference:
                        return TrySetObjectReference(prop, value, out error);

                    case SerializedPropertyType.AnimationCurve:
                        error = "AnimationCurve is not supported";
                        return false;

                    case SerializedPropertyType.Gradient:
                        error = "Gradient is not supported";
                        return false;

                    default:
                        error = $"Unsupported property type: {prop.propertyType}";
                        return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        #region Object Reference Helper

        /// <summary>
        /// ObjectReference を設定
        /// サポート形式:
        /// - instance_id (int / long): EditorUtility.InstanceIDToObject で解決
        /// - {"$ref": "path"}: GameObject のパスから解決（シーン内またはアセットパス）
        /// - {"$ref": "path", "component": "TypeName"}: コンポーネントを解決
        /// - {"$asset": "Assets/..."}: アセットパスから直接ロード
        /// - null: 参照をクリア
        /// </summary>
        private static bool TrySetObjectReference(SerializedProperty prop, object value, out string error)
        {
            error = null;

            // null: 参照をクリア
            if (value == null)
            {
                prop.objectReferenceValue = null;
                return true;
            }

            // instance_id（数値）から解決（SimpleJson 由来の long と直接 API 呼び出しの int の両方に対応）
            // 文字列はパスとして解釈するため除外する
            if (!(value is string) && NumericCoercion.TryToInt64(value, out var instanceId))
            {
                return TryResolveInstanceId(prop, instanceId, out error);
            }

            // {"$ref": "path"}, {"$asset": "path"}, または {"instance_id": id} 形式
            if (value is Dictionary<string, object> dict)
            {
                // $asset: アセットパスから直接ロード
                if (dict.TryGetValue("$asset", out var assetValue) && assetValue is string assetPath)
                {
                    return TryLoadAsset(prop, assetPath, out error);
                }

                if (dict.TryGetValue("$ref", out var refValue) && refValue is string path)
                {
                    // アセットパス（Assets/ で始まる）の場合はアセットをロード
                    if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    {
                        return TryLoadAsset(prop, path, out error);
                    }

                    // シーン内の GameObject を解決
                    var resolveResult = GameObjectResolver.Resolve(path, null, null);
                    if (!resolveResult.Success)
                    {
                        error = $"GameObject not found: {path}";
                        return false;
                    }

                    var go = resolveResult.GameObject;

                    // component が指定されている場合はコンポーネントを取得
                    if (dict.TryGetValue("component", out var compValue) && compValue is string componentType)
                    {
                        var component = FindComponent(go, componentType);
                        if (component == null)
                        {
                            error = $"Component not found: {componentType} on {path}";
                            return false;
                        }
                        prop.objectReferenceValue = component;
                        return true;
                    }

                    // component が指定されていない場合は GameObject を設定
                    prop.objectReferenceValue = go;
                    return true;
                }

                // instance_id キーでも対応
                if (dict.TryGetValue("instance_id", out var idValue) &&
                    NumericCoercion.TryToInt64(idValue, out var dictInstanceId))
                {
                    return TryResolveInstanceId(prop, dictInstanceId, out error);
                }
            }

            // 文字列の場合はパスとして解釈
            if (value is string strPath)
            {
                // アセットパスの場合
                if (strPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    return TryLoadAsset(prop, strPath, out error);
                }

                // シーン内のパス
                var resolveResult = GameObjectResolver.Resolve(strPath, null, null);
                if (!resolveResult.Success)
                {
                    error = $"GameObject not found: {strPath}";
                    return false;
                }
                prop.objectReferenceValue = resolveResult.GameObject;
                return true;
            }

            error = "ObjectReference requires instance_id (int), path (string), {\"$ref\": \"path\"}, {\"$asset\": \"path\"}, or null";
            return false;
        }

        /// <summary>
        /// instance_id から ObjectReference を解決して設定
        /// </summary>
        private static bool TryResolveInstanceId(SerializedProperty prop, long instanceId, out string error)
        {
            error = null;

            if (instanceId < int.MinValue || instanceId > int.MaxValue)
            {
                error = "Instance ID out of range";
                return false;
            }

            var obj = EditorUtility.InstanceIDToObject((int)instanceId);
            if (obj == null)
            {
                error = $"Object not found with instance_id: {instanceId}";
                return false;
            }

            prop.objectReferenceValue = obj;
            return true;
        }

        /// <summary>
        /// アセットパスからオブジェクトをロード
        /// </summary>
        private static bool TryLoadAsset(SerializedProperty prop, string assetPath, out string error)
        {
            error = null;

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null)
            {
                error = $"Asset not found at path: {assetPath}";
                return false;
            }

            prop.objectReferenceValue = asset;
            return true;
        }

        /// <summary>
        /// GameObject からコンポーネントを検索（短縮名またはフルネームで大文字小文字無視で照合）
        /// </summary>
        public static Component FindComponent(GameObject go, string componentType)
        {
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null) continue; // Missing script

                var typeName = component.GetType().Name;
                var fullTypeName = component.GetType().FullName;

                if (typeName.Equals(componentType, StringComparison.OrdinalIgnoreCase) ||
                    fullTypeName.Equals(componentType, StringComparison.OrdinalIgnoreCase))
                {
                    return component;
                }
            }
            return null;
        }

        #endregion

        #region Parse Helpers

        private static bool TryParseVector2(object value, out Vector2 result)
        {
            result = Vector2.zero;
            var arr = GetFloatArray(value);
            if (arr == null || arr.Length < 2) return false;
            result = new Vector2(arr[0], arr[1]);
            return true;
        }

        private static bool TryParseVector3(object value, out Vector3 result)
        {
            result = Vector3.zero;
            var arr = GetFloatArray(value);
            if (arr == null || arr.Length < 3) return false;
            result = new Vector3(arr[0], arr[1], arr[2]);
            return true;
        }

        internal static bool TryParseVector4(object value, out Vector4 result)
        {
            result = Vector4.zero;
            var arr = GetFloatArray(value);
            if (arr == null || arr.Length < 4) return false;
            result = new Vector4(arr[0], arr[1], arr[2], arr[3]);
            return true;
        }

        internal static bool TryParseColor(object value, out Color result)
        {
            result = Color.white;

            // dict 形式: {"r": 0.5, "g": 0.5, "b": 0.5, "a": 1.0}
            if (value is Dictionary<string, object> dict)
            {
                if (!dict.TryGetValue("r", out var rv) ||
                    !dict.TryGetValue("g", out var gv) ||
                    !dict.TryGetValue("b", out var bv))
                    return false;

                try
                {
                    float r = System.Convert.ToSingle(rv, CultureInfo.InvariantCulture);
                    float g = System.Convert.ToSingle(gv, CultureInfo.InvariantCulture);
                    float b = System.Convert.ToSingle(bv, CultureInfo.InvariantCulture);
                    float a = dict.TryGetValue("a", out var av) ? System.Convert.ToSingle(av, CultureInfo.InvariantCulture) : 1f;
                    result = new Color(r, g, b, a);
                    return true;
                }
                catch (System.Exception)
                {
                    return false;
                }
            }

            // 配列形式: [r, g, b] or [r, g, b, a]
            var arr = GetFloatArray(value);
            if (arr == null || arr.Length < 3) return false;
            float alpha = arr.Length >= 4 ? arr[3] : 1f;
            result = new Color(arr[0], arr[1], arr[2], alpha);
            return true;
        }

        private static bool TryParseQuaternion(object value, out Quaternion result)
        {
            result = Quaternion.identity;
            var arr = GetFloatArray(value);
            if (arr == null) return false;

            if (arr.Length == 4)
            {
                // [x, y, z, w] 形式
                result = new Quaternion(arr[0], arr[1], arr[2], arr[3]);
                return true;
            }
            if (arr.Length == 3)
            {
                // Euler [x, y, z] 形式
                result = Quaternion.Euler(arr[0], arr[1], arr[2]);
                return true;
            }
            return false;
        }

        private static bool TryParseRect(object value, out Rect result)
        {
            result = Rect.zero;
            var arr = GetFloatArray(value);
            if (arr == null || arr.Length < 4) return false;
            result = new Rect(arr[0], arr[1], arr[2], arr[3]);
            return true;
        }

        private static bool TryParseBounds(object value, out Bounds result)
        {
            result = new Bounds();
            if (value is Dictionary<string, object> dict)
            {
                Vector3 center = Vector3.zero, size = Vector3.zero;
                if (dict.TryGetValue("center", out var c) && TryParseVector3(c, out center) &&
                    dict.TryGetValue("size", out var s) && TryParseVector3(s, out size))
                {
                    result = new Bounds(center, size);
                    return true;
                }
            }
            return false;
        }

        private static bool TryParseEnum(SerializedProperty prop, object value, out int result)
        {
            result = 0;

            // 数値（enumValueIndex として扱う。文字列は名前検索のため除外）
            if (!(value is string) && NumericCoercion.TryToInt64(value, out var numericIndex))
            {
                result = (int)numericIndex;
                return true;
            }

            // 文字列（名前で検索）
            if (value is string name)
            {
                var names = prop.enumNames;
                for (int idx = 0; idx < names.Length; idx++)
                {
                    if (string.Equals(names[idx], name, StringComparison.OrdinalIgnoreCase))
                    {
                        result = idx;
                        return true;
                    }
                }
            }

            return false;
        }

        private static float[] GetFloatArray(object value)
        {
            if (value is List<object> list)
            {
                var arr = new float[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    try
                    {
                        arr[i] = Convert.ToSingle(list[i], CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                        return null;
                    }
                }
                return arr;
            }
            if (value is float[] fa) return fa;
            if (value is double[] da)
            {
                var arr = new float[da.Length];
                for (int i = 0; i < da.Length; i++) arr[i] = (float)da[i];
                return arr;
            }
            return null;
        }

        #endregion
    }
}
