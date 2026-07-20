using NUnit.Framework;
using UniForge.Tools.Mutations;

namespace UniForge.Tests
{
    [TestFixture]
    public class PackageUpdateHandlerTests
    {
        [Test]
        public void TryBuildUpdateUrl_WithPathAndFragment_ReplacesFragment()
        {
            const string packageId =
                "dev.crysta.uniforge@https://github.com/neptaco/uniforge-unity.git?path=Packages/dev.crysta.uniforge#abc123";

            var result = PackageUpdateHandler.TryBuildUpdateUrl(
                packageId,
                "0.12.0",
                out var updateUrl);

            Assert.IsTrue(result);
            Assert.AreEqual(
                "https://github.com/neptaco/uniforge-unity.git?path=Packages/dev.crysta.uniforge#v0.12.0",
                updateUrl);
        }

        [Test]
        public void TryBuildUpdateUrl_WithoutFragment_AppendsVersionFragment()
        {
            const string packageId =
                "dev.crysta.uniforge@https://github.com/neptaco/uniforge-unity.git?path=Packages/dev.crysta.uniforge";

            var result = PackageUpdateHandler.TryBuildUpdateUrl(
                packageId,
                "0.12.0",
                out var updateUrl);

            Assert.IsTrue(result);
            Assert.AreEqual(
                "https://github.com/neptaco/uniforge-unity.git?path=Packages/dev.crysta.uniforge#v0.12.0",
                updateUrl);
        }

        [TestCase("dev.crysta.uniforge@1.0.0")]
        [TestCase("dev.crysta.uniforge@file:../dev.crysta.uniforge")]
        [TestCase("dev.crysta.uniforge@file:///tmp/dev.crysta.uniforge")]
        [TestCase("dev.crysta.uniforge@file:///tmp/uniforge-unity.git?path=Packages/dev.crysta.uniforge")]
        [TestCase("dev.crysta.uniforge@Packages/dev.crysta.uniforge")]
        public void TryBuildUpdateUrl_WithNonGitPackageId_ReturnsFalse(string packageId)
        {
            var result = PackageUpdateHandler.TryBuildUpdateUrl(
                packageId,
                "0.12.0",
                out var updateUrl);

            Assert.IsFalse(result);
            Assert.IsNull(updateUrl);
        }

        [TestCase(null, null)]
        [TestCase("", "")]
        public void TryResolveTargetVersion_WithoutRequestedOrLatestVersion_ReturnsFalse(
            string requestedVersion,
            string latestVersion)
        {
            var result = PackageUpdateHandler.TryResolveTargetVersion(
                requestedVersion,
                latestVersion,
                out var targetVersion);

            Assert.IsFalse(result);
            Assert.IsTrue(string.IsNullOrEmpty(targetVersion));
        }

        [TestCase("0.12.0", "0.13.0", "0.12.0")]
        [TestCase(null, "0.13.0", "0.13.0")]
        public void TryResolveTargetVersion_UsesRequestedVersionBeforeLatestVersion(
            string requestedVersion,
            string latestVersion,
            string expectedVersion)
        {
            var result = PackageUpdateHandler.TryResolveTargetVersion(
                requestedVersion,
                latestVersion,
                out var targetVersion);

            Assert.IsTrue(result);
            Assert.AreEqual(expectedVersion, targetVersion);
        }
    }
}
