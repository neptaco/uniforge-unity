using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UniForge
{
    internal static class UniForgeProtocolMessages
    {
        [ThreadStatic] private static StringBuilder _responseBuilder;

        private const int MaxRetainedCapacity = 64 * 1024;

        internal static string BuildUnityRegisterRequest(
            string requestId,
            string projectId,
            string projectName,
            string gitRoot,
            List<Dictionary<string, object>> tools,
            List<string> pendingRequestIds,
            string consoleLogPath = null,
            string packageVersion = null)
        {
            var paramsBuilder = SimpleJson.Object()
                .Add("projectId", projectId)
                .Add("projectName", projectName);

            if (gitRoot != null)
            {
                paramsBuilder = paramsBuilder.Add("gitRoot", gitRoot);
            }

            if (!string.IsNullOrEmpty(consoleLogPath))
            {
                paramsBuilder = paramsBuilder.Add("consoleLogPath", consoleLogPath);
            }

            if (!string.IsNullOrEmpty(packageVersion))
            {
                paramsBuilder = paramsBuilder.Add("packageVersion", packageVersion);
            }

            paramsBuilder = paramsBuilder.AddRaw("tools", SimpleJson.Serialize(tools));

            if (pendingRequestIds != null && pendingRequestIds.Count > 0)
            {
                paramsBuilder = paramsBuilder.AddRaw("pendingRequestIds", SimpleJson.Serialize(pendingRequestIds));
            }

            return SimpleJson.Object()
                .Add("jsonrpc", "2.0")
                .Add("id", requestId)
                .Add("method", "unity.register")
                .AddRaw("params", paramsBuilder.ToString())
                .ToString();
        }

        internal static string BuildUnityToolsUpdateNotification(
            string projectId,
            List<Dictionary<string, object>> tools)
        {
            return SimpleJson.Object()
                .Add("jsonrpc", "2.0")
                .Add("method", "unity.toolsUpdate")
                .AddRaw("params", SimpleJson.Object()
                    .Add("projectId", projectId)
                    .AddRaw("tools", SimpleJson.Serialize(tools))
                    .ToString())
                .ToString();
        }

        internal static string BuildToolResponse(
            string requestId,
            bool success,
            object resultPayload,
            string error = null,
            bool pending = false)
        {
            var sb = GetResponseBuilder();
            sb.Append("{\"jsonrpc\":\"2.0\",\"id\":");
            AppendJsonString(sb, requestId);
            sb.Append(",\"result\":{\"success\":");
            sb.Append(success ? "true" : "false");
            if (pending)
            {
                sb.Append(",\"pending\":true");
            }
            sb.Append(",\"result\":");
            if (resultPayload != null) sb.Append(SimpleJson.Serialize(resultPayload));
            else sb.Append("null");
            sb.Append(",\"error\":");
            if (error != null) AppendJsonString(sb, error);
            else sb.Append("null");
            sb.Append("}}");
            return sb.ToString();
        }

        internal static string ExtractParamsJson(string fullMessage)
        {
            return ExtractJsonObjectField(fullMessage, "params");
        }

        internal static string ExtractArgsJson(string fullMessage)
        {
            return ExtractJsonObjectField(fullMessage, "args");
        }

        internal static string ExtractStringField(string json, string fieldName)
        {
            return TcpTransportClient.ExtractStringField(json, fieldName);
        }

        internal static bool TryParseUnityRegisterResponse(
            string message,
            out string requestId,
            out bool success,
            out string latestPackageVersion,
            out string minPackageVersion,
            out string latestPackageUnity,
            out string latestPackageUnityRelease)
        {
            requestId = null;
            success = false;
            latestPackageVersion = null;
            minPackageVersion = null;
            latestPackageUnity = null;
            latestPackageUnityRelease = null;

            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            var trimmedMessage = message.Trim();
            if (trimmedMessage.Length < 2 || trimmedMessage[0] != '{' || trimmedMessage[trimmedMessage.Length - 1] != '}')
            {
                return false;
            }

            try
            {
                var response = JsonUtility.FromJson<UnityRegisterResponseMessage>(trimmedMessage);
                if (response == null || string.IsNullOrEmpty(response.id) || response.result == null)
                {
                    return false;
                }

                var responseObject = SimpleJson.Parse(trimmedMessage);
                if (!responseObject.TryGetValue("result", out var resultValue)
                    || !(resultValue is Dictionary<string, object> resultObject))
                {
                    return false;
                }

                var responseSuccess = true;
                if (resultObject.TryGetValue("success", out var successValue))
                {
                    if (!(successValue is bool parsedSuccess))
                    {
                        return false;
                    }

                    responseSuccess = parsedSuccess;
                }

                requestId = response.id;
                success = responseSuccess;
                latestPackageVersion = response.result.latestPackageVersion;
                minPackageVersion = response.result.minPackageVersion;
                latestPackageUnity = response.result.latestPackageUnity;
                latestPackageUnityRelease = response.result.latestPackageUnityRelease;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static bool TryParseUnityRegisterResponse(
            string message,
            string expectedRequestId,
            out bool success,
            out string latestPackageVersion,
            out string minPackageVersion,
            out string latestPackageUnity,
            out string latestPackageUnityRelease)
        {
            success = false;
            latestPackageVersion = null;
            minPackageVersion = null;
            latestPackageUnity = null;
            latestPackageUnityRelease = null;

            if (!TryParseUnityRegisterResponse(
                    message,
                    out var responseRequestId,
                    out var parsedSuccess,
                    out var parsedLatestPackageVersion,
                    out var parsedMinPackageVersion,
                    out var parsedLatestPackageUnity,
                    out var parsedLatestPackageUnityRelease)
                || !string.Equals(responseRequestId, expectedRequestId, StringComparison.Ordinal))
            {
                return false;
            }

            success = parsedSuccess;
            latestPackageVersion = parsedLatestPackageVersion;
            minPackageVersion = parsedMinPackageVersion;
            latestPackageUnity = parsedLatestPackageUnity;
            latestPackageUnityRelease = parsedLatestPackageUnityRelease;
            return true;
        }

        private static string ExtractJsonObjectField(string json, string fieldName)
        {
            var key = $"\"{fieldName}\":";
            int keyIndex = json.IndexOf(key, StringComparison.Ordinal);
            if (keyIndex < 0) return "{}";

            int startIndex = keyIndex + key.Length;

            while (startIndex < json.Length && char.IsWhiteSpace(json[startIndex]))
                startIndex++;

            if (startIndex >= json.Length || json[startIndex] != '{') return "{}";

            // 文字列リテラル内のブレースを数えないよう inString / エスケープ状態を追跡する
            int braceCount = 0;
            int endIndex = startIndex;
            bool inString = false;
            bool escaped = false;
            for (int i = startIndex; i < json.Length; i++)
            {
                char c = json[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (inString)
                {
                    if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{') braceCount++;
                else if (c == '}') braceCount--;

                if (braceCount == 0)
                {
                    endIndex = i + 1;
                    break;
                }
            }

            return json.Substring(startIndex, endIndex - startIndex);
        }

        private static StringBuilder GetResponseBuilder()
        {
            var sb = _responseBuilder;
            if (sb == null || sb.Capacity > MaxRetainedCapacity)
            {
                _responseBuilder = sb = new StringBuilder(512);
            }
            else
            {
                sb.Clear();
            }
            return sb;
        }

        private static void AppendJsonString(StringBuilder sb, string str)
        {
            if (str.Length > SimpleJson.MaxStringLength)
            {
                throw new ArgumentException(
                    $"String length ({str.Length}) exceeds maximum allowed length ({SimpleJson.MaxStringLength})",
                    nameof(str));
            }

            sb.Append('"');
            int len = str.Length;
            for (int i = 0; i < len; i++)
            {
                char c = str[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("X4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        [Serializable]
        private class UnityRegisterResponseMessage
        {
            public string id;
            public UnityRegisterResponseResult result;
        }

        [Serializable]
        private class UnityRegisterResponseResult
        {
            public string latestPackageVersion;
            public string minPackageVersion;
            public string latestPackageUnity;
            public string latestPackageUnityRelease;
        }
    }
}
