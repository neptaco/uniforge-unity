using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// バッチ操作用のターゲット指定
    /// </summary>
    public class Target
    {
        public string path;
        public int? instance_id;
        public string scene;
    }

    /// <summary>
    /// バッチ解決の結果
    /// </summary>
    public class BatchResolveResult
    {
        public readonly GameObjectResolver.Result[] Results;
        public readonly List<(int index, GameObject go)> ResolvedObjects;

        public BatchResolveResult(GameObjectResolver.Result[] results)
        {
            Results = results;
            ResolvedObjects = new List<(int, GameObject)>();
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i].Success)
                {
                    ResolvedObjects.Add((i, results[i].GameObject));
                }
            }
        }

        public int SuccessCount => ResolvedObjects.Count;
        public int FailCount => Results.Length - SuccessCount;
        public bool HasAnySuccess => SuccessCount > 0;
        public bool AllSucceeded => SuccessCount == Results.Length;
    }

    /// <summary>
    /// path または instance_id から GameObject を解決する共通ユーティリティ
    /// </summary>
    public static class GameObjectResolver
    {
        /// <summary>
        /// 解決結果
        /// </summary>
        public readonly struct Result
        {
            public readonly GameObject GameObject;
            public readonly string Error;
            public readonly bool Success;

            private Result(GameObject go, string error)
            {
                GameObject = go;
                Error = error;
                Success = go != null;
            }

            public static Result Ok(GameObject go) => new Result(go, null);
            public static Result Fail(string error) => new Result(null, error);
        }

        /// <summary>
        /// path または instance_id から GameObject を解決
        /// </summary>
        /// <param name="path">ヒエラルキーパス (例: "Canvas/Panel")</param>
        /// <param name="instanceId">InstanceID</param>
        /// <param name="sceneName">シーン名またはパス (path 検索時のみ使用)</param>
        /// <returns>解決結果</returns>
        public static Result Resolve(string path, int? instanceId, string sceneName = null)
        {
            // instance_id 優先
            if (instanceId.HasValue)
            {
                var obj = EditorUtility.InstanceIDToObject(instanceId.Value);
                var go = obj as GameObject;
                if (go == null)
                {
                    return Result.Fail($"GameObject not found with instance_id: {instanceId.Value}");
                }
                return Result.Ok(go);
            }

            // path が指定されていない場合
            if (string.IsNullOrEmpty(path))
            {
                return Result.Fail("Either 'path' or 'instance_id' is required");
            }

            // シーン取得
            Scene scene;
            if (!string.IsNullOrEmpty(sceneName))
            {
                scene = SceneManager.GetSceneByName(sceneName);
                if (!scene.IsValid())
                {
                    scene = SceneManager.GetSceneByPath(sceneName);
                }
                if (!scene.IsValid())
                {
                    return Result.Fail($"Scene not found: {sceneName}");
                }
            }
            else
            {
                scene = SceneManager.GetActiveScene();
            }

            if (!scene.isLoaded)
            {
                return Result.Fail($"Scene is not loaded: {scene.name}");
            }

            // パスから検索
            var target = FindByPath(scene, path);
            if (target == null)
            {
                return Result.Fail($"GameObject not found at path: {path}");
            }

            return Result.Ok(target);
        }

        /// <summary>
        /// ToolArgsParser から解決
        /// </summary>
        public static Result ResolveFromArgs(ToolArgsParser args)
        {
            var path = args.GetString("path");
            var instanceId = args.HasKey("instance_id") ? (int?)args.GetInt("instance_id") : null;
            var sceneName = args.GetString("scene");

            return Resolve(path, instanceId, sceneName);
        }

        /// <summary>
        /// Target オブジェクトから解決
        /// </summary>
        public static Result ResolveFromTarget(Target target)
        {
            if (target == null)
            {
                return Result.Fail("Target is null");
            }
            return Resolve(target.path, target.instance_id, target.scene);
        }

        /// <summary>
        /// 複数ターゲットを一括解決
        /// </summary>
        /// <param name="targets">ターゲット配列</param>
        /// <returns>バッチ解決結果</returns>
        public static BatchResolveResult ResolveBatch(Target[] targets)
        {
            if (targets == null || targets.Length == 0)
            {
                return new BatchResolveResult(new Result[0]);
            }

            var results = new Result[targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                results[i] = ResolveFromTarget(targets[i]);
            }

            return new BatchResolveResult(results);
        }

        /// <summary>
        /// パスから GameObject を検索（非アクティブオブジェクトも検索可能）
        /// </summary>
        public static GameObject FindByPath(Scene scene, string path)
        {
            var parts = path.Split('/');
            if (parts.Length == 0) return null;

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == parts[0])
                {
                    if (parts.Length == 1)
                    {
                        return root;
                    }
                    return TraversePath(root.transform, parts, 1);
                }
            }

            return null;
        }

        /// <summary>
        /// ヒエラルキーパスを構築
        /// </summary>
        public static string GetHierarchyPath(GameObject go)
        {
            var pathBuilder = new System.Text.StringBuilder();
            var current = go.transform;
            while (current != null)
            {
                if (pathBuilder.Length > 0)
                {
                    pathBuilder.Insert(0, "/");
                }
                pathBuilder.Insert(0, current.name);
                current = current.parent;
            }
            return pathBuilder.ToString();
        }

        private static GameObject TraversePath(Transform parent, string[] parts, int index)
        {
            if (index >= parts.Length)
            {
                return parent.gameObject;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == parts[index])
                {
                    if (index == parts.Length - 1)
                    {
                        return child.gameObject;
                    }
                    return TraversePath(child, parts, index + 1);
                }
            }

            return null;
        }

        #region Vector3 Validation Helpers

        /// <summary>
        /// float配列をVector3に変換（NaN/Infinityチェック付き）
        /// </summary>
        /// <param name="array">float配列 (最低3要素)</param>
        /// <param name="paramName">パラメータ名 (エラーメッセージ用)</param>
        /// <param name="result">変換結果</param>
        /// <param name="error">エラーメッセージ</param>
        /// <returns>成功した場合true</returns>
        public static bool TryParseVector3(float[] array, string paramName, out Vector3 result, out string error)
        {
            result = Vector3.zero;
            error = null;

            if (array == null)
            {
                return false; // null は有効（オプションパラメータ）
            }

            if (array.Length < 3)
            {
                error = $"'{paramName}' must have at least 3 elements [x, y, z]";
                return false;
            }

            for (int i = 0; i < 3; i++)
            {
                if (float.IsNaN(array[i]) || float.IsInfinity(array[i]))
                {
                    error = $"'{paramName}' contains invalid value (NaN or Infinity) at index {i}";
                    return false;
                }
            }

            result = new Vector3(array[0], array[1], array[2]);
            return true;
        }

        /// <summary>
        /// オプションのfloat配列をVector3に変換（nullの場合はスキップ）
        /// </summary>
        public static bool TryParseOptionalVector3(float[] array, string paramName, out Vector3? result, out string error)
        {
            result = null;
            error = null;

            if (array == null)
            {
                return true; // null は有効
            }

            if (TryParseVector3(array, paramName, out var vec, out error))
            {
                result = vec;
                return true;
            }

            return false;
        }

        #endregion
    }
}
