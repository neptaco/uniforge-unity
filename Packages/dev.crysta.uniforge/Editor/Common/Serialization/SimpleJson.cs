using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace UniForge
{
    /// <summary>
    /// Simple JSON serializer for Unity without external dependencies.
    /// Optimized for performance with reflection caching.
    /// </summary>
    public static class SimpleJson
    {
        // Maximum recursion depth to prevent stack overflow
        private const int MaxDepth = 32;

        // Maximum string length to prevent out of memory (10MB)
        internal const int MaxStringLength = 10 * 1024 * 1024;

        // Cache for reflected fields per type
        private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new Dictionary<Type, FieldInfo[]>();
        private static readonly object CacheLock = new object();

        // Reusable StringBuilder pool (simple single-instance for editor use)
        [ThreadStatic]
        private static StringBuilder _cachedBuilder;

        private static StringBuilder GetBuilder()
        {
            var sb = _cachedBuilder;
            if (sb == null)
            {
                _cachedBuilder = sb = new StringBuilder(256);
            }
            else
            {
                sb.Clear();
            }
            return sb;
        }

        /// <summary>
        /// Serialize an object to JSON string
        /// </summary>
        public static string Serialize(object obj)
        {
            if (obj == null)
            {
                return "null";
            }

            var sb = GetBuilder();
            SerializeValue(obj, sb, 0);
            return sb.ToString();
        }

        /// <summary>
        /// Create a JSON object builder for manual construction
        /// </summary>
        public static JsonObjectBuilder Object()
        {
            return new JsonObjectBuilder();
        }

        /// <summary>
        /// Parse JSON string to Dictionary
        /// </summary>
        public static Dictionary<string, object> Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return new Dictionary<string, object>();
            var parser = new JsonParser(json);
            return parser.ParseObject();
        }

        /// <summary>
        /// Simple JSON parser
        /// </summary>
        private class JsonParser
        {
            private readonly string _json;
            private int _pos;

            public JsonParser(string json)
            {
                _json = json;
                _pos = 0;
            }

            public Dictionary<string, object> ParseObject()
            {
                var result = new Dictionary<string, object>();
                SkipWhitespace();
                if (_pos >= _json.Length || _json[_pos] != '{') return result;
                _pos++; // skip '{'

                SkipWhitespace();
                if (_pos < _json.Length && _json[_pos] == '}')
                {
                    _pos++;
                    return result;
                }

                while (_pos < _json.Length)
                {
                    int prevPos = _pos;  // Track position for infinite loop prevention

                    SkipWhitespace();
                    var key = ParseString();
                    SkipWhitespace();
                    if (_pos < _json.Length && _json[_pos] == ':') _pos++;
                    SkipWhitespace();
                    var value = ParseValue();
                    if (!string.IsNullOrEmpty(key))
                    {
                        result[key] = value;
                    }
                    SkipWhitespace();
                    if (_pos >= _json.Length) break;
                    if (_json[_pos] == '}') { _pos++; break; }
                    if (_json[_pos] == ',') _pos++;

                    // Prevent infinite loop on invalid JSON — skip silently
                    if (_pos == prevPos)
                    {
                        _pos++;
                    }
                }
                return result;
            }

            private object ParseValue()
            {
                SkipWhitespace();
                if (_pos >= _json.Length) return null;

                char c = _json[_pos];
                if (c == '"') return ParseString();
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == 't') { _pos += 4; return true; }
                if (c == 'f') { _pos += 5; return false; }
                if (c == 'n') { _pos += 4; return null; }
                if (c == '-' || char.IsDigit(c)) return ParseNumber();
                return null;
            }

            private string ParseString()
            {
                if (_pos >= _json.Length || _json[_pos] != '"') return "";
                _pos++; // skip opening quote
                var sb = new StringBuilder();
                while (_pos < _json.Length)
                {
                    char c = _json[_pos++];
                    if (c == '"') break;
                    if (c == '\\' && _pos < _json.Length)
                    {
                        char escaped = _json[_pos++];
                        switch (escaped)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case 'u':
                                if (_pos + 4 <= _json.Length)
                                {
                                    var hex = _json.Substring(_pos, 4);
                                    sb.Append((char)Convert.ToInt32(hex, 16));
                                    _pos += 4;
                                }
                                break;
                            default: sb.Append(escaped); break;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                return sb.ToString();
            }

            private List<object> ParseArray()
            {
                var result = new List<object>();
                if (_pos >= _json.Length || _json[_pos] != '[') return result;
                _pos++; // skip '['
                SkipWhitespace();
                if (_pos < _json.Length && _json[_pos] == ']') { _pos++; return result; }
                while (_pos < _json.Length)
                {
                    int prevPos = _pos;  // Track position for infinite loop prevention

                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (_pos >= _json.Length) break;
                    if (_json[_pos] == ']') { _pos++; break; }
                    if (_json[_pos] == ',') _pos++;

                    // Prevent infinite loop on invalid JSON — skip silently
                    if (_pos == prevPos)
                    {
                        _pos++;
                    }
                }
                return result;
            }

            private object ParseNumber()
            {
                int start = _pos;
                if (_pos < _json.Length && _json[_pos] == '-') _pos++;
                while (_pos < _json.Length && char.IsDigit(_json[_pos])) _pos++;
                bool isFloat = false;
                if (_pos < _json.Length && _json[_pos] == '.')
                {
                    isFloat = true;
                    _pos++;
                    while (_pos < _json.Length && char.IsDigit(_json[_pos])) _pos++;
                }
                if (_pos < _json.Length && (_json[_pos] == 'e' || _json[_pos] == 'E'))
                {
                    isFloat = true;
                    _pos++;
                    if (_pos < _json.Length && (_json[_pos] == '+' || _json[_pos] == '-')) _pos++;
                    while (_pos < _json.Length && char.IsDigit(_json[_pos])) _pos++;
                }
                var str = _json.Substring(start, _pos - start);
                if (isFloat) return double.Parse(str, CultureInfo.InvariantCulture);
                if (long.TryParse(str, out var l)) return l;
                return double.Parse(str, CultureInfo.InvariantCulture);
            }

            private void SkipWhitespace()
            {
                while (_pos < _json.Length && char.IsWhiteSpace(_json[_pos])) _pos++;
            }
        }

        private static FieldInfo[] GetCachedFields(Type type)
        {
            lock (CacheLock)
            {
                if (FieldCache.TryGetValue(type, out var cached))
                {
                    return cached;
                }

                var allFields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                var validFields = new List<FieldInfo>(allFields.Length);

                for (int i = 0; i < allFields.Length; i++)
                {
                    if (!allFields[i].IsDefined(typeof(NonSerializedAttribute), false))
                    {
                        validFields.Add(allFields[i]);
                    }
                }

                var result = validFields.ToArray();
                FieldCache[type] = result;
                return result;
            }
        }

        private static void SerializeValue(object value, StringBuilder sb, int depth)
        {
            // Check recursion depth to prevent stack overflow
            if (depth > MaxDepth)
            {
                throw new InvalidOperationException($"Maximum serialization depth ({MaxDepth}) exceeded. Possible circular reference.");
            }

            if (value == null)
            {
                sb.Append("null");
                return;
            }

            // Handle RawJson wrapper first (most specific)
            if (value is RawJson rawJson)
            {
                sb.Append(rawJson.Json ?? "null");
                return;
            }

            // Use TypeCode for fast primitive checks
            var typeCode = Type.GetTypeCode(value.GetType());

            switch (typeCode)
            {
                case TypeCode.String:
                    SerializeString((string)value, sb);
                    return;

                case TypeCode.Boolean:
                    sb.Append((bool)value ? "true" : "false");
                    return;

                case TypeCode.Int32:
                    sb.Append(((int)value).ToString());
                    return;

                case TypeCode.Int64:
                    sb.Append(((long)value).ToString());
                    return;

                case TypeCode.Double:
                    sb.Append(((double)value).ToString(CultureInfo.InvariantCulture));
                    return;

                case TypeCode.Single:
                    sb.Append(((float)value).ToString(CultureInfo.InvariantCulture));
                    return;

                case TypeCode.Int16:
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Decimal:
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
            }

            // Check for collections (IList covers arrays and List<T>)
            if (value is IList list)
            {
                SerializeArray(list, sb, depth);
                return;
            }

            // Check for dictionary
            if (value is IDictionary dict)
            {
                SerializeDictionary(dict, sb, depth);
                return;
            }

            // Object serialization
            SerializeObject(value, sb, depth);
        }

        private static void SerializeString(string str, StringBuilder sb)
        {
            // Validate string length to prevent out of memory
            if (str.Length > MaxStringLength)
            {
                throw new ArgumentException(
                    $"String length ({str.Length}) exceeds maximum allowed length ({MaxStringLength})",
                    nameof(str)
                );
            }

            sb.Append('"');

            int len = str.Length;
            for (int i = 0; i < len; i++)
            {
                char c = str[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("X4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }

            sb.Append('"');
        }

        private static void SerializeArray(IList list, StringBuilder sb, int depth)
        {
            sb.Append('[');

            int count = list.Count;
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                SerializeValue(list[i], sb, depth + 1);
            }

            sb.Append(']');
        }

        private static void SerializeDictionary(IDictionary dict, StringBuilder sb, int depth)
        {
            sb.Append('{');

            bool first = true;
            var enumerator = dict.GetEnumerator();
            while (enumerator.MoveNext())
            {
                if (!first) sb.Append(',');
                first = false;

                SerializeString(enumerator.Key.ToString(), sb);
                sb.Append(':');
                SerializeValue(enumerator.Value, sb, depth + 1);
            }

            sb.Append('}');
        }

        private static void SerializeObject(object obj, StringBuilder sb, int depth)
        {
            var type = obj.GetType();
            var fields = GetCachedFields(type);

            sb.Append('{');

            int fieldCount = fields.Length;
            for (int i = 0; i < fieldCount; i++)
            {
                if (i > 0) sb.Append(',');

                var field = fields[i];
                SerializeString(field.Name, sb);
                sb.Append(':');
                SerializeValue(field.GetValue(obj), sb, depth + 1);
            }

            sb.Append('}');
        }
    }

    /// <summary>
    /// Wrapper for raw JSON that should not be escaped
    /// </summary>
    public readonly struct RawJson
    {
        public readonly string Json;

        public RawJson(string json)
        {
            Json = json;
        }
    }

    /// <summary>
    /// Builder for constructing JSON objects manually
    /// </summary>
    public sealed class JsonObjectBuilder
    {
        private readonly StringBuilder _sb;
        private bool _hasProperty;

        public JsonObjectBuilder()
        {
            _sb = new StringBuilder(128);
            _sb.Append('{');
        }

        public JsonObjectBuilder Add(string key, string value)
        {
            AppendKey(key);
            if (value == null)
            {
                _sb.Append("null");
            }
            else
            {
                AppendString(value);
            }
            return this;
        }

        public JsonObjectBuilder Add(string key, bool value)
        {
            AppendKey(key);
            _sb.Append(value ? "true" : "false");
            return this;
        }

        public JsonObjectBuilder Add(string key, int value)
        {
            AppendKey(key);
            _sb.Append(value);
            return this;
        }

        public JsonObjectBuilder Add(string key, long value)
        {
            AppendKey(key);
            _sb.Append(value);
            return this;
        }

        /// <summary>
        /// Add a raw JSON value (will not be escaped)
        /// </summary>
        public JsonObjectBuilder AddRaw(string key, string rawJson)
        {
            AppendKey(key);
            _sb.Append(rawJson ?? "null");
            return this;
        }

        private void AppendKey(string key)
        {
            if (_hasProperty)
            {
                _sb.Append(',');
            }
            _hasProperty = true;

            AppendString(key);
            _sb.Append(':');
        }

        private void AppendString(string str)
        {
            // Validate string length to prevent out of memory
            if (str.Length > SimpleJson.MaxStringLength)
            {
                throw new ArgumentException(
                    $"String length ({str.Length}) exceeds maximum allowed length ({SimpleJson.MaxStringLength})",
                    nameof(str)
                );
            }

            _sb.Append('"');

            int len = str.Length;
            for (int i = 0; i < len; i++)
            {
                char c = str[i];
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\b': _sb.Append("\\b"); break;
                    case '\f': _sb.Append("\\f"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (c < 32)
                        {
                            _sb.Append("\\u");
                            _sb.Append(((int)c).ToString("X4"));
                        }
                        else
                        {
                            _sb.Append(c);
                        }
                        break;
                }
            }

            _sb.Append('"');
        }

        public override string ToString()
        {
            _sb.Append('}');
            return _sb.ToString();
        }
    }
}
