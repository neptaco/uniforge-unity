using System;
using System.Collections.Generic;
using System.Text;

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
            List<string> pendingRequestIds)
        {
            var paramsBuilder = SimpleJson.Object()
                .Add("projectId", projectId)
                .Add("projectName", projectName);

            if (gitRoot != null)
            {
                paramsBuilder = paramsBuilder.Add("gitRoot", gitRoot);
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

        private static string ExtractJsonObjectField(string json, string fieldName)
        {
            var key = $"\"{fieldName}\":";
            int keyIndex = json.IndexOf(key, StringComparison.Ordinal);
            if (keyIndex < 0) return "{}";

            int startIndex = keyIndex + key.Length;

            while (startIndex < json.Length && char.IsWhiteSpace(json[startIndex]))
                startIndex++;

            if (startIndex >= json.Length || json[startIndex] != '{') return "{}";

            int braceCount = 0;
            int endIndex = startIndex;
            for (int i = startIndex; i < json.Length; i++)
            {
                char c = json[i];
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
    }
}
