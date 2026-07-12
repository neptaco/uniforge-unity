using System;
using System.Collections.Generic;

namespace UniForge
{
    internal static class UniForgeToolDefinitionComparer
    {
        internal static bool AreEqual(List<Dictionary<string, object>> a, List<Dictionary<string, object>> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;

            var mapA = BuildToolMap(a);
            var mapB = BuildToolMap(b);
            if (mapA.Count != mapB.Count) return false;

            foreach (var pair in mapA)
            {
                if (!mapB.TryGetValue(pair.Key, out var other))
                {
                    return false;
                }

                if (!JsonLikeEquals(pair.Value, other))
                {
                    return false;
                }
            }

            return true;
        }

        private static Dictionary<string, Dictionary<string, object>> BuildToolMap(List<Dictionary<string, object>> tools)
        {
            var result = new Dictionary<string, Dictionary<string, object>>();

            foreach (var dict in tools)
            {
                if (dict != null && dict.TryGetValue("name", out var name) && name != null)
                {
                    result[name.ToString()] = dict;
                }
            }

            return result;
        }

        private static bool JsonLikeEquals(object a, object b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;

            if (TryConvertToDouble(a, out var numberA) && TryConvertToDouble(b, out var numberB))
            {
                return Math.Abs(numberA - numberB) < double.Epsilon;
            }

            if (a is string stringA && b is string stringB) return stringA == stringB;
            if (a is bool boolA && b is bool boolB) return boolA == boolB;

            if (a is Dictionary<string, object> dictA && b is Dictionary<string, object> dictB)
            {
                if (dictA.Count != dictB.Count) return false;

                foreach (var pair in dictA)
                {
                    if (!dictB.TryGetValue(pair.Key, out var otherValue))
                    {
                        return false;
                    }

                    if (!JsonLikeEquals(pair.Value, otherValue))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (a is List<object> listA && b is List<object> listB)
            {
                if (listA.Count != listB.Count) return false;

                for (int i = 0; i < listA.Count; i++)
                {
                    if (!JsonLikeEquals(listA[i], listB[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            return a.Equals(b);
        }

        private static bool TryConvertToDouble(object value, out double number)
        {
            switch (value)
            {
                case byte byteValue:
                    number = byteValue;
                    return true;
                case sbyte sbyteValue:
                    number = sbyteValue;
                    return true;
                case short shortValue:
                    number = shortValue;
                    return true;
                case ushort ushortValue:
                    number = ushortValue;
                    return true;
                case int intValue:
                    number = intValue;
                    return true;
                case uint uintValue:
                    number = uintValue;
                    return true;
                case long longValue:
                    number = longValue;
                    return true;
                case ulong ulongValue:
                    number = ulongValue;
                    return true;
                case float floatValue:
                    number = floatValue;
                    return true;
                case double doubleValue:
                    number = doubleValue;
                    return true;
                case decimal decimalValue:
                    number = (double)decimalValue;
                    return true;
                default:
                    number = 0;
                    return false;
            }
        }
    }
}
