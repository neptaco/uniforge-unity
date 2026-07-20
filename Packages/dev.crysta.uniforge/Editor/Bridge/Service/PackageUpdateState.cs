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
        public bool IsUpdateAvailable => Status.IsUpdateAvailable;
        public bool IsBelowMinimumVersion => Status.IsBelowMinimumVersion;

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
            string minPackageVersion)
        {
            Status.UpdateVersions(currentPackageVersion, latestPackageVersion, minPackageVersion);
        }

        internal bool LogUpdateNotificationIfNeeded()
        {
            return Status.LogUpdateNotificationIfNeeded();
        }
    }

    [Serializable]
    internal class PackageUpdateStatus
    {
        private const string NotificationSessionKeyPrefix = "UniForge.PackageUpdate.Notified.";

        [SerializeField] private string _currentPackageVersion;
        [SerializeField] private string _latestPackageVersion;
        [SerializeField] private string _minPackageVersion;
        [SerializeField] private bool _isUpdateAvailable;
        [SerializeField] private bool _isBelowMinimumVersion;

        internal string CurrentPackageVersion => _currentPackageVersion;
        internal string LatestPackageVersion => _latestPackageVersion;
        internal string MinPackageVersion => _minPackageVersion;
        internal bool IsUpdateAvailable => _isUpdateAvailable;
        internal bool IsBelowMinimumVersion => _isBelowMinimumVersion;

        internal void UpdateVersions(
            string currentPackageVersion,
            string latestPackageVersion,
            string minPackageVersion)
        {
            _currentPackageVersion = currentPackageVersion;
            _latestPackageVersion = latestPackageVersion;
            _minPackageVersion = minPackageVersion;

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
            if (!_isUpdateAvailable || string.IsNullOrEmpty(_latestPackageVersion))
            {
                return false;
            }

            var sessionKey = GetNotificationSessionKey(_latestPackageVersion);
            if (SessionState.GetBool(sessionKey, false))
            {
                return false;
            }

            SessionState.SetBool(sessionKey, true);
            var message = $"[UniForge] Package update available: {_currentPackageVersion} -> {_latestPackageVersion} (update via Package Manager)";
            if (_isBelowMinimumVersion)
            {
                Debug.LogWarning(message);
            }
            else
            {
                Debug.Log(message);
            }

            return true;
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
}
