using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace UniForge
{
    /// <summary>
    /// Simple YAML serializer for Unity without external dependencies.
    /// Converts objects to YAML text using reflection with field caching.
    /// </summary>
    internal static class YamlWriter
    {
        private const int MaxDepth = 32;
        private const int IndentSize = 2;

        // Cache for reflected fields per type (independent from SimpleJson)
        private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new Dictionary<Type, FieldInfo[]>();
        private static readonly object CacheLock = new object();

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
        /// Serialize an object to YAML string.
        /// </summary>
        public static string Serialize(object obj)
        {
            if (obj == null)
            {
                return "";
            }

            var sb = GetBuilder();
            SerializeObject(obj, sb, 0);
            return sb.ToString();
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

        private static void SerializeObject(object obj, StringBuilder sb, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new InvalidOperationException(
                    $"Maximum serialization depth ({MaxDepth}) exceeded. Possible circular reference.");
            }

            var type = obj.GetType();
            var fields = GetCachedFields(type);

            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                var value = field.GetValue(obj);

                if (value == null)
                {
                    continue;
                }

                WriteKey(sb, field.Name, depth);
                WriteValue(value, sb, depth);
            }
        }

        private static void WriteKey(StringBuilder sb, string key, int depth)
        {
            AppendIndent(sb, depth);
            sb.Append(key);
            sb.Append(':');
        }

        private static void WriteValue(object value, StringBuilder sb, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new InvalidOperationException(
                    $"Maximum serialization depth ({MaxDepth}) exceeded. Possible circular reference.");
            }

            if (value == null)
            {
                sb.Append(" null\n");
                return;
            }

            var type = value.GetType();
            var typeCode = Type.GetTypeCode(type);

            switch (typeCode)
            {
                case TypeCode.String:
                    WriteString((string)value, sb, depth);
                    return;

                case TypeCode.Boolean:
                    sb.Append(' ');
                    sb.Append((bool)value ? "true" : "false");
                    sb.Append('\n');
                    return;

                case TypeCode.Int32:
                    sb.Append(' ');
                    sb.Append(((int)value).ToString());
                    sb.Append('\n');
                    return;

                case TypeCode.Int64:
                    sb.Append(' ');
                    sb.Append(((long)value).ToString());
                    sb.Append('\n');
                    return;

                case TypeCode.Double:
                    sb.Append(' ');
                    sb.Append(((double)value).ToString(CultureInfo.InvariantCulture));
                    sb.Append('\n');
                    return;

                case TypeCode.Single:
                    sb.Append(' ');
                    sb.Append(((float)value).ToString(CultureInfo.InvariantCulture));
                    sb.Append('\n');
                    return;

                case TypeCode.Int16:
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Decimal:
                    sb.Append(' ');
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    sb.Append('\n');
                    return;
            }

            // IList (arrays and List<T>)
            if (value is IList list)
            {
                WriteList(list, sb, depth);
                return;
            }

            // IDictionary
            if (value is IDictionary dict)
            {
                WriteDictionary(dict, sb, depth);
                return;
            }

            // Nested object
            sb.Append('\n');
            SerializeObject(value, sb, depth + 1);
        }

        private static void WriteString(string str, StringBuilder sb, int depth)
        {
            if (str.IndexOf('\n') >= 0)
            {
                // Literal block scalar for multi-line strings
                sb.Append(" |\n");
                var lines = str.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    AppendIndent(sb, depth + 1);
                    sb.Append(lines[i]);
                    sb.Append('\n');
                }
            }
            else
            {
                sb.Append(' ');
                sb.Append(str);
                sb.Append('\n');
            }
        }

        private static void WriteList(IList list, StringBuilder sb, int depth)
        {
            if (list.Count == 0)
            {
                sb.Append(" []\n");
                return;
            }

            // Determine if all elements are primitives for inline format
            if (IsPrimitiveList(list))
            {
                sb.Append(" [");
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    AppendInlineValue(list[i], sb);
                }
                sb.Append("]\n");
                return;
            }

            // Block sequence for object elements
            sb.Append('\n');
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item == null)
                {
                    AppendIndent(sb, depth);
                    sb.Append("- null\n");
                    continue;
                }

                var itemType = item.GetType();
                var itemTypeCode = Type.GetTypeCode(itemType);

                if (itemTypeCode != TypeCode.Object || item is string)
                {
                    // Primitive item in block sequence
                    AppendIndent(sb, depth);
                    sb.Append("- ");
                    AppendInlineValue(item, sb);
                    sb.Append('\n');
                }
                else if (item is IDictionary itemDict)
                {
                    WriteDictionarySequenceItem(itemDict, sb, depth);
                }
                else
                {
                    // Object item: first field on same line as "- ", rest indented
                    WriteObjectSequenceItem(item, sb, depth);
                }
            }
        }

        private static void WriteObjectSequenceItem(object obj, StringBuilder sb, int depth)
        {
            if (depth + 1 > MaxDepth)
            {
                throw new InvalidOperationException(
                    $"Maximum serialization depth ({MaxDepth}) exceeded. Possible circular reference.");
            }

            var fields = GetCachedFields(obj.GetType());

            // Find the first non-null field
            int firstIndex = -1;
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].GetValue(obj) != null)
                {
                    firstIndex = i;
                    break;
                }
            }

            if (firstIndex < 0)
            {
                // All fields are null, output empty sequence item
                AppendIndent(sb, depth);
                sb.Append("-\n");
                return;
            }

            // First non-null field on the "- " line
            AppendIndent(sb, depth);
            sb.Append("- ");
            sb.Append(fields[firstIndex].Name);
            sb.Append(':');
            WriteValue(fields[firstIndex].GetValue(obj), sb, depth + 1);

            // Remaining fields indented under the sequence item
            for (int i = firstIndex + 1; i < fields.Length; i++)
            {
                var value = fields[i].GetValue(obj);
                if (value == null) continue;

                WriteKey(sb, fields[i].Name, depth + 1);
                WriteValue(value, sb, depth + 1);
            }
        }

        private static void WriteDictionary(IDictionary dict, StringBuilder sb, int depth)
        {
            if (dict.Count == 0)
            {
                sb.Append(" {}\n");
                return;
            }

            sb.Append('\n');
            var enumerator = dict.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var key = enumerator.Key;
                var val = enumerator.Value;

                WriteKey(sb, key.ToString(), depth + 1);
                WriteValue(val, sb, depth + 1);
            }
        }

        private static void WriteDictionarySequenceItem(IDictionary dict, StringBuilder sb, int depth)
        {
            var enumerator = dict.GetEnumerator();
            bool first = true;
            while (enumerator.MoveNext())
            {
                if (first)
                {
                    AppendIndent(sb, depth);
                    sb.Append("- ");
                    sb.Append(enumerator.Key.ToString());
                    sb.Append(':');
                    WriteValue(enumerator.Value, sb, depth + 1);
                    first = false;
                }
                else
                {
                    WriteKey(sb, enumerator.Key.ToString(), depth + 1);
                    WriteValue(enumerator.Value, sb, depth + 1);
                }
            }

            if (first)
            {
                // Empty dictionary
                AppendIndent(sb, depth);
                sb.Append("- {}\n");
            }
        }

        private static bool IsPrimitiveList(IList list)
        {
            // Check element type for typed arrays/lists
            var listType = list.GetType();
            Type elementType = null;

            if (listType.IsArray)
            {
                elementType = listType.GetElementType();
            }
            else if (listType.IsGenericType)
            {
                var genericArgs = listType.GetGenericArguments();
                if (genericArgs.Length == 1)
                {
                    elementType = genericArgs[0];
                }
            }

            if (elementType != null)
            {
                var code = Type.GetTypeCode(elementType);
                return code != TypeCode.Object || elementType == typeof(string);
            }

            // Fallback: check each element
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item == null) continue;
                var code = Type.GetTypeCode(item.GetType());
                if (code == TypeCode.Object && !(item is string)) return false;
            }
            return true;
        }

        private static void AppendInlineValue(object value, StringBuilder sb)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            var typeCode = Type.GetTypeCode(value.GetType());

            switch (typeCode)
            {
                case TypeCode.String:
                    sb.Append((string)value);
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

                default:
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
            }
        }

        private static void AppendIndent(StringBuilder sb, int depth)
        {
            for (int i = 0; i < depth * IndentSize; i++)
            {
                sb.Append(' ');
            }
        }
    }
}
