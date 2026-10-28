using UnityEditor;

[InitializeOnLoad]
public static class AutoRunBridgeController
{
    private const string BridgeEnabledKey = "UnityAutorunTool.BridgeEnabled";
    private const string BridgePortKey = "UnityAutorunTool.BridgePort";
    private static readonly AutoRunBridgeServer Server = new AutoRunBridgeServer();

    // 新编辑器会话默认启动；显式停止在当前会话的程序集重载后仍然有效。
    public static bool IsEnabled => SessionState.GetBool(BridgeEnabledKey, true);
    public static bool IsRunning => Server.IsRunning;
    public static int Port => Server.Port;
    public static string Url => Server.Url;

    static AutoRunBridgeController()
    {
        EditorApplication.update += RestoreBridgeOnNextUpdate;
        AssemblyReloadEvents.beforeAssemblyReload += StopBeforeAssemblyReload;
        EditorApplication.quitting += StopBeforeAssemblyReload;
    }

    public static void Start()
    {
        SessionState.SetBool(BridgeEnabledKey, true);
        Server.Start(SessionState.GetInt(BridgePortKey, AutoRunBridgeServer.DefaultPort));
        SessionState.SetInt(BridgePortKey, Server.Port);
    }

    public static AutoRunBridgeResponse Enqueue(string requestJson)
    {
        return Server.Enqueue(requestJson);
    }

    public static void Stop()
    {
        SessionState.SetBool(BridgeEnabledKey, false);
        Server.Stop();
    }

    private static void RestoreBridgeIfNeeded()
    {
        if (IsEnabled)
        {
            Start();
        }
    }

    private static void RestoreBridgeOnNextUpdate()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= RestoreBridgeOnNextUpdate;
        RestoreBridgeIfNeeded();
    }

    private static void StopBeforeAssemblyReload()
    {
        Server.Stop();
    }
}
