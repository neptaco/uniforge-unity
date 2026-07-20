using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace UniForge.Tests
{
    [TestFixture]
    public class PackageUpdateStateTests
    {
        private const string CurrentNotificationTestVersion = "987.654.320";
        private const string LatestNotificationTestVersion = "987.654.321";
        private PackageUpdateStatus _status;

        [SetUp]
        public void SetUp()
        {
            SessionState.EraseBool(
                PackageUpdateStatus.GetNotificationSessionKey(LatestNotificationTestVersion));
            _status = new PackageUpdateStatus();
        }

        [TearDown]
        public void TearDown()
        {
            SessionState.EraseBool(
                PackageUpdateStatus.GetNotificationSessionKey(LatestNotificationTestVersion));
            _status = null;
        }

        [TestCase("0.11.9", "0.12.0", -1)]
        [TestCase("0.12.0", "0.12.0", 0)]
        [TestCase("1.0.0", "0.12.0", 1)]
        public void TryCompareSemanticVersions_WithValidVersions_ReturnsComparison(
            string left,
            string right,
            int expectedComparison)
        {
            var parsed = PackageUpdateStatus.TryCompareSemanticVersions(left, right, out var comparison);

            Assert.IsTrue(parsed);
            Assert.AreEqual(expectedComparison, comparison);
        }

        [TestCase("0.11", "0.12.0")]
        [TestCase("0.11.0-preview", "0.12.0")]
        [TestCase("invalid", "0.12.0")]
        [TestCase("0.11.0", "invalid")]
        public void TryCompareSemanticVersions_WithUnsupportedVersion_ReturnsFalse(string left, string right)
        {
            var parsed = PackageUpdateStatus.TryCompareSemanticVersions(left, right, out var comparison);

            Assert.IsFalse(parsed);
            Assert.AreEqual(0, comparison);
        }

        [TestCase("0.11.0", "0.12.0", true)]
        [TestCase("0.12.0", "0.12.0", false)]
        [TestCase("0.13.0", "0.12.0", false)]
        [TestCase("invalid", "0.12.0", false)]
        [TestCase("0.11.0", "invalid", false)]
        public void UpdateVersions_DeterminesWhetherUpdateIsAvailable(
            string currentVersion,
            string latestVersion,
            bool expectedUpdateAvailable)
        {
            _status.UpdateVersions(currentVersion, latestVersion, null);

            Assert.AreEqual(currentVersion, _status.CurrentPackageVersion);
            Assert.AreEqual(latestVersion, _status.LatestPackageVersion);
            Assert.AreEqual(expectedUpdateAvailable, _status.IsUpdateAvailable);
        }

        [Test]
        public void LogUpdateNotificationIfNeeded_ForSameLatestVersion_LogsOnlyOnce()
        {
            _status.UpdateVersions(
                CurrentNotificationTestVersion,
                LatestNotificationTestVersion,
                null);
            LogAssert.Expect(
                LogType.Log,
                "[UniForge] Package update available: 987.654.320 -> 987.654.321 (update via Package Manager)");

            Assert.IsTrue(_status.LogUpdateNotificationIfNeeded());
            Assert.IsFalse(_status.LogUpdateNotificationIfNeeded());
        }

        [Test]
        public void LogUpdateNotificationIfNeeded_WhenCurrentIsBelowMinimum_LogsWarning()
        {
            _status.UpdateVersions(
                CurrentNotificationTestVersion,
                LatestNotificationTestVersion,
                "987.654.321");
            LogAssert.Expect(
                LogType.Warning,
                "[UniForge] Package update available: 987.654.320 -> 987.654.321 (update via Package Manager)");

            Assert.IsTrue(_status.IsBelowMinimumVersion);
            Assert.IsTrue(_status.LogUpdateNotificationIfNeeded());
        }
    }
}
