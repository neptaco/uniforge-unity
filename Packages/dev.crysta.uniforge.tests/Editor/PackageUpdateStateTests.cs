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
            UpdateStatus(currentVersion, latestVersion, null);

            Assert.AreEqual(currentVersion, _status.CurrentPackageVersion);
            Assert.AreEqual(latestVersion, _status.LatestPackageVersion);
            Assert.AreEqual(expectedUpdateAvailable, _status.IsUpdateAvailable);
        }

        [Test]
        public void UpdateVersions_WithUnityRequirement_RetainsRequirementFields()
        {
            UpdateStatus(
                "0.11.0",
                "0.12.0",
                null,
                "6000.2",
                "0f1",
                "6000.0.70f1");

            Assert.AreEqual("6000.2", _status.LatestPackageUnity);
            Assert.AreEqual("0f1", _status.LatestPackageUnityRelease);
            Assert.AreEqual("6000.2.0f1", _status.RequiredUnityVersion);
            Assert.AreEqual("6000.0.70f1", _status.CurrentUnityVersion);
        }

        [TestCase(
            "6000.2.0f1",
            "6000.2",
            "0f1",
            UnityVersionCompatibilityResult.Compatible)]
        [TestCase(
            "6000.2.99p9",
            "6000.2",
            null,
            UnityVersionCompatibilityResult.Compatible)]
        [TestCase(
            "6000.1.99f1",
            "6000.2",
            null,
            UnityVersionCompatibilityResult.Incompatible)]
        [TestCase(
            "6000.2.0f1",
            "6000.2",
            "0f2",
            UnityVersionCompatibilityResult.Incompatible)]
        [TestCase(
            "6000.2.1f1",
            "6000.2",
            "0f99",
            UnityVersionCompatibilityResult.Compatible)]
        [TestCase(
            "6000.2.0b99",
            "6000.2",
            "0f1",
            UnityVersionCompatibilityResult.Incompatible)]
        [TestCase(
            "6000.2.0p1",
            "6000.2",
            "0f1",
            UnityVersionCompatibilityResult.Compatible)]
        [TestCase(
            "invalid",
            "6000.2",
            "0f1",
            UnityVersionCompatibilityResult.Unknown)]
        [TestCase(
            "6000.2.0f1",
            "invalid",
            "0f1",
            UnityVersionCompatibilityResult.Unknown)]
        [TestCase(
            "6000.2.0f1",
            "6000.2",
            "invalid",
            UnityVersionCompatibilityResult.Unknown)]
        public void EvaluateUnityCompatibility_ReturnsExpectedResult(
            string currentUnityVersion,
            string requiredUnity,
            string requiredUnityRelease,
            UnityVersionCompatibilityResult expected)
        {
            var result = UnityVersionCompatibility.Evaluate(
                currentUnityVersion,
                requiredUnity,
                requiredUnityRelease);

            Assert.AreEqual(expected, result);
        }

        [TestCase(
            false,
            UnityVersionCompatibilityResult.Incompatible,
            PackageUpdateNotificationKind.None)]
        [TestCase(
            true,
            UnityVersionCompatibilityResult.Compatible,
            PackageUpdateNotificationKind.Available)]
        [TestCase(
            true,
            UnityVersionCompatibilityResult.Unknown,
            PackageUpdateNotificationKind.Available)]
        [TestCase(
            true,
            UnityVersionCompatibilityResult.Incompatible,
            PackageUpdateNotificationKind.RequiresUnityUpgrade)]
        public void DetermineNotificationKind_ReturnsExpectedKind(
            bool isUpdateAvailable,
            UnityVersionCompatibilityResult unityCompatibility,
            PackageUpdateNotificationKind expected)
        {
            var result = PackageUpdateStatus.DetermineNotificationKind(
                isUpdateAvailable,
                unityCompatibility);

            Assert.AreEqual(expected, result);
        }

        [Test]
        public void LogUpdateNotificationIfNeeded_ForSameLatestVersion_LogsOnlyOnce()
        {
            UpdateStatus(
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
        public void LogUpdateNotificationIfNeeded_WhenUnityIsIncompatible_LogsRequirementOnce()
        {
            UpdateStatus(
                CurrentNotificationTestVersion,
                LatestNotificationTestVersion,
                null,
                "6000.2",
                "0f1",
                "6000.0.70f1");
            LogAssert.Expect(
                LogType.Log,
                "[UniForge] Package 987.654.321 is available but requires Unity >= 6000.2.0f1 (current 6000.0.70f1)");

            Assert.AreEqual(
                PackageUpdateNotificationKind.RequiresUnityUpgrade,
                _status.NotificationKind);
            Assert.AreEqual(
                "Package 987.654.321 is available but requires Unity >= 6000.2.0f1 (current 6000.0.70f1)",
                _status.GetWindowMessage());
            Assert.IsTrue(_status.LogUpdateNotificationIfNeeded());
            Assert.IsFalse(_status.LogUpdateNotificationIfNeeded());
        }

        [Test]
        public void LogUpdateNotificationIfNeeded_WhenCurrentIsBelowMinimum_LogsWarning()
        {
            UpdateStatus(
                CurrentNotificationTestVersion,
                LatestNotificationTestVersion,
                "987.654.321");
            LogAssert.Expect(
                LogType.Warning,
                "[UniForge] Package update available: 987.654.320 -> 987.654.321 (update via Package Manager)");

            Assert.IsTrue(_status.IsBelowMinimumVersion);
            Assert.IsTrue(_status.LogUpdateNotificationIfNeeded());
        }

        private void UpdateStatus(
            string currentPackageVersion,
            string latestPackageVersion,
            string minPackageVersion,
            string latestPackageUnity = null,
            string latestPackageUnityRelease = null,
            string currentUnityVersion = "6000.0.70f1")
        {
            _status.UpdateVersions(
                currentPackageVersion,
                latestPackageVersion,
                minPackageVersion,
                latestPackageUnity,
                latestPackageUnityRelease,
                currentUnityVersion);
        }
    }
}
