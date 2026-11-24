using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace UniForge.Tools.Mutations
{
    internal static class EvalValueConverter
    {
        private const long MaxExactInteger = 1L << 53;
        private const int MaxItems = 100;
        private const int MaxDepth = 3;
        private const int MaxValues = 1000;
        private const int MaxStringLength = 65536;

        internal static (object value, string type) Convert(object value)
        {
            if (value == null || value is UnityEngine.Object unityObject && unityObject == null)
                return (null, "null");

            var type = value.GetType();
            var remaining = MaxValues;
            return (ConvertValue(value, 0, ref remaining), type.FullName ?? type.Name);
        }

        private static object ConvertValue(object value, int depth, ref int remaining)
        {
            remaining--;
            if (value == null || value is UnityEngine.Object destroyedObject && destroyedObject == null)
                return null;

            if (value is decimal precise)
                return precise.ToString(CultureInfo.InvariantCulture);
            if (depth >= MaxDepth)
                return LimitString(value.ToString());

            switch (value)
            {
                case Enum enumValue:
                    return enumValue.ToString();
                case string text:
                    return LimitString(text);
                case long signed when signed < -MaxExactInteger || signed > MaxExactInteger:
                    return signed.ToString(CultureInfo.InvariantCulture);
                case ulong unsigned when unsigned > (ulong)MaxExactInteger:
                    return unsigned.ToString(CultureInfo.InvariantCulture);
                case bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double:
                    return value;
                case UnityEngine.Object unityObject:
                    return new Dictionary<string, object>
                    {
                        ["type"] = value.GetType().FullName,
                        ["name"] = LimitString(unityObject.name),
                        ["instance_id"] = unityObject.GetInstanceID()
                    };
                case IDictionary dictionary:
                    return ConvertDictionary(dictionary, depth, ref remaining);
                case IEnumerable sequence:
                    return ConvertSequence(sequence, depth, ref remaining);
                default:
                    return LimitString(value.ToString());
            }
        }

        private static Dictionary<string, object> ConvertDictionary(IDictionary dictionary, int depth, ref int remaining)
        {
            var result = new Dictionary<string, object>();
            if (remaining == 0)
                return result;

            var count = 0;
            foreach (DictionaryEntry entry in dictionary)
            {
                result[LimitString(entry.Key?.ToString() ?? "null")] = ConvertValue(entry.Value, depth + 1, ref remaining);
                if (++count == MaxItems || remaining == 0)
                    break;
            }

            return result;
        }

        private static List<object> ConvertSequence(IEnumerable sequence, int depth, ref int remaining)
        {
            var result = new List<object>();
            if (remaining == 0)
                return result;

            foreach (var item in sequence)
            {
                result.Add(ConvertValue(item, depth + 1, ref remaining));
                if (result.Count == MaxItems || remaining == 0)
                    break;
            }

            return result;
        }

        internal static string LimitString(string value)
        {
            if (value == null || value.Length <= MaxStringLength)
                return value;

            var length = MaxStringLength;
            if (char.IsSurrogatePair(value, length - 1))
                length--;
            return value.Substring(0, length) + $"… (truncated, {value.Length} chars)";
        }
    }
}
