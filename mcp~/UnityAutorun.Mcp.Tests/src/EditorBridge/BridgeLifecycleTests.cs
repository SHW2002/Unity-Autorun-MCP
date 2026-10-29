using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace UnityAutorun.Mcp.Tests
{
    internal static class BridgeLifecycleTests
    {
        public static IEnumerable<(string Name, Action Test)> Cases()
        {
            yield return ("bridge starts automatically once the editor is ready", Startup);
            yield return ("bridge restores after reload and retains the selected port", Reload);
            yield return ("manual bridge stop survives reload until a new editor session", ManualStop);
            yield return ("bridge startup retries transient failures without blocking", RetryStartup);
            yield return ("an explicit script run restores a stopped service", EnsureRunning);
            yield return ("editor quit releases the bridge", Quit);
        }

        private static void Startup()
        {
            Assert(AutoRunBridgeController.IsEnabled && !AutoRunBridgeController.IsRunning, "New session is not enabled by default.");
            EditorApplication.isCompiling = true;
            EditorApplication.Tick();
            Assert(!AutoRunBridgeController.IsRunning, "Started while compiling.");
            EditorApplication.isCompiling = false;
            EditorApplication.isUpdating = true;
            EditorApplication.Tick();
            Assert(!AutoRunBridgeController.IsRunning, "Started while importing.");
            EditorApplication.isUpdating = false;
            AutoRunBridgeServer.SelectedPort = 17332;
            EditorApplication.Tick();
            EditorApplication.Tick();
            Assert(AutoRunBridgeController.IsRunning && AutoRunBridgeServer.Starts == 1, "Startup must run once after the editor is ready.");
        }

        private static void Reload()
        {
            AssemblyReloadEvents.Reload();
            Assert(!AutoRunBridgeController.IsRunning && AutoRunBridgeController.IsEnabled, "Reload discarded the enabled state.");
            RestoreAfterReload();
            Assert(AutoRunBridgeController.IsRunning && AutoRunBridgeServer.RequestedPort == 17332, "Reload lost the selected port.");
        }

        private static void ManualStop()
        {
            AutoRunBridgeController.Stop();
            AssemblyReloadEvents.Reload();
            RestoreAfterReload();
            Assert(!AutoRunBridgeController.IsRunning && !AutoRunBridgeController.IsEnabled, "Reload undid manual stop.");
            // SessionState 在关闭 Unity 后清空；下一会话应再次默认启动。
            SessionState.Clear();
            RestoreAfterReload();
            Assert(AutoRunBridgeController.IsRunning && AutoRunBridgeController.IsEnabled, "A new editor session did not auto-start.");
        }

        private static void RetryStartup()
        {
            AssemblyReloadEvents.Reload();
            AutoRunBridgeServer.FailuresRemaining = 1;
            int starts = AutoRunBridgeServer.Starts;
            RestoreAfterReload();
            Assert(!AutoRunBridgeController.IsRunning && AutoRunWindow.Warnings == 1, "Startup failure escaped or was not reported.");
            EditorApplication.Tick();
            Assert(AutoRunBridgeServer.Starts == starts + 1, "Startup failure retried every frame.");
            EditorApplication.timeSinceStartup += 5;
            EditorApplication.Tick();
            Assert(AutoRunBridgeController.IsRunning && AutoRunBridgeServer.Starts == starts + 2, "Startup did not recover automatically.");
        }

        private static void EnsureRunning()
        {
            AutoRunBridgeController.Stop();
            EditorApplication.Tick();
            Assert(!AutoRunBridgeController.IsRunning, "Manual stop was ignored.");
            EditorApplication.isCompiling = true;
            AutoRunBridgeController.EnsureRunning();
            AutoRunBridgeController.EnsureRunning();
            EditorApplication.Tick();
            Assert(!AutoRunBridgeController.IsRunning, "Explicit recovery started during compilation.");
            EditorApplication.isCompiling = false;
            int starts = AutoRunBridgeServer.Starts;
            EditorApplication.Tick();
            EditorApplication.Tick();
            Assert(AutoRunBridgeController.IsRunning && AutoRunBridgeServer.Starts == starts + 1, "Explicit recovery did not start exactly once.");
        }

        private static void Quit()
        {
            EditorApplication.Quit();
            Assert(!AutoRunBridgeController.IsRunning, "Quit left the bridge running.");
            EditorApplication.Tick();
            Assert(!AutoRunBridgeController.IsRunning, "An update after quit restarted the bridge.");
        }

        private static void RestoreAfterReload()
        {
            // 域重载后 Unity 会重新注册此回调；测试宿主显式调度同一个入口。
            MethodInfo restore = typeof(AutoRunBridgeController).GetMethod("RestoreBridgeOnNextUpdate", BindingFlags.NonPublic | BindingFlags.Static);
            Action callback = (Action)Delegate.CreateDelegate(typeof(Action), restore);
            EditorApplication.update -= callback;
            EditorApplication.update += callback;
            EditorApplication.Tick();
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
