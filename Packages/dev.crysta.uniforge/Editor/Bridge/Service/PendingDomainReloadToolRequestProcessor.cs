using System;
using System.Collections.Generic;
using UniForge.Tools;

namespace UniForge
{
    internal static class PendingDomainReloadToolRequestProcessor
    {
        internal static void TrackPendingRequest(string requestId, string toolName, ToolResult result)
        {
            var startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            PendingDomainReloadToolRequestsStorage.instance.Add(new PendingDomainReloadToolRequest
            {
                requestId = requestId,
                toolName = toolName,
                startTime = startTime,
                timeoutMs = result.DomainReloadTimeoutMs > 0 ? result.DomainReloadTimeoutMs : 30000,
                nextPollTime = startTime + Math.Max(0, result.DomainReloadNextPollMs),
                stateJson = result.DomainReloadStateJson
            });
        }

        internal static List<string> GetPendingRequestIds()
        {
            var ids = new List<string>();
            foreach (var pending in PendingDomainReloadToolRequestsStorage.instance.Requests)
            {
                if (!string.IsNullOrEmpty(pending.requestId))
                {
                    ids.Add(pending.requestId);
                }
            }
            return ids;
        }

        internal static void ProcessPendingRequests(ToolDispatcher toolDispatcher, TcpTransportClient transport)
        {
            if (toolDispatcher == null)
            {
                return;
            }

            var storage = PendingDomainReloadToolRequestsStorage.instance;
            if (storage.Requests.Count == 0) return;

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            for (int i = storage.Requests.Count - 1; i >= 0; i--)
            {
                var pending = storage.Requests[i];

                if (pending.readyToSend)
                {
                    if (TrySendPendingResponse(storage, i, pending, transport))
                    {
                        continue;
                    }

                    pending.nextPollTime = now + 250;
                    continue;
                }

                if (pending.nextPollTime > now)
                {
                    continue;
                }

                var result = toolDispatcher.ResumeAfterDomainReload(
                    pending.toolName,
                    pending.stateJson,
                    new DomainReloadResumeContext(pending.startTime, now, pending.timeoutMs));

                if (result.WaitsForDomainReload)
                {
                    pending.stateJson = result.DomainReloadStateJson;
                    if (result.DomainReloadTimeoutMs > 0)
                    {
                        pending.timeoutMs = result.DomainReloadTimeoutMs;
                    }
                    pending.nextPollTime = now + Math.Max(0, result.DomainReloadNextPollMs);
                    continue;
                }

                MarkReadyToSend(pending, result);
                if (!TrySendPendingResponse(storage, i, pending, transport))
                {
                    pending.nextPollTime = now + 250;
                }
            }
        }

        private static void MarkReadyToSend(PendingDomainReloadToolRequest pending, ToolResult result)
        {
            pending.readyToSend = true;
            pending.finalSuccess = result.Kind != ToolResultKind.Fail && result.ResponseSuccess;
            pending.finalResultText = result.ResultText;
            pending.finalError = result.Error;
        }

        private static bool TrySendPendingResponse(
            PendingDomainReloadToolRequestsStorage storage,
            int index,
            PendingDomainReloadToolRequest pending,
            TcpTransportClient transport)
        {
            if (transport == null || !transport.IsConnected)
            {
                return false;
            }

            if (!transport.Send(
                UniForgeProtocolMessages.BuildToolResponse(
                    pending.requestId,
                    pending.finalSuccess,
                    pending.finalResultText != null ? SimpleJson.Parse(pending.finalResultText) : null,
                    pending.finalError),
                TcpTransportClient.PriorityHigh))
            {
                return false;
            }

            storage.RemoveAt(index);
            return true;
        }
    }
}
