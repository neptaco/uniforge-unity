using System.Globalization;

namespace UniForge.Tools
{
    /// <summary>
    /// JSON パーサー由来の数値（SimpleJson は整数を long、小数を double で返す）と
    /// 直接 API 呼び出し由来の数値（int / float）を統一的に変換するヘルパー。
    /// 文字列は InvariantCulture でパースする（CurrentCulture に依存しない）。
    /// </summary>
    internal static class NumericCoercion
    {
        /// <summary>
        /// 値を long に変換する。int / long / float / double / 整数文字列を受け付ける。
        /// 浮動小数点値は小数部を切り捨てる。
        /// </summary>
        public static bool TryToInt64(object value, out long result)
        {
            switch (value)
            {
                case int i:
                    result = i;
                    return true;
                case long l:
                    result = l;
                    return true;
                case float f:
                    result = (long)f;
                    return true;
                case double d:
                    result = (long)d;
                    return true;
                case string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                    result = parsed;
                    return true;
                default:
                    result = 0;
                    return false;
            }
        }

        /// <summary>
        /// 値を double に変換する。int / long / float / double / 数値文字列を受け付ける。
        /// </summary>
        public static bool TryToDouble(object value, out double result)
        {
            switch (value)
            {
                case int i:
                    result = i;
                    return true;
                case long l:
                    result = l;
                    return true;
                case float f:
                    result = f;
                    return true;
                case double d:
                    result = d;
                    return true;
                case string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                    result = parsed;
                    return true;
                default:
                    result = 0;
                    return false;
            }
        }
    }
}
