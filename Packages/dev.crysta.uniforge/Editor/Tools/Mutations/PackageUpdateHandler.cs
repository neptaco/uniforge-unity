using System;
using UnityEditor.PackageManager;

namespace UniForge.Tools.Mutations
{
    [Tool("package-update",
        Description = "Update the installed UniForge package to a specific or daemon-reported version",
        Title = "Package Update",
        Category = ToolCategory.Editor,
        Kind = ToolKind.Mutation,
        Destructive = true,
        Idempotent = false)]
    public class PackageUpdateHandler : MutationHandler
    {
        public class Args
        {
            [ToolParameter("Target version as a bare semantic version (for example, '0.12.0')", Required = false)]
            public string version;
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            var packageInfo = PackageInfo.FindForAssembly(typeof(PackageUpdateHandler).Assembly);
            if (packageInfo == null)
            {
                return ToolResult.Fail("failed to find package information for UniForge");
            }

            if (packageInfo.source != PackageSource.Git)
            {
                return ToolResult.Fail("package is not installed from git; update it manually");
            }

            var args = new ToolArgsParser(argsJson);
            if (!TryResolveTargetVersion(
                    args.GetString("version"),
                    PackageUpdateState.instance.LatestPackageVersion,
                    out var targetVersion))
            {
                return ToolResult.Fail(
                    "version is required when no latest package version has been received from the daemon");
            }

            if (!PackageUpdateStatus.TryCompareSemanticVersions(
                    targetVersion,
                    targetVersion,
                    out _))
            {
                return ToolResult.Fail(
                    "version must be a bare semantic version (for example, '0.12.0')");
            }

            if (!TryBuildUpdateUrl(packageInfo.packageId, targetVersion, out var updateUrl))
            {
                return ToolResult.Fail("package is not installed from git; update it manually");
            }

            if (string.Equals(packageInfo.version, targetVersion, StringComparison.Ordinal))
            {
                return ToolResult.Complete(new
                {
                    message = "Already up to date",
                    target_version = targetVersion,
                    package_id = packageInfo.packageId,
                    running = false
                }, success: true);
            }

            // This is intentionally a started acknowledgement, not a completion response.
            // Updating this package triggers resolve, recompilation, and a domain reload that
            // reinitializes the bridge itself, so delivery of an AddRequest completion response
            // cannot be guaranteed. The CLI verifies completion from packageVersion after reconnecting.
            _ = Client.Add(updateUrl);

            return ToolResult.Complete(new
            {
                message = "Package update started",
                target_version = targetVersion,
                package_id = packageInfo.packageId,
                running = true
            });
        }

        internal static bool TryResolveTargetVersion(
            string requestedVersion,
            string latestVersion,
            out string targetVersion)
        {
            targetVersion = !string.IsNullOrEmpty(requestedVersion)
                ? requestedVersion
                : latestVersion;
            return !string.IsNullOrEmpty(targetVersion);
        }

        internal static bool TryBuildUpdateUrl(
            string packageId,
            string targetVersion,
            out string updateUrl)
        {
            updateUrl = null;
            if (string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(targetVersion))
            {
                return false;
            }

            var separatorIndex = packageId.IndexOf('@');
            if (separatorIndex <= 0 || separatorIndex == packageId.Length - 1)
            {
                return false;
            }

            var gitUrl = packageId.Substring(separatorIndex + 1);
            if (!LooksLikeGitUrl(gitUrl))
            {
                return false;
            }

            var fragmentIndex = gitUrl.IndexOf('#');
            if (fragmentIndex >= 0)
            {
                gitUrl = gitUrl.Substring(0, fragmentIndex);
            }

            updateUrl = $"{gitUrl}#v{targetVersion}";
            return true;
        }

        private static bool LooksLikeGitUrl(string value)
        {
            var repositoryUrl = value;
            var fragmentIndex = repositoryUrl.IndexOf('#');
            if (fragmentIndex >= 0)
            {
                repositoryUrl = repositoryUrl.Substring(0, fragmentIndex);
            }

            var queryIndex = repositoryUrl.IndexOf('?');
            if (queryIndex >= 0)
            {
                repositoryUrl = repositoryUrl.Substring(0, queryIndex);
            }

            if (repositoryUrl.StartsWith("git://", StringComparison.OrdinalIgnoreCase)
                || repositoryUrl.StartsWith("git+https://", StringComparison.OrdinalIgnoreCase)
                || repositoryUrl.StartsWith("git+http://", StringComparison.OrdinalIgnoreCase)
                || repositoryUrl.StartsWith("git+ssh://", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var hasGitSuffix = repositoryUrl.EndsWith(".git", StringComparison.OrdinalIgnoreCase);
            return hasGitSuffix
                && (repositoryUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    || repositoryUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || repositoryUrl.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)
                    || repositoryUrl.StartsWith("git@", StringComparison.OrdinalIgnoreCase));
        }
    }
}
