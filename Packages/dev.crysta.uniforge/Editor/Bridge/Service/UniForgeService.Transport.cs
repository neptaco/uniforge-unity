using System;
using UnityEngine;
using UniForge.Tools;

namespace UniForge
{
    public partial class UniForgeService
    {
        public void Connect()
        {
            if (_transport != null && _transport.IsConnected)
            {
                return;
            }

            if (_transport != null)
            {
                try
                {
                    _transport.OnConnected -= OnConnected;
                    _transport.OnDisconnected -= OnDisconnected;
                    _transport.OnMessage -= OnMessage;
                    _transport.OnError -= OnError;
                    _transport.Dispose();
                }
                catch { }
                _transport = null;
            }

            _transport = new TcpTransportClient();
            _transport.OnConnected += OnConnected;
            _transport.OnDisconnected += OnDisconnected;
            _transport.OnMessage += OnMessage;
            _transport.OnError += OnError;

            _ = _transport.ConnectAsync();
        }

        public void Disconnect()
        {
            var client = _transport;
            _transport = null;

            if (client != null)
            {
                try
                {
                    client.OnConnected -= OnConnected;
                    client.OnDisconnected -= OnDisconnected;
                    client.OnMessage -= OnMessage;
                    client.OnError -= OnError;
                    client.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[UniForge] Disconnect error: {ex.Message}");
                }
            }
        }

        private void OnConnected()
        {
            Debug.Log("[UniForge] Connected to daemon");

            var tools = _toolRegistry.ToEnabledRegistrationList();

            var requestId = $"u-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var registerMsg = UniForgeProtocolMessages.BuildUnityRegisterRequest(
                requestId,
                ProjectIdentifier.GetProjectId(),
                ProjectIdentifier.GetProjectName(),
                ProjectIdentifier.GetGitRoot(),
                tools,
                PendingDomainReloadToolRequestProcessor.GetPendingRequestIds());
            _transport.Send(registerMsg);
        }

        private void OnDisconnected()
        {
            Debug.Log("[UniForge] Disconnected from daemon");
        }

        private void OnMessage(string message)
        {
            try
            {
                var baseMsg = JsonUtility.FromJson<JsonRpcMessage>(message);

                if (baseMsg.method == "daemon.executeTool" && !string.IsNullOrEmpty(baseMsg.id))
                {
                    _ = HandleExecuteToolAsync(message, baseMsg.id);
                }
                else if (baseMsg.method == "daemon.ping")
                {
                    SendPong();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UniForge] Failed to handle message: {ex}");
            }
        }

        private void OnError(string error)
        {
            // Connection errors are expected when daemon is not running.
        }

        private async Awaitable HandleExecuteToolAsync(string message, string jsonRpcId)
        {
            var paramsJson = UniForgeProtocolMessages.ExtractParamsJson(message);
            var toolName = UniForgeProtocolMessages.ExtractStringField(paramsJson, "tool");
            var argsJson = UniForgeProtocolMessages.ExtractArgsJson(paramsJson);

            object resultPayload = null;
            bool success = true;
            string error = null;

            try
            {
                var result = await _toolDispatcher.DispatchAsync(toolName, argsJson);

                if (result.WaitsForDomainReload)
                {
                    HandleWaitForDomainReload(jsonRpcId, toolName, result);
                    return;
                }

                success = result.Kind != ToolResultKind.Fail && result.ResponseSuccess;
                resultPayload = result.ResultPayload;
                error = result.Error;
            }
            catch (Exception ex)
            {
                success = false;
                error = ex.Message;
                Debug.LogError($"[UniForge] Tool execution error: {ex}");
            }

            _transport?.Send(
                UniForgeProtocolMessages.BuildToolResponse(jsonRpcId, success, resultPayload, error),
                TcpTransportClient.PriorityHigh);
        }

        private void HandleWaitForDomainReload(string jsonRpcId, string toolName, ToolResult result)
        {
            PendingDomainReloadToolRequestProcessor.TrackPendingRequest(jsonRpcId, toolName, result);

            _transport?.Send(
                UniForgeProtocolMessages.BuildToolResponse(
                    jsonRpcId,
                    true,
                    result.ResultPayload,
                    null,
                    pending: true),
                TcpTransportClient.PriorityHigh);
        }

        private void SendPong()
        {
            _transport?.Send(TcpTransportClient.PongMessage);
        }

        [Serializable]
        private class JsonRpcMessage
        {
            public string jsonrpc;
            public string id;
            public string method;
        }
    }
}
