using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace UniForge.Tools
{
    /// <summary>
    /// ツール引数のパーサー。
    /// JSON 文字列をパースし、型安全な値取得と C# クラスへのリフレクションマッピングを提供する。
    /// 基本的な型アクセスは JsonObject に委譲し、ツール固有の機能（オブジェクトマッピング、snake_case 変換）を追加する。
    /// </summary>
    public class ToolArgsParser
    {
        private static readonly MethodInfo CachedParseObjectMethod = FindParseObjectMethod();
        private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new Dictionary<Type, FieldInfo[]>();
        private static readonly Dictionary<Type, MethodInfo> GenericMethodCache = new Dictionary<Type, MethodInfo>();

        private static MethodInfo FindParseObjectMethod()
        {
            foreach (var m in typeof(ToolArgsParser).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (m.Name == nameof(ParseObject) && m.IsGenericMethodDefinition)
                    return m;
            }
            return null;
        }

        private static FieldInfo[] GetCachedFields(Type type)
        {
            if (!FieldCache.TryGetValue(type, out var fields))
            {
                fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                FieldCache[type] = fields;
            }
            return fields;
        }

        private static MethodInfo GetOrCreateGenericMethod(Type elementType)
        {
            if (!GenericMethodCache.TryGetValue(elementType, out var method))
            {
                method = CachedParseObjectMethod?.MakeGenericMethod(elementType);
                if (method != null)
                {
                    GenericMethodCache[elementType] = method;
                }
            }
            return method;
        }

        private readonly JsonObject _json;

        /// <summary>JSON パースに失敗した場合 true</summary>
        public bool HasParseError { get; }

        public ToolArgsParser(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "{}")
            {
                _json = new JsonObject(new Dictionary<string, object>());
            }
            else
            {
                try
                {
                    _json = JsonObject.Parse(json);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ToolArgsParser] Failed to parse JSON: {ex.Message}");
                    _json = new JsonObject(new Dictionary<string, object>());
                    HasParseError = true;
                }
            }
        }

        /// <summary>内部の JsonObject を取得</summary>
        public JsonObject Json => _json;

        // --- JsonObject に委譲するアクセサ ---

        public string GetString(string key, string defaultValue = null) => _json.GetString(key, defaultValue);
        public int GetInt(string key, int defaultValue = 0) => _json.GetInt(key, defaultValue);
        public long GetLong(string key, long defaultValue = 0) => _json.GetLong(key, defaultValue);
        public float GetFloat(string key, float defaultValue = 0f) => _json.GetFloat(key, defaultValue);
        public bool GetBool(string key, bool defaultValue = false) => _json.GetBool(key, defaultValue);
        public int? GetNullableInt(string key) => _json.GetNullableInt(key);
        public bool? GetNullableBool(string key) => _json.GetNullableBool(key);
        public float[] GetFloatArray(string key) => _json.GetFloatArray(key);
        public string[] GetStringArray(string key) => _json.GetStringArray(key);
        public bool HasKey(string key) => _json.HasKey(key);
        public Dictionary<string, object> GetDictionary(string key) => _json.GetObject(key)?.ToDictionary();

        // --- ツール固有の機能 ---

        /// <summary>
        /// オブジェクト配列を取得し、指定した型にリフレクションマッピング。
        /// 配列要素が辞書でない場合はその要素をスキップし、有効な要素のみを返す。
        /// </summary>
        public T[] GetObjectArray<T>(string key) where T : class, new()
        {
            var objects = _json.GetObjectArray(key);
            if (objects == null)
                return null;

            var results = new List<T>(objects.Length);
            for (int i = 0; i < objects.Length; i++)
            {
                var dict = objects[i].ToDictionary();
                results.Add(ParseObject<T>(dict));
            }
            return results.ToArray();
        }

        /// <summary>
        /// Dictionary を指定した型のオブジェクトにマッピング
        /// </summary>
        private T ParseObject<T>(Dictionary<string, object> dict) where T : class, new()
        {
            var obj = new T();
            var fields = GetCachedFields(typeof(T));

            foreach (var field in fields)
            {
                // snake_case のキー名でマッチング
                var snakeName = ToSnakeCase(field.Name);
                if (!dict.TryGetValue(snakeName, out var val))
                {
                    // キャメルケースでも試行
                    if (!dict.TryGetValue(field.Name, out val))
                    {
                        continue;
                    }
                }

                try
                {
                    var converted = ConvertValue(val, field.FieldType);
                    if (converted != null || !field.FieldType.IsValueType)
                    {
                        field.SetValue(obj, converted);
                    }
                }
                catch (Exception ex)
                {
                    var valueDesc = val == null ? "null" : $"{val.GetType().Name}({val})";
                    Debug.LogWarning(
                        $"[ToolArgsParser] Failed to set {typeof(T).Name}.{field.Name} " +
                        $"(type: {field.FieldType.Name}) from value {valueDesc}: {ex.Message}");
                }
            }

            return obj;
        }

        /// <summary>
        /// 値を指定した型に変換
        /// </summary>
        private object ConvertValue(object value, Type targetType)
        {
            if (value == null)
                return null;

            // Nullable 型の処理
            var underlyingType = Nullable.GetUnderlyingType(targetType);
            if (underlyingType != null)
            {
                targetType = underlyingType;
            }

            // 文字列
            if (targetType == typeof(string))
            {
                return value.ToString();
            }

            // 整数
            if (targetType == typeof(int))
            {
                try { return Convert.ToInt32(value); }
                catch (Exception ex) { throw new FormatException($"Cannot convert '{value}' ({value.GetType().Name}) to int: {ex.Message}"); }
            }

            // 長整数
            if (targetType == typeof(long))
            {
                try { return Convert.ToInt64(value); }
                catch (Exception ex) { throw new FormatException($"Cannot convert '{value}' ({value.GetType().Name}) to long: {ex.Message}"); }
            }

            // 浮動小数点
            if (targetType == typeof(float))
            {
                try { return Convert.ToSingle(value); }
                catch (Exception ex) { throw new FormatException($"Cannot convert '{value}' ({value.GetType().Name}) to float: {ex.Message}"); }
            }

            if (targetType == typeof(double))
            {
                try { return Convert.ToDouble(value); }
                catch (Exception ex) { throw new FormatException($"Cannot convert '{value}' ({value.GetType().Name}) to double: {ex.Message}"); }
            }

            // ブール
            if (targetType == typeof(bool))
            {
                try { return Convert.ToBoolean(value); }
                catch (Exception ex) { throw new FormatException($"Cannot convert '{value}' ({value.GetType().Name}) to bool: {ex.Message}"); }
            }

            // float 配列
            if (targetType == typeof(float[]))
            {
                if (value is List<object> list)
                {
                    var result = new float[list.Count];
                    for (int i = 0; i < list.Count; i++)
                    {
                        result[i] = Convert.ToSingle(list[i]);
                    }
                    return result;
                }
                return null;
            }

            // string 配列
            if (targetType == typeof(string[]))
            {
                if (value is List<object> list)
                {
                    var result = new string[list.Count];
                    for (int i = 0; i < list.Count; i++)
                    {
                        result[i] = list[i]?.ToString();
                    }
                    return result;
                }
                return null;
            }

            // Dictionary<string, object>
            if (targetType == typeof(Dictionary<string, object>))
            {
                return value as Dictionary<string, object>;
            }

            // ネストしたオブジェクト配列
            if (targetType.IsArray && targetType.GetElementType().IsClass)
            {
                var elementType = targetType.GetElementType();
                if (value is List<object> list)
                {
                    var parseMethod = GetOrCreateGenericMethod(elementType);
                    if (parseMethod == null)
                    {
                        Debug.LogError($"[ToolArgsParser] ParseObject generic method not found for type {elementType.Name}");
                        return null;
                    }

                    var results = new List<object>(list.Count);
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i] is Dictionary<string, object> dict)
                        {
                            var item = parseMethod.Invoke(this, new object[] { dict });
                            results.Add(item);
                        }
                        else if (list[i] != null)
                        {
                            Debug.LogWarning(
                                $"[ToolArgsParser] Element at index {i} is {list[i].GetType().Name}, expected object for {elementType.Name}. Skipping.");
                        }
                    }

                    var arr = Array.CreateInstance(elementType, results.Count);
                    for (int i = 0; i < results.Count; i++)
                    {
                        arr.SetValue(results[i], i);
                    }
                    return arr;
                }
            }

            // その他の型はそのまま返す
            return value;
        }

        /// <summary>
        /// キャメルケースをスネークケースに変換。
        /// 連続する大文字（略語）は1つのグループとして扱う。
        /// 例: "XMLParser" → "xml_parser", "backgroundColor" → "background_color"
        /// </summary>
        /// <summary>
        /// 連続する大文字（略語）は1つのグループとして扱う。
        /// 例: "XMLParser" → "xml_parser", "backgroundColor" → "background_color"
        /// </summary>
        internal static string ToSnakeCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var result = new System.Text.StringBuilder(input.Length + 4);
            for (int i = 0; i < input.Length; i++)
            {
                var c = input[i];
                if (char.IsUpper(c))
                {
                    if (i > 0)
                    {
                        bool prevIsLower = char.IsLower(input[i - 1]);
                        bool nextIsLower = i + 1 < input.Length && char.IsLower(input[i + 1]);
                        if (prevIsLower || nextIsLower)
                        {
                            result.Append('_');
                        }
                    }
                    result.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    result.Append(c);
                }
            }
            return result.ToString();
        }
    }
}
