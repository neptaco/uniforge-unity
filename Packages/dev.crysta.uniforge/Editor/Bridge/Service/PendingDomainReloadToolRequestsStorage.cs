using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UniForge
{
    /// <summary>
    /// Pending tool request data that survives a domain reload.
    /// </summary>
    [Serializable]
    public class PendingDomainReloadToolRequest
    {
        public string requestId;
        public string toolName;
        public long startTime;
        public long timeoutMs;
        public long nextPollTime;
        public string stateJson;
        public bool readyToSend;
        public bool finalSuccess;
        public string finalResultText;
        public string finalError;
    }

    /// <summary>
    /// Store for pending domain reload tool requests.
    /// </summary>
    [Serializable]
    public class PendingDomainReloadToolRequestsStore
    {
        [SerializeField]
        private List<PendingDomainReloadToolRequest> _requests = new List<PendingDomainReloadToolRequest>();

        public List<PendingDomainReloadToolRequest> Requests => _requests;

        public void Add(PendingDomainReloadToolRequest request)
        {
            _requests.Add(request);
        }

        public void RemoveAt(int index)
        {
            _requests.RemoveAt(index);
        }

        public void Clear()
        {
            _requests.Clear();
        }
    }

    [Serializable]
    internal class PendingDomainReloadToolRequestsSnapshot
    {
        public List<PendingDomainReloadToolRequest> requests = new List<PendingDomainReloadToolRequest>();
    }

    /// <summary>
    /// Singleton storage for pending tool requests that will resume after a domain reload.
    /// </summary>
    public class PendingDomainReloadToolRequestsStorage : ScriptableSingleton<PendingDomainReloadToolRequestsStorage>
    {
        [SerializeField]
        private PendingDomainReloadToolRequestsStore _store = new PendingDomainReloadToolRequestsStore();

        public PendingDomainReloadToolRequestsStore Store => _store;
        public List<PendingDomainReloadToolRequest> Requests => _store.Requests;
        public void Add(PendingDomainReloadToolRequest request) => _store.Add(request);
        public void RemoveAt(int index) => _store.RemoveAt(index);
        public void Clear() => _store.Clear();

        internal PendingDomainReloadToolRequestsSnapshot CaptureState()
        {
            var snapshot = new PendingDomainReloadToolRequestsSnapshot();
            foreach (var request in _store.Requests)
            {
                snapshot.requests.Add(CloneRequest(request));
            }

            return snapshot;
        }

        internal void RestoreState(PendingDomainReloadToolRequestsSnapshot snapshot)
        {
            _store.Clear();
            if (snapshot?.requests == null)
            {
                return;
            }

            foreach (var request in snapshot.requests)
            {
                _store.Add(CloneRequest(request));
            }
        }

        private static PendingDomainReloadToolRequest CloneRequest(PendingDomainReloadToolRequest request)
        {
            if (request == null)
            {
                return null;
            }

            return new PendingDomainReloadToolRequest
            {
                requestId = request.requestId,
                toolName = request.toolName,
                startTime = request.startTime,
                timeoutMs = request.timeoutMs,
                nextPollTime = request.nextPollTime,
                stateJson = request.stateJson,
                readyToSend = request.readyToSend,
                finalSuccess = request.finalSuccess,
                finalResultText = request.finalResultText,
                finalError = request.finalError
            };
        }
    }
}
