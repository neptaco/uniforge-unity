using System;
using System.IO;
using UnityEngine;

namespace UniForge
{
    /// <summary>
    /// Determines the project ID and Git root for this Unity project.
    /// Project ID is the Unity project path (parent of Assets folder).
    /// Git root is provided separately for composite project matching.
    /// </summary>
    public static class ProjectIdentifier
    {
        private static string _cachedProjectId;
        private static string _cachedProjectName;
        private static string _cachedGitRoot;
        private static bool _gitRootSearched;

        /// <summary>
        /// Get the project ID (Unity project path - parent of Assets folder)
        /// </summary>
        public static string GetProjectId()
        {
            if (!string.IsNullOrEmpty(_cachedProjectId))
            {
                return _cachedProjectId;
            }

            // Use Unity project path (parent of Assets folder)
            var projectPath = Directory.GetParent(Application.dataPath)?.FullName;
            if (!string.IsNullOrEmpty(projectPath))
            {
                var normalizedPath = NormalizePath(projectPath);
                if (Directory.Exists(normalizedPath))
                {
                    _cachedProjectId = normalizedPath;
                    Debug.Log($"[UniForge] Project ID: {_cachedProjectId}");
                    return _cachedProjectId;
                }
                else
                {
                    Debug.LogWarning($"[UniForge] Unity project path does not exist: {normalizedPath}");
                }
            }

            // Last resort
            _cachedProjectId = NormalizePath(Application.dataPath);
            return _cachedProjectId;
        }

        /// <summary>
        /// Get the project name
        /// </summary>
        public static string GetProjectName()
        {
            if (!string.IsNullOrEmpty(_cachedProjectName))
            {
                return _cachedProjectName;
            }

            // Use directory name as project name
            var projectPath = Directory.GetParent(Application.dataPath)?.FullName;
            if (!string.IsNullOrEmpty(projectPath))
            {
                _cachedProjectName = Path.GetFileName(projectPath);
            }
            else
            {
                _cachedProjectName = Application.productName;
            }

            return _cachedProjectName;
        }

        /// <summary>
        /// Get the Git root path for this project.
        /// Returns null if not inside a Git repository.
        /// </summary>
        public static string GetGitRoot()
        {
            if (_gitRootSearched)
            {
                return _cachedGitRoot;
            }

            _gitRootSearched = true;

            var gitRoot = FindGitRoot(Application.dataPath);
            if (!string.IsNullOrEmpty(gitRoot))
            {
                var normalizedPath = NormalizePath(gitRoot);
                if (Directory.Exists(normalizedPath))
                {
                    _cachedGitRoot = normalizedPath;
                    Debug.Log($"[UniForge] Git root: {_cachedGitRoot}");
                }
                else
                {
                    Debug.LogWarning($"[UniForge] Git root path does not exist: {normalizedPath}");
                }
            }

            return _cachedGitRoot;
        }

        /// <summary>
        /// Find the Git root directory by searching up the directory tree
        /// </summary>
        private static string FindGitRoot(string startPath)
        {
            try
            {
                var currentDir = new DirectoryInfo(startPath);

                while (currentDir != null)
                {
                    var gitDir = Path.Combine(currentDir.FullName, ".git");
                    if (Directory.Exists(gitDir) || File.Exists(gitDir)) // .git can be a file for worktrees
                    {
                        return currentDir.FullName;
                    }
                    currentDir = currentDir.Parent;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UniForge] Error searching for Git root: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Normalize path for consistent comparison across platforms
        /// </summary>
        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            try
            {
                // Use Path.GetFullPath for proper normalization and validation
                // This resolves relative paths, normalizes separators, and validates path format
                path = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UniForge] Failed to normalize path '{path}': {ex.Message}");
                return path;
            }

            // Use forward slashes for consistency
            path = path.Replace('\\', '/');

            // Remove trailing slash
            path = path.TrimEnd('/');

            return path;
        }

        /// <summary>
        /// Reset cached values (useful for testing)
        /// </summary>
        public static void ResetCache()
        {
            _cachedProjectId = null;
            _cachedProjectName = null;
            _cachedGitRoot = null;
            _gitRootSearched = false;
        }
    }
}
