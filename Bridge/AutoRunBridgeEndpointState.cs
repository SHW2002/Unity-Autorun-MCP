using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class AutoRunBridgeEndpointState
{
    private const string StateDirectoryName = "UnityAutorunTool";
    private const string StateFileName = "bridge-endpoint.json";
    public const string ProfileEnvironmentVariable = "UNITY_AUTORUN_BRIDGE_PROFILE";
    private const string ProfileSessionKeyPrefix = "UnityAutorunTool.Bridge.Profile";
    private static readonly string Profile;

    static AutoRunBridgeEndpointState()
    {
        // SessionState survives domain reloads. Each bridge assembly keeps its own
        // first profile for this editor session, including an explicitly empty one.
        string sessionKey = ProfileSessionKeyPrefix + "." + typeof(AutoRunBridgeEndpointState).Assembly.GetName().Name;
        if (SessionState.GetBool(sessionKey + ".Captured", false))
        {
            Profile = ValidateProfile(SessionState.GetString(sessionKey + ".Value", ""));
        }
        else
        {
            Profile = ValidateProfile(Environment.GetEnvironmentVariable(ProfileEnvironmentVariable));
            SessionState.SetString(sessionKey + ".Value", Profile ?? "");
            SessionState.SetBool(sessionKey + ".Captured", true);
        }
    }

    public static string StateFilePath
    {
        get
        {
            string directory = Path.Combine(GetProjectRootDirectory(), "Library", StateDirectoryName);
            return string.IsNullOrEmpty(Profile)
                ? Path.Combine(directory, StateFileName)
                : Path.Combine(directory, "profiles", Profile, StateFileName);
        }
    }

    public static void Publish(string host, int port)
    {
        string projectRoot = GetProjectRootDirectory();
        string path = StateFilePath;
        string directory = Path.GetDirectoryName(path);
        Directory.CreateDirectory(directory);

        var state = new EndpointState
        {
            host = host,
            port = port,
            url = $"http://{host}:{port}/",
            processId = System.Diagnostics.Process.GetCurrentProcess().Id,
            projectRoot = projectRoot.Replace('\\', '/'),
            updatedAtUtc = DateTime.UtcNow.ToString("O"),
        };

        string temporaryPath = path + "." + state.processId + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(state), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static void Clear()
    {
        try
        {
            string path = StateFilePath;
            if (!File.Exists(path))
            {
                return;
            }

            EndpointState state = JsonUtility.FromJson<EndpointState>(File.ReadAllText(path));
            int processId = System.Diagnostics.Process.GetCurrentProcess().Id;
            if (state != null && state.processId == processId)
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Runtime discovery cleanup must not prevent the bridge from stopping.
        }
    }

    private static string GetProjectRootDirectory()
    {
        DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
        if (projectRoot == null)
        {
            throw new InvalidOperationException("Cannot resolve the Unity project root directory.");
        }

        return projectRoot.FullName;
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

    [Serializable]
    private sealed class EndpointState
    {
        public string host;
        public int port;
        public string url;
        public int processId;
        public string projectRoot;
        public string updatedAtUtc;
    }
}
