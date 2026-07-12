using System;
using UnityEditor;
using UnityEngine;

namespace UniForge
{
    /// <summary>
    /// Tracks the latest Unity domain reload timestamp.
    /// </summary>
    [Serializable]
    public class DomainReloadTrackerStore
    {
        [SerializeField]
        private long _lastDomainReloadTime;

        public long LastDomainReloadTime => _lastDomainReloadTime;

        public void MarkDomainReload()
        {
            _lastDomainReloadTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    /// <summary>
    /// Singleton wrapper for domain reload tracking.
    /// </summary>
    public class DomainReloadTracker : ScriptableSingleton<DomainReloadTracker>
    {
        [SerializeField]
        private DomainReloadTrackerStore _store = new DomainReloadTrackerStore();

        public DomainReloadTrackerStore Store => _store;
        public long LastDomainReloadTime => _store.LastDomainReloadTime;
        public void MarkDomainReload() => _store.MarkDomainReload();
    }
}
