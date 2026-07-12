using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace UniForge.Tests
{
    [TestFixture]
    public class ProjectIdentifierTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            // Reset cache before each test
            ProjectIdentifier.ResetCache();
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up temp directory if created
            if (!string.IsNullOrEmpty(_tempDir) && Directory.Exists(_tempDir))
            {
                try
                {
                    Directory.Delete(_tempDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            // Reset cache after each test
            ProjectIdentifier.ResetCache();
        }

        #region GetProjectId Tests

        [Test]
        public void GetProjectId_ReturnsNonNullValue()
        {
            var projectId = ProjectIdentifier.GetProjectId();

            Assert.IsNotNull(projectId);
            Assert.IsNotEmpty(projectId);
        }

        [Test]
        public void GetProjectId_ReturnsSameValueOnSecondCall()
        {
            var firstCall = ProjectIdentifier.GetProjectId();
            var secondCall = ProjectIdentifier.GetProjectId();

            Assert.AreEqual(firstCall, secondCall);
        }

        [Test]
        public void GetProjectId_ReturnsValidPath()
        {
            var projectId = ProjectIdentifier.GetProjectId();

            // Should be an absolute path
            Assert.IsTrue(Path.IsPathRooted(projectId), "Project ID should be an absolute path");

            // Path should exist
            Assert.IsTrue(Directory.Exists(projectId), $"Project ID path should exist: {projectId}");
        }

        [Test]
        public void GetProjectId_ReturnsNormalizedPath()
        {
            var projectId = ProjectIdentifier.GetProjectId();

            // Should use forward slashes (normalized)
            Assert.IsFalse(projectId.Contains("\\"), "Path should use forward slashes");

            // Should not have trailing slash
            Assert.IsFalse(projectId.EndsWith("/"), "Path should not have trailing slash");
        }

        [Test]
        public void GetProjectId_IsParentOfAssetsFolder()
        {
            var projectId = ProjectIdentifier.GetProjectId();
            var dataPath = Application.dataPath; // This is the Assets folder

            // projectId should be the parent of Assets folder
            var expectedParent = Directory.GetParent(dataPath)?.FullName?.Replace('\\', '/').TrimEnd('/');
            Assert.AreEqual(expectedParent, projectId,
                $"Project ID should be parent of Assets folder. Got: {projectId}, Expected: {expectedParent}");
        }

        [Test]
        public void GetProjectId_AfterResetCache_StillReturnsValidPath()
        {
            var firstCall = ProjectIdentifier.GetProjectId();
            ProjectIdentifier.ResetCache();
            var afterReset = ProjectIdentifier.GetProjectId();

            Assert.IsNotNull(afterReset);
            Assert.AreEqual(firstCall, afterReset, "Should return same path after cache reset");
        }

        #endregion

        #region GetProjectName Tests

        [Test]
        public void GetProjectName_ReturnsNonNullValue()
        {
            var projectName = ProjectIdentifier.GetProjectName();

            Assert.IsNotNull(projectName);
            Assert.IsNotEmpty(projectName);
        }

        [Test]
        public void GetProjectName_ReturnsSameValueOnSecondCall()
        {
            var firstCall = ProjectIdentifier.GetProjectName();
            var secondCall = ProjectIdentifier.GetProjectName();

            Assert.AreEqual(firstCall, secondCall);
        }

        [Test]
        public void GetProjectName_DoesNotContainPathSeparators()
        {
            var projectName = ProjectIdentifier.GetProjectName();

            Assert.IsFalse(projectName.Contains("/"), "Project name should not contain forward slash");
            Assert.IsFalse(projectName.Contains("\\"), "Project name should not contain backslash");
        }

        [Test]
        public void GetProjectName_AfterResetCache_StillReturnsValidName()
        {
            var firstCall = ProjectIdentifier.GetProjectName();
            ProjectIdentifier.ResetCache();
            var afterReset = ProjectIdentifier.GetProjectName();

            Assert.IsNotNull(afterReset);
            Assert.AreEqual(firstCall, afterReset, "Should return same name after cache reset");
        }

        #endregion

        #region GetGitRoot Tests

        [Test]
        public void GetGitRoot_ReturnsValueForGitRepo()
        {
            // This test project is inside a Git repository
            var gitRoot = ProjectIdentifier.GetGitRoot();

            Assert.IsNotNull(gitRoot, "Should find Git root for this project");
            Assert.IsNotEmpty(gitRoot);
        }

        [Test]
        public void GetGitRoot_ReturnsSameValueOnSecondCall()
        {
            var firstCall = ProjectIdentifier.GetGitRoot();
            var secondCall = ProjectIdentifier.GetGitRoot();

            Assert.AreEqual(firstCall, secondCall);
        }

        [Test]
        public void GetGitRoot_ReturnsNormalizedPath()
        {
            var gitRoot = ProjectIdentifier.GetGitRoot();

            if (gitRoot != null)
            {
                Assert.IsFalse(gitRoot.Contains("\\"), "Path should use forward slashes");
                Assert.IsFalse(gitRoot.EndsWith("/"), "Path should not have trailing slash");
            }
        }

        [Test]
        public void GetGitRoot_ContainsGitDirectory()
        {
            var gitRoot = ProjectIdentifier.GetGitRoot();

            if (gitRoot != null)
            {
                var gitDir = Path.Combine(gitRoot.Replace('/', Path.DirectorySeparatorChar), ".git");
                Assert.IsTrue(
                    Directory.Exists(gitDir) || File.Exists(gitDir),
                    $"Git root should contain .git: {gitDir}"
                );
            }
        }

        [Test]
        public void GetGitRoot_IsAncestorOfProjectId()
        {
            var projectId = ProjectIdentifier.GetProjectId();
            var gitRoot = ProjectIdentifier.GetGitRoot();

            if (gitRoot != null)
            {
                Assert.IsTrue(
                    projectId.StartsWith(gitRoot),
                    $"Project ID ({projectId}) should be under Git root ({gitRoot})"
                );
            }
        }

        [Test]
        public void GetGitRoot_AfterResetCache_StillReturnsValidPath()
        {
            var firstCall = ProjectIdentifier.GetGitRoot();
            ProjectIdentifier.ResetCache();
            var afterReset = ProjectIdentifier.GetGitRoot();

            Assert.AreEqual(firstCall, afterReset, "Should return same path after cache reset");
        }

        #endregion

        #region ResetCache Tests

        [Test]
        public void ResetCache_ClearsProjectIdCache()
        {
            // Get initial value (populates cache)
            var initial = ProjectIdentifier.GetProjectId();
            Assert.IsNotNull(initial);

            // Reset cache
            ProjectIdentifier.ResetCache();

            // Get again - should still work (repopulates cache)
            var afterReset = ProjectIdentifier.GetProjectId();
            Assert.IsNotNull(afterReset);
        }

        [Test]
        public void ResetCache_ClearsProjectNameCache()
        {
            // Get initial value (populates cache)
            var initial = ProjectIdentifier.GetProjectName();
            Assert.IsNotNull(initial);

            // Reset cache
            ProjectIdentifier.ResetCache();

            // Get again - should still work (repopulates cache)
            var afterReset = ProjectIdentifier.GetProjectName();
            Assert.IsNotNull(afterReset);
        }

        [Test]
        public void ResetCache_ClearsGitRootCache()
        {
            // Get initial value (populates cache)
            var initial = ProjectIdentifier.GetGitRoot();

            // Reset cache
            ProjectIdentifier.ResetCache();

            // Get again - should still work (repopulates cache)
            var afterReset = ProjectIdentifier.GetGitRoot();
            Assert.AreEqual(initial, afterReset);
        }

        [Test]
        public void ResetCache_CanBeCalledMultipleTimes()
        {
            ProjectIdentifier.ResetCache();
            ProjectIdentifier.ResetCache();
            ProjectIdentifier.ResetCache();

            // Should not throw and should still work
            var projectId = ProjectIdentifier.GetProjectId();
            Assert.IsNotNull(projectId);
        }

        #endregion

        #region Integration Tests

        [Test]
        public void GetProjectName_MatchesExpectedProjectName()
        {
            var projectName = ProjectIdentifier.GetProjectName();
            var expectedDirectoryName = Path.GetFileName(Path.GetDirectoryName(Application.dataPath));

            // The project name should match the Unity project directory or product name.
            Assert.IsTrue(
                projectName == expectedDirectoryName ||
                projectName == Application.productName,
                $"Project name should match '{expectedDirectoryName}' or product name, got: {projectName}"
            );
        }

        #endregion

        #region Edge Case Tests

        [Test]
        public void GetProjectId_CalledFromMultipleContexts_ReturnsSameValue()
        {
            // Simulate being called from different contexts
            string id1 = null, id2 = null, id3 = null;

            id1 = ProjectIdentifier.GetProjectId();
            ProjectIdentifier.ResetCache();
            id2 = ProjectIdentifier.GetProjectId();
            ProjectIdentifier.ResetCache();
            id3 = ProjectIdentifier.GetProjectId();

            Assert.AreEqual(id1, id2, "All calls should return same project ID");
            Assert.AreEqual(id2, id3, "All calls should return same project ID");
        }

        [Test]
        public void ProjectId_And_GitRoot_AreDifferent_InMonorepo()
        {
            var projectId = ProjectIdentifier.GetProjectId();
            var gitRoot = ProjectIdentifier.GetGitRoot();

            // In this monorepo setup,
            // projectId should be the Unity project path and gitRoot should be the repo root
            if (gitRoot != null && projectId != gitRoot)
            {
                Assert.IsTrue(
                    projectId.Length > gitRoot.Length,
                    "In a monorepo, project path should be deeper than git root"
                );
            }
        }

        #endregion
    }
}
