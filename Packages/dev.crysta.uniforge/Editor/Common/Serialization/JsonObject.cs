using System;
using System.Collections.Generic;
using System.Globalization;

namespace UniForge
{
    /// <summary>
    /// JSON オブジェクトの型安全なラッパー。
    /// SimpleJson.Parse が返す Dictionary&lt;string, object&gt; に対して
    /// 型変換付きアクセサを提供する。
    /// </summary>
    public class JsonObject
    {
        private readonly Dictionary<string, object> _data;

        public JsonObject(Dictionary<string, object> data)
        {
            _data = data ?? new Dictionary<string, object>();
        }

        /// <summary>JSON 文字列をパースして JsonObject を生成</summary>
        public static JsonObject Parse(string json)
        {
            return new JsonObject(SimpleJson.Parse(json));
        }

        /// <summary>キーが存在するか確認</summary>
        public bool HasKey(string key)
        {
            return _data.ContainsKey(key);
        }

        /// <summary>文字列を取得</summary>
        public string GetString(string key, string defaultValue = null)
        {
            if (_data.TryGetValue(key, out var value) && value is string s)
                return s;
            return defaultValue;
        }

        /// <summary>整数を取得</summary>
        public int GetInt(string key, int defaultValue = 0)
        {
            if (_data.TryGetValue(key, out var value))
            {
                if (value is int i) return i;
                if (value is long l) return (int)l;
                if (value is double d) return (int)d;
                // カルチャ非依存でパースする（InvariantCulture）
                if (int.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;
            }
            return defaultValue;
        }

        /// <summary>長整数を取得</summary>
        public long GetLong(string key, long defaultValue = 0)
        {
            if (_data.TryGetValue(key, out var value))
            {
                if (value is long l) return l;
                if (value is int i) return i;
                if (value is double d) return (long)d;
                // カルチャ非依存でパースする（InvariantCulture）
                if (long.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;
            }
            return defaultValue;
        }

        /// <summary>浮動小数点を取得</summary>
        public float GetFloat(string key, float defaultValue = 0f)
        {
            if (_data.TryGetValue(key, out var value))
            {
                if (value is float f) return f;
                if (value is double d) return (float)d;
                if (value is int i) return i;
                if (value is long l) return l;
                // カルチャ非依存でパースする（InvariantCulture、桁区切りは不許可）
                if (float.TryParse(value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return parsed;
            }
            return defaultValue;
        }

        /// <summary>ブール値を取得</summary>
        public bool GetBool(string key, bool defaultValue = false)
        {
            if (_data.TryGetValue(key, out var value))
            {
                if (value is bool b) return b;
                if (bool.TryParse(value?.ToString(), out var parsed)) return parsed;
            }
            return defaultValue;
        }

        /// <summary>Nullable int を取得</summary>
        public int? GetNullableInt(string key)
        {
            if (!_data.TryGetValue(key, out var value))
                return null;

            if (value is int i) return i;
            if (value is long l) return (int)l;
            if (value is double d) return (int)d;
            if (int.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;
            return null;
        }

        /// <summary>Nullable bool を取得</summary>
        public bool? GetNullableBool(string key)
        {
            if (!_data.TryGetValue(key, out var value))
                return null;

            if (value is bool b) return b;
            if (bool.TryParse(value?.ToString(), out var parsed)) return parsed;
            return null;
        }

        /// <summary>float 配列を取得</summary>
        public float[] GetFloatArray(string key)
        {
            if (!_data.TryGetValue(key, out var value))
                return null;

            if (value is List<object> list)
            {
                var result = new float[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    try
                    {
                        result[i] = Convert.ToSingle(list[i]);
                    }
                    catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
                    {
                        return null;
                    }
                }
                return result;
            }

            return null;
        }

        /// <summary>文字列配列を取得</summary>
        public string[] GetStringArray(string key)
        {
            if (!_data.TryGetValue(key, out var value))
                return null;

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

        /// <summary>ネストした JsonObject を取得</summary>
        public JsonObject GetObject(string key)
        {
            if (_data.TryGetValue(key, out var value) && value is Dictionary<string, object> dict)
                return new JsonObject(dict);
            return null;
        }

        /// <summary>JsonObject 配列を取得</summary>
        public JsonObject[] GetObjectArray(string key)
        {
            if (!_data.TryGetValue(key, out var value))
                return null;

            if (value is List<object> list)
            {
                var results = new List<JsonObject>(list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] is Dictionary<string, object> dict)
                        results.Add(new JsonObject(dict));
                }
                return results.ToArray();
            }

            return null;
        }

        /// <summary>内部の Dictionary を取得（シリアライズ等で必要な場合）</summary>
        public Dictionary<string, object> ToDictionary()
        {
            return _data;
        }
    }
}
