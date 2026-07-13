using UnityEngine;

namespace UniForge.Tools
{
    /// <summary>
    /// コンポーネントの enabled 状態を読み書きする共通ユーティリティ。
    /// enabled プロパティは Component 基底には無く型ごとに定義されているため、
    /// 対応型（Behaviour / Renderer / Collider / Cloth / LODGroup）をここに集約し、
    /// 読み取り側と書き込み側で対応範囲が食い違わないようにする。
    /// </summary>
    public static class ComponentEnabledUtility
    {
        /// <summary>
        /// コンポーネントの enabled 状態を取得する
        /// </summary>
        /// <param name="component">対象コンポーネント</param>
        /// <param name="enabled">取得した enabled 状態</param>
        /// <returns>enabled プロパティを持つ型であれば true</returns>
        public static bool TryGetEnabled(Component component, out bool enabled)
        {
            switch (component)
            {
                case Behaviour behaviour:
                    enabled = behaviour.enabled;
                    return true;
                case Renderer renderer:
                    enabled = renderer.enabled;
                    return true;
                case Collider collider:
                    enabled = collider.enabled;
                    return true;
                case Cloth cloth:
                    enabled = cloth.enabled;
                    return true;
                case LODGroup lodGroup:
                    enabled = lodGroup.enabled;
                    return true;
                default:
                    enabled = false;
                    return false;
            }
        }

        /// <summary>
        /// コンポーネントの enabled 状態を設定する
        /// </summary>
        /// <param name="component">対象コンポーネント</param>
        /// <param name="enabled">設定する enabled 状態</param>
        /// <returns>enabled プロパティを持つ型であれば true（設定を実行）</returns>
        public static bool TrySetEnabled(Component component, bool enabled)
        {
            switch (component)
            {
                case Behaviour behaviour:
                    behaviour.enabled = enabled;
                    return true;
                case Renderer renderer:
                    renderer.enabled = enabled;
                    return true;
                case Collider collider:
                    collider.enabled = enabled;
                    return true;
                case Cloth cloth:
                    cloth.enabled = enabled;
                    return true;
                case LODGroup lodGroup:
                    lodGroup.enabled = enabled;
                    return true;
                default:
                    return false;
            }
        }
    }
}
