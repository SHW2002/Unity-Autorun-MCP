using System;
using System.Collections.Generic;

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class InitializeOnLoadAttribute : Attribute { }

    public static class SessionState
    {
        private static readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        public static bool GetBool(string key, bool fallback) { return Values.TryGetValue(key, out object value) ? (bool)value : fallback; }
        public static void SetBool(string key, bool value) { Values[key] = value; }
        public static int GetInt(string key, int fallback) { return Values.TryGetValue(key, out object value) ? (int)value : fallback; }
        public static void SetInt(string key, int value) { Values[key] = value; }
        public static void Clear() { Values.Clear(); }
    }

    public static class EditorApplication
    {
        public static bool isCompiling;
        public static bool isUpdating;
        public static event Action update;
        public static event Action quitting;
        public static void Tick() { update?.Invoke(); }
        public static void Quit() { quitting?.Invoke(); }
    }

    public static class AssemblyReloadEvents
    {
        public static event Action beforeAssemblyReload;
        public static void Reload() { beforeAssemblyReload?.Invoke(); }
    }
}

// 只替代传输层；生命周期测试编译并调用真实 AutoRunBridgeController。
public sealed class AutoRunBridgeServer
{
    public const int DefaultPort = 17331;
    public static int SelectedPort = DefaultPort;
    public static int RequestedPort;
    public static int Starts;
    public bool IsRunning { get; private set; }
    public int Port { get; private set; } = DefaultPort;
    public string Url => "http://127.0.0.1:" + Port + "/";
    public void Start(int port) { RequestedPort = port; Port = SelectedPort; Starts++; IsRunning = true; }
    public void Stop() { IsRunning = false; }
    public AutoRunBridgeResponse Enqueue(string json) { return new AutoRunBridgeResponse(); }
}

public sealed class AutoRunBridgeResponse { }
