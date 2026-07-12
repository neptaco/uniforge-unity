using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace UniForge
{
    internal sealed class DaemonConnectionInfo
    {
        public string transport;
        public string endpoint;
        public string host;
        public int? port;
    }

    internal static class DaemonBootstrap
    {
        private static string _cachedCliPath;

        internal static DaemonConnectionInfo ReadConnectionInfo()
        {
            var path = GetDaemonJsonPath();
            if (!File.Exists(path)) return null;

            try
            {
                var content = File.ReadAllText(path);
                var data = SimpleJson.Parse(content);
                return ParseConnectionInfo(data);
            }
            catch
            {
                return null;
            }
        }

        internal static async Task<bool> TryStartAsync()
        {
            var cliPath = ResolveCliPath();
            if (cliPath == null)
            {
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = cliPath,
                    Arguments = "daemon start",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (var proc = Process.Start(psi))
                {
                    proc.WaitForExit(10000);
                }
            }
            catch
            {
                return false;
            }

            var daemonJsonPath = GetDaemonJsonPath();
            const int maxWaitMs = 10000;
            const int pollIntervalMs = 200;
            var waited = 0;
            while (waited < maxWaitMs)
            {
                if (File.Exists(daemonJsonPath))
                {
                    return true;
                }

                await Task.Delay(pollIntervalMs);
                waited += pollIntervalMs;
            }

            return false;
        }

        private static DaemonConnectionInfo ParseConnectionInfo(Dictionary<string, object> data)
        {
            if (data == null) return null;

            var endpoint = GetStringValue(data, "endpoint");
            if (!string.IsNullOrEmpty(endpoint))
            {
                var transport = GetStringValue(data, "transport");
                if (transport != "namedPipe" && transport != "unix")
                {
                    transport = endpoint.StartsWith(@"\\.\pipe\", StringComparison.OrdinalIgnoreCase)
                        ? "namedPipe"
                        : "unix";
                }

                return new DaemonConnectionInfo
                {
                    transport = transport,
                    endpoint = endpoint,
                };
            }

            var port = GetIntValue(data, "port");
            if (port.HasValue)
            {
                return new DaemonConnectionInfo
                {
                    transport = "tcp",
                    host = GetStringValue(data, "host") ?? "127.0.0.1",
                    port = port,
                };
            }

            return null;
        }

        private static string GetStringValue(Dictionary<string, object> data, string key)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            return value as string;
        }

        private static int? GetIntValue(Dictionary<string, object> data, string key)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            switch (value)
            {
                case int intValue:
                    return intValue;
                case long longValue:
                    return (int)longValue;
                case double doubleValue:
                    return (int)doubleValue;
                default:
                    return null;
            }
        }

        private static string GetDaemonJsonPath()
        {
            return Path.Combine(GetRuntimeDir(), "daemon.json");
        }

        private static string GetRuntimeDir()
        {
#if UNITY_EDITOR_LINUX
            var xdgRuntime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (!string.IsNullOrEmpty(xdgRuntime))
                return Path.Combine(xdgRuntime, "uniforge");
            var uid = Environment.GetEnvironmentVariable("UID") ?? "0";
            return Path.Combine(Path.GetTempPath(), "uniforge-" + uid);
#elif UNITY_EDITOR_WIN
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "uniforge");
#else // macOS
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Caches", "uniforge");
#endif
        }

        private static string ResolveCliPath()
        {
            if (!string.IsNullOrEmpty(_cachedCliPath)) return _cachedCliPath;

            var envConfiguredPath = Environment.GetEnvironmentVariable("UNIFORGE_BIN");
            if (!string.IsNullOrWhiteSpace(envConfiguredPath))
            {
                _cachedCliPath = envConfiguredPath.Trim();
                return _cachedCliPath;
            }

            foreach (var candidate in new[] { "uniforge" })
            {
                var resolvedPath = ResolveCommandPath(candidate);
                if (!string.IsNullOrEmpty(resolvedPath))
                {
                    _cachedCliPath = resolvedPath;
                    return _cachedCliPath;
                }
            }

            return null;
        }

        private static string ResolveCommandPath(string commandName)
        {
            try
            {
                var psi = BuildCommandLookupProcessStartInfo(commandName);
                using (var proc = Process.Start(psi))
                {
                    var output = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit();
                    if (proc.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        using (var reader = new StringReader(output))
                        {
                            return reader.ReadLine();
                        }
                    }
                }
            }
            catch
            {
                // Failed to resolve CLI - will fall back to waiting for daemon.
            }

            return null;
        }

        private static ProcessStartInfo BuildCommandLookupProcessStartInfo(string commandName)
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c where {commandName}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
            }

            return new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"-l -c \"command -v {commandName}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
        }
    }
}
