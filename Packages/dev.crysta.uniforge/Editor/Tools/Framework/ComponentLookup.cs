using System;
using System.Collections.Generic;
using UnityEngine;

namespace UniForge.Tools
{
    /// <summary>
    /// コンポーネント名からコンポーネントを検索する共通ユーティリティ。
    /// 短縮名（Name）またはフルネーム（FullName）を大文字小文字無視で照合する。
    /// </summary>
    public static class ComponentLookup
    {
        /// <summary>
        /// GameObject からコンポーネントを検索（最初にマッチしたもの）
        /// </summary>
        /// <param name="go">検索対象の GameObject</param>
        /// <param name="componentType">コンポーネントの短縮名またはフルネーム（大文字小文字無視）</param>
        /// <returns>マッチしたコンポーネント。見つからなければ null</returns>
        public static Component FindComponent(GameObject go, string componentType)
        {
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null) continue; // Missing script

                if (Matches(component, componentType))
                {
                    return component;
                }
            }
            return null;
        }

        /// <summary>
        /// GameObject からコンポーネントを検索（マッチした全件、remove_all などバッチ操作用）
        /// </summary>
        /// <param name="go">検索対象の GameObject</param>
        /// <param name="componentType">コンポーネントの短縮名またはフルネーム（大文字小文字無視）</param>
        /// <returns>マッチしたコンポーネントのリスト（0件の場合は空リスト）</returns>
        public static List<Component> FindComponents(GameObject go, string componentType)
        {
            var matches = new List<Component>();
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null) continue; // Missing script

                if (Matches(component, componentType))
                {
                    matches.Add(component);
                }
            }
            return matches;
        }

        /// <summary>
        /// コンポーネントが指定された型名にマッチするか判定
        /// </summary>
        private static bool Matches(Component component, string componentType)
        {
            var type = component.GetType();
            return type.Name.Equals(componentType, StringComparison.OrdinalIgnoreCase) ||
                   type.FullName.Equals(componentType, StringComparison.OrdinalIgnoreCase);
        }
    }
}
