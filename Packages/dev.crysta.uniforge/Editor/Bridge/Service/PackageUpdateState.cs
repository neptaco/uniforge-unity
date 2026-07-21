using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace UniForge
{
    /// <summary>
    /// Package update information reported by the daemon.
    /// ScriptableSingleton keeps the state stable across domain reloads without persisting stale data to disk.
    /// </summary>
    public class PackageUpdateState : ScriptableSingleton<PackageUpdateState>
    {
        [SerializeField] private PackageUpdateStatus _status = new PackageUpdateStatus();

        public string CurrentPackageVersion => Status.CurrentPackageVersion;
        public string LatestPackageVersion => Status.LatestPackageVersion;
        public string MinPackageVersion => Status.MinPackageVersion;
        public string LatestPackageUnity => Status.LatestPackageUnity;
        public string LatestPackageUnityRelease => Status.LatestPackageUnityRelease;
        public string CurrentUnityVersion => Status.CurrentUnityVersion;
        public string RequiredUnityVersion => Status.RequiredUnityVersion;
        public bool IsUpdateAvailable => Status.IsUpdateAvailable;
        public bool IsBelowMinimumVersion => Status.IsBelowMinimumVersion;
        public bool IsUnityUpgradeRequired =>
            Status.NotificationKind == PackageUpdateNotificationKind.RequiresUnityUpgrade;

        private PackageUpdateStatus Status
        {
            get
            {
                if (_status == null)
                {
                    _status = new PackageUpdateStatus();
                }

                return _status;
            }
        }

        internal void UpdateVersions(
            string currentPackageVersion,
            string latestPackageVersion,
            string minPackageVersion,
            string latestPackageUnity,
            string latestPackageUnityRelease)
        {
            Status.UpdateVersions(
                currentPackageVersion,
                latestPackageVersion,
                minPackageVersion,
                latestPackageUnity,
                latestPackageUnityRelease,
                Application.unityVersion);
        }

        internal bool LogUpdateNotificationIfNeeded()
        {
            return Status.LogUpdateNotificationIfNeeded();
        }

        internal string GetWindowMessage()
        {
            return Status.GetWindowMessage();
        }
    }

    [Serializable]
    internal class PackageUpdateStatus
    {
        private const string NotificationSessionKeyPrefix = "UniForge.PackageUpdate.Notified.";

        [SerializeField] private string _currentPackageVersion;
        [SerializeField] private string _latestPackageVersion;
        [SerializeField] private string _minPackageVersion;
        [SerializeField] private string _latestPackageUnity;
        [SerializeField] private string _latestPackageUnityRelease;
        [SerializeField] private string _currentUnityVersion;
        [SerializeField] private UnityVersionCompatibilityResult _unityCompatibility;
        [SerializeField] private bool _isUpdateAvailable;
        [SerializeField] private bool _isBelowMinimumVersion;

        internal string CurrentPackageVersion => _currentPackageVersion;
        internal string LatestPackageVersion => _latestPackageVersion;
        internal string MinPackageVersion => _minPackageVersion;
        internal string LatestPackageUnity => _latestPackageUnity;
        internal string LatestPackageUnityRelease => _latestPackageUnityRelease;
        internal string CurrentUnityVersion => _currentUnityVersion;
        internal string RequiredUnityVersion => UnityVersionCompatibility.FormatRequirement(
            _latestPackageUnity,
            _latestPackageUnityRelease);
        internal bool IsUpdateAvailable => _isUpdateAvailable;
        internal bool IsBelowMinimumVersion => _isBelowMinimumVersion;
        internal PackageUpdateNotificationKind NotificationKind => DetermineNotificationKind(
            _isUpdateAvailable,
            _unityCompatibility);

        internal void UpdateVersions(
            string currentPackageVersion,
            string latestPackageVersion,
            string minPackageVersion,
            string latestPackageUnity,
            string latestPackageUnityRelease,
            string currentUnityVersion)
        {
            _currentPackageVersion = currentPackageVersion;
            _latestPackageVersion = latestPackageVersion;
            _minPackageVersion = minPackageVersion;
            _latestPackageUnity = latestPackageUnity;
            _latestPackageUnityRelease = latestPackageUnityRelease;
            _currentUnityVersion = currentUnityVersion;
            _unityCompatibility = UnityVersionCompatibility.Evaluate(
                currentUnityVersion,
                latestPackageUnity,
                latestPackageUnityRelease);

            _isUpdateAvailable = TryCompareSemanticVersions(
                currentPackageVersion,
                latestPackageVersion,
                out var latestComparison) && latestComparison < 0;

            _isBelowMinimumVersion = TryCompareSemanticVersions(
                currentPackageVersion,
                minPackageVersion,
                out var minimumComparison) && minimumComparison < 0;
        }

        internal bool LogUpdateNotificationIfNeeded()
        {
            var notificationKind = NotificationKind;
            if (notificationKind == PackageUpdateNotificationKind.None
                || string.IsNullOrEmpty(_latestPackageVersion))
            {
                return false;
            }

            var sessionKey = GetNotificationSessionKey(_latestPackageVersion);
            if (SessionState.GetBool(sessionKey, false))
            {
                return false;
            }

            SessionState.SetBool(sessionKey, true);
            var message = GetNotificationMessage(notificationKind, includePrefix: true);
            if (notificationKind == PackageUpdateNotificationKind.Available
                && _isBelowMinimumVersion)
            {
                Debug.LogWarning(message);
            }
            else
            {
                Debug.Log(message);
            }

            return true;
        }

        internal string GetWindowMessage()
        {
            return GetNotificationMessage(NotificationKind, includePrefix: false);
        }

        internal static PackageUpdateNotificationKind DetermineNotificationKind(
            bool isUpdateAvailable,
            UnityVersionCompatibilityResult unityCompatibility)
        {
            if (!isUpdateAvailable)
            {
                return PackageUpdateNotificationKind.None;
            }

            return unityCompatibility == UnityVersionCompatibilityResult.Incompatible
                ? PackageUpdateNotificationKind.RequiresUnityUpgrade
                : PackageUpdateNotificationKind.Available;
        }

        private string GetNotificationMessage(
            PackageUpdateNotificationKind notificationKind,
            bool includePrefix)
        {
            var prefix = includePrefix ? "[UniForge] " : string.Empty;
            if (notificationKind == PackageUpdateNotificationKind.RequiresUnityUpgrade)
            {
                return $"{prefix}Package {_latestPackageVersion} is available but requires Unity >= {RequiredUnityVersion} (current {_currentUnityVersion})";
            }

            return includePrefix
                ? $"[UniForge] Package update available: {_currentPackageVersion} -> {_latestPackageVersion} (update via Package Manager)"
                : $"Update available: v{_currentPackageVersion} -> v{_latestPackageVersion}";
        }

        internal static string GetNotificationSessionKey(string latestPackageVersion)
        {
            return NotificationSessionKeyPrefix + latestPackageVersion;
        }

        internal static bool TryCompareSemanticVersions(string left, string right, out int comparison)
        {
            comparison = 0;
            if (!TryParseSemanticVersion(left, out var leftVersion)
                || !TryParseSemanticVersion(right, out var rightVersion))
            {
                return false;
            }

            comparison = leftVersion.Major.CompareTo(rightVersion.Major);
            if (comparison != 0)
            {
                return true;
            }

            comparison = leftVersion.Minor.CompareTo(rightVersion.Minor);
            if (comparison != 0)
            {
                return true;
            }

            comparison = leftVersion.Patch.CompareTo(rightVersion.Patch);
            return true;
        }

        private static bool TryParseSemanticVersion(string value, out SemanticVersion version)
        {
            version = default;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            var parts = value.Split('.');
            if (parts.Length != 3
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
                || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
            {
                return false;
            }

            version = new SemanticVersion(major, minor, patch);
            return true;
        }

        private readonly struct SemanticVersion
        {
            internal SemanticVersion(int major, int minor, int patch)
            {
                Major = major;
                Minor = minor;
                Patch = patch;
            }

            internal int Major { get; }
            internal int Minor { get; }
            internal int Patch { get; }
        }
    }

    public enum PackageUpdateNotificationKind
    {
        None,
        Available,
        RequiresUnityUpgrade
    }

    public enum UnityVersionCompatibilityResult
    {
        Unknown,
        Compatible,
        Incompatible
    }

    internal static class UnityVersionCompatibility
    {
        internal static UnityVersionCompatibilityResult Evaluate(
            string currentUnityVersion,
            string requiredUnity,
            string requiredUnityRelease)
        {
            if (!TryParseEditorVersion(currentUnityVersion, out var current)
                || !TryParseRequirement(requiredUnity, requiredUnityRelease, out var required))
            {
                return UnityVersionCompatibilityResult.Unknown;
            }

            var comparison = current.Major.CompareTo(required.Major);
            if (comparison == 0)
            {
                comparison = current.Minor.CompareTo(required.Minor);
            }

            if (comparison == 0 && !string.IsNullOrEmpty(requiredUnityRelease))
            {
                comparison = current.Patch.CompareTo(required.Patch);
                if (comparison == 0)
                {
                    comparison = current.ChannelRank.CompareTo(required.ChannelRank);
                }

                if (comparison == 0)
                {
                    comparison = current.Increment.CompareTo(required.Increment);
                }
            }

            return comparison >= 0
                ? UnityVersionCompatibilityResult.Compatible
                : UnityVersionCompatibilityResult.Incompatible;
        }

        internal static string FormatRequirement(string requiredUnity, string requiredUnityRelease)
        {
            return string.IsNullOrEmpty(requiredUnityRelease)
                ? requiredUnity
                : $"{requiredUnity}.{requiredUnityRelease}";
        }

        private static bool TryParseRequirement(
            string requiredUnity,
            string requiredUnityRelease,
            out UnityVersion version)
        {
            version = default;
            if (!TryParseMajorMinor(requiredUnity, out var major, out var minor))
            {
                return false;
            }

            if (string.IsNullOrEmpty(requiredUnityRelease))
            {
                version = new UnityVersion(major, minor, 0, 0, 0);
                return true;
            }

            if (!TryParseRelease(
                    requiredUnityRelease,
                    out var patch,
                    out var channelRank,
                    out var increment))
            {
                return false;
            }

            version = new UnityVersion(major, minor, patch, channelRank, increment);
            return true;
        }

        private static bool TryParseEditorVersion(string value, out UnityVersion version)
        {
            version = default;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            var channelIndex = FindChannelIndex(value);
            if (channelIndex <= 0
                || !TryGetChannelRank(value[channelIndex], out var channelRank)
                || !int.TryParse(
                    value.Substring(channelIndex + 1),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var increment))
            {
                return false;
            }

            var numberParts = value.Substring(0, channelIndex).Split('.');
            if (numberParts.Length != 3
                || !TryParseNumber(numberParts[0], out var major)
                || !TryParseNumber(numberParts[1], out var minor)
                || !TryParseNumber(numberParts[2], out var patch))
            {
                return false;
            }

            version = new UnityVersion(major, minor, patch, channelRank, increment);
            return true;
        }

        private static bool TryParseMajorMinor(string value, out int major, out int minor)
        {
            major = 0;
            minor = 0;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            var parts = value.Split('.');
            return parts.Length == 2
                && TryParseNumber(parts[0], out major)
                && TryParseNumber(parts[1], out minor);
        }

        private static bool TryParseRelease(
            string value,
            out int patch,
            out int channelRank,
            out int increment)
        {
            patch = 0;
            channelRank = 0;
            increment = 0;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            var channelIndex = FindChannelIndex(value);
            return channelIndex > 0
                && TryParseNumber(value.Substring(0, channelIndex), out patch)
                && TryGetChannelRank(value[channelIndex], out channelRank)
                && int.TryParse(
                    value.Substring(channelIndex + 1),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out increment);
        }

        private static int FindChannelIndex(string value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                if (TryGetChannelRank(value[index], out _))
                {
                    return index;
                }
            }

            return -1;
        }

        private static bool TryGetChannelRank(char channel, out int rank)
        {
            switch (channel)
            {
                case 'a':
                    rank = 0;
                    return true;
                case 'b':
                    rank = 1;
                    return true;
                case 'f':
                case 'p':
                    rank = 2;
                    return true;
                default:
                    rank = 0;
                    return false;
            }
        }

        private static bool TryParseNumber(string value, out int number)
        {
            return int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out number);
        }

        private readonly struct UnityVersion
        {
            internal UnityVersion(
                int major,
                int minor,
                int patch,
                int channelRank,
                int increment)
            {
                Major = major;
                Minor = minor;
                Patch = patch;
                ChannelRank = channelRank;
                Increment = increment;
            }

            internal int Major { get; }
            internal int Minor { get; }
            internal int Patch { get; }
            internal int ChannelRank { get; }
            internal int Increment { get; }
        }
    }
}
