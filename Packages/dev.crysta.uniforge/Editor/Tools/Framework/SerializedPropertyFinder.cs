using System;
using UnityEditor;

namespace UniForge.Tools
{
    /// <summary>
    /// SerializedObject からプロパティ名のエイリアスを試行して SerializedProperty を検索する共通ヘルパー。
    /// ComponentPropertyGetter と ComponentPropertySetter の両方で使用される。
    /// </summary>
    public static class SerializedPropertyFinder
    {
        /// <summary>
        /// プロパティ名のエイリアスを試行して SerializedProperty を検索する。
        /// Inspector 表示名（camelCase）とシリアライズ名（m_ プレフィックス付き）の両方に対応。
        /// </summary>
        public static SerializedProperty FindPropertyWithAliases(SerializedObject so, string propName)
        {
            // 1. そのまま試行
            var prop = so.FindProperty(propName);
            if (prop != null) return prop;

            // 2. m_ + PascalCase（例: backgroundColor → m_BackgroundColor）
            if (propName.Length > 0)
            {
                prop = so.FindProperty("m_" + char.ToUpperInvariant(propName[0]) + propName.Substring(1));
                if (prop != null) return prop;
            }

            // 3. m_ + そのまま（例: text → m_text）
            prop = so.FindProperty("m_" + propName);
            if (prop != null) return prop;

            // 4. camelCase → スペース区切り（例: orthographicSize → orthographic size）
            var spaced = CamelCaseToSpaceSeparated(propName);
            if (!string.Equals(spaced, propName, StringComparison.Ordinal))
            {
                prop = so.FindProperty(spaced);
                if (prop != null) return prop;
            }

            // 5. Inspector 表示名からの逆引き: 全プロパティを走査
            //    displayName と propName を正規化（小文字化 + スペース/アンダースコア除去）して比較
            var normalizedInput = NormalizePropertyName(propName);
            var iter = so.GetIterator();
            if (iter.Next(true))
            {
                do
                {
                    // 完全一致（大文字小文字無視）
                    if (string.Equals(iter.displayName, propName, StringComparison.OrdinalIgnoreCase))
                    {
                        return so.FindProperty(iter.propertyPath);
                    }

                    // 正規化一致（スペース/アンダースコア除去 + 小文字化）
                    if (string.Equals(NormalizePropertyName(iter.displayName), normalizedInput, StringComparison.Ordinal))
                    {
                        return so.FindProperty(iter.propertyPath);
                    }

                    // シリアライズ名の正規化一致（m_ プレフィックス除去 + 小文字化）
                    var serializedName = iter.name;
                    if (serializedName.StartsWith("m_"))
                    {
                        serializedName = serializedName.Substring(2);
                    }
                    if (string.Equals(NormalizePropertyName(serializedName), normalizedInput, StringComparison.Ordinal))
                    {
                        return so.FindProperty(iter.propertyPath);
                    }
                } while (iter.Next(false));
            }

            // 6. PascalCase → camelCase（例: BodyType → bodyType）
            if (propName.Length > 0 && char.IsUpper(propName[0]))
            {
                var camel = char.ToLowerInvariant(propName[0]) + propName.Substring(1);
                prop = so.FindProperty(camel);
                if (prop != null) return prop;
            }

            return null;
        }

        /// <summary>
        /// プロパティ名を正規化: 小文字化 + スペース/アンダースコア除去
        /// 例: "BackGroundColor" → "backgroundcolor", "orthographic size" → "orthographicsize"
        /// </summary>
        private static string NormalizePropertyName(string name)
        {
            var sb = new System.Text.StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (c != ' ' && c != '_')
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// camelCase をスペース区切りに変換
        /// 例: "orthographicSize" → "orthographic size"
        /// </summary>
        private static string CamelCaseToSpaceSeparated(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var sb = new System.Text.StringBuilder(input.Length + 4);
            for (int i = 0; i < input.Length; i++)
            {
                var c = input[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(input[i - 1]))
                {
                    sb.Append(' ');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
