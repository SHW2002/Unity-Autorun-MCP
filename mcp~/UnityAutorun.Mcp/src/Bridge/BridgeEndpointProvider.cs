using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace UnityAutorun.Mcp
{
    public sealed class BridgeEndpointProvider
    {
        private const string StateDirectoryName = "UnityAutorunTool";
        private const string StateFileName = "bridge-endpoint.json";
        public const string ProfileEnvironmentVariable = "UNITY_AUTORUN_BRIDGE_PROFILE";

        public BridgeEndpoint GetRequired()
        {
            string stateFilePath = GetStateFilePath();
            if (!File.Exists(stateFilePath))
            {
                throw new InvalidOperationException(
                    $"Unity AutoRun has not published a bridge endpoint for this project. "
                    + $"Start it from Unity: Window/Auto Run MCP Bridge/Start. Expected state file: {stateFilePath}"
                );
            }

            JsonObject state;
            try
            {
                state = JsonNode.Parse(File.ReadAllText(stateFilePath))?.AsObject();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Cannot read Unity AutoRun bridge endpoint state: {ex.Message}", ex);
            }

            string host = state?["host"]?.GetValue<string>();
            int port = state?["port"]?.GetValue<int>() ?? 0;
            int processId = state?["processId"]?.GetValue<int>() ?? 0;
            if (string.IsNullOrWhiteSpace(host) || port < 1 || port > 65535 || processId < 1)
            {
                throw new InvalidOperationException($"Unity AutoRun bridge endpoint state is invalid: {stateFilePath}");
            }

            if (!IsProcessRunning(processId))
            {
                throw new InvalidOperationException(
                    $"Unity AutoRun bridge endpoint state is stale because process {processId} is not running: {stateFilePath}"
                );
            }

            return new BridgeEndpoint(
                host,
                port,
                processId,
                state?["projectRoot"]?.GetValue<string>(),
                state?["updatedAtUtc"]?.GetValue<string>(),
                stateFilePath
            );
        }

        private static bool IsProcessRunning(int processId)
        {
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    return !process.HasExited;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        public JsonObject GetInfo()
        {
            try
            {
                BridgeEndpoint endpoint = GetRequired();
                return JsonUtil.Obj(
                    ("ok", true),
                    ("code", "ok"),
                    ("message", "Unity AutoRun bridge endpoint was provided dynamically."),
                    ("data", JsonUtil.Obj(
                        ("host", endpoint.Host),
                        ("port", endpoint.Port),
                        ("url", endpoint.Url),
                        ("processId", endpoint.ProcessId),
                        ("projectRoot", endpoint.ProjectRoot),
                        ("stateFilePath", endpoint.StateFilePath),
                        ("updatedAtUtc", endpoint.UpdatedAtUtc)
                    ))
                );
            }
            catch (Exception ex)
            {
                return JsonUtil.Obj(
                    ("ok", false),
                    ("code", "bridge_endpoint_unavailable"),
                    ("message", ex.Message),
                    ("data", new JsonObject())
                );
            }
        }

        public static void PublishForCurrentProcess(string host, int port)
        {
            string projectRoot = ResolveProjectRoot();
            string stateFilePath = GetStateFilePath();
            string directory = Path.GetDirectoryName(stateFilePath);
            Directory.CreateDirectory(directory);

            JsonObject state = JsonUtil.Obj(
                ("host", host),
                ("port", port),
                ("url", $"http://{host}:{port}/"),
                ("processId", Environment.ProcessId),
                ("projectRoot", projectRoot.Replace('\\', '/')),
                ("updatedAtUtc", DateTime.UtcNow.ToString("O"))
            );

            string temporaryPath = stateFilePath + "." + Environment.ProcessId + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, state.ToJsonString(), new UTF8Encoding(false));
                if (File.Exists(stateFilePath))
                {
                    File.Delete(stateFilePath);
                }

                File.Move(temporaryPath, stateFilePath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        public static void ClearForCurrentProcess()
        {
            try
            {
                string stateFilePath = GetStateFilePath();
                if (!File.Exists(stateFilePath))
                {
                    return;
                }

                JsonObject state = JsonNode.Parse(File.ReadAllText(stateFilePath))?.AsObject();
                if (state?["processId"]?.GetValue<int>() == Environment.ProcessId)
                {
                    File.Delete(stateFilePath);
                }
            }
            catch (Exception)
            {
                // Runtime discovery cleanup must not hide the original shutdown reason.
            }
        }

        private static string GetStateFilePath()
        {
            string profile = ValidateProfile(Environment.GetEnvironmentVariable(ProfileEnvironmentVariable));
            string directory = Path.Combine(ResolveProjectRoot(), "Library", StateDirectoryName);
            return string.IsNullOrEmpty(profile)
                ? Path.Combine(directory, StateFileName)
                : Path.Combine(directory, "profiles", profile, StateFileName);
        }

        private static string ValidateProfile(string profile)
        {
            if (string.IsNullOrEmpty(profile)) return null;
            if (profile.Length > 64)
                throw new ArgumentException(ProfileEnvironmentVariable + " must contain 1-64 ASCII letters, digits, hyphens or underscores.");
            foreach (char character in profile)
            {
                if (!((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9') || character == '-' || character == '_'))
                    throw new ArgumentException(ProfileEnvironmentVariable + " must be a simple name containing 1-64 ASCII letters, digits, hyphens or underscores; directories are not allowed.");
            }
            return profile;
        }

        private static string ResolveProjectRoot()
        {
            return ProjectPaths.ResolveProjectRootDirectory();
        }
    }

    public sealed class BridgeEndpoint
    {
        public BridgeEndpoint(
            string host,
            int port,
            int processId,
            string projectRoot,
            string updatedAtUtc,
            string stateFilePath)
        {
            Host = host;
            Port = port;
            ProcessId = processId;
            ProjectRoot = projectRoot;
            UpdatedAtUtc = updatedAtUtc;
            StateFilePath = stateFilePath;
        }

        public string Host { get; }
        public int Port { get; }
        public int ProcessId { get; }
        public string ProjectRoot { get; }
        public string UpdatedAtUtc { get; }
        public string StateFilePath { get; }
        public string BaseUrl => $"http://{Host}:{Port}";
        public string Url => BaseUrl + "/";
    }
}
