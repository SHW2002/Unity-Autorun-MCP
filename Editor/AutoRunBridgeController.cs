using System;
using UnityEditor;

[InitializeOnLoad]
public static class AutoRunBridgeController
{
    private const string BridgeEnabledKey = "UnityAutorunTool.BridgeEnabled";
    private const string BridgePortKey = "UnityAutorunTool.BridgePort";
    private const double RestoreRetrySeconds = 5;
    private static readonly AutoRunBridgeServer Server = new AutoRunBridgeServer();
    private static double _nextRestoreTime;

    // 新编辑器会话默认启动；显式停止保持有效，直到用户再次启动服务或运行脚本。
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
        _nextRestoreTime = 0;
    }

    public static void EnsureRunning()
    {
        // 显式运行脚本即请求恢复连接；启动失败由后台重试处理，不阻塞本地脚本。
        SessionState.SetBool(BridgeEnabledKey, true);
        _nextRestoreTime = 0;
        EditorApplication.update -= RestoreBridgeOnNextUpdate;
        EditorApplication.update += RestoreBridgeOnNextUpdate;
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

    private static void RestoreBridgeOnNextUpdate()
    {
        if (!IsEnabled || IsRunning || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.timeSinceStartup < _nextRestoreTime) return;
        try
        {
            Start();
        }
        catch (Exception exception)
        {
            // 导入、端口或文件系统的短暂失败不能永久丢失启动回调，也不能每帧刷屏。
            _nextRestoreTime = EditorApplication.timeSinceStartup + RestoreRetrySeconds;
            AutoRunWindow.AppendBridgeConsoleText("AutoRun bridge startup failed; retrying in "
                + RestoreRetrySeconds + "s: " + exception.Message, AutoRunLogLevel.Warning);
        }
    }

    private static void StopBeforeAssemblyReload()
    {
        EditorApplication.update -= RestoreBridgeOnNextUpdate;
        Server.Stop();
    }
}
