using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

/// <summary>Compilation evidence survives domain reload without rerunning scripts.</summary>
[InitializeOnLoad]
public static class AutoRunCompilationMonitor
{
    private const string MessagesKey = "UnityAutorunTool.Scripts.CompilerMessages";
    private static bool _refreshPending;

    public static bool IsCompiling => _refreshPending || EditorApplication.isCompiling;
    public static List<string> Messages
    {
        get
        {
            var stored = JsonUtility.FromJson<StoredMessages>(SessionState.GetString(MessagesKey, "{}"));
            return stored?.messages ?? new List<string>();
        }
    }

    static AutoRunCompilationMonitor()
    {
        CompilationPipeline.compilationStarted += _ =>
            SessionState.SetString(MessagesKey, JsonUtility.ToJson(new StoredMessages()));
        CompilationPipeline.assemblyCompilationFinished += RecordMessages;
    }

    public static void RequestRefresh()
    {
        if (_refreshPending) return;
        _refreshPending = true;
        // The bridge must reply before Refresh can unload its request thread.
        EditorApplication.delayCall += () =>
        {
            try
            {
                AssetDatabase.Refresh();
                UnityAutorun.Scripting.AutoRunScriptService.Refresh();
            }
            catch (Exception ex)
            {
                var messages = Messages;
                messages.Add("Refresh failed: " + ex.Message);
                SaveMessages(messages);
            }
            finally { _refreshPending = false; }
        };
    }

    private static void RecordMessages(string assemblyPath, CompilerMessage[] compilerMessages)
    {
        var messages = Messages;
        foreach (var message in compilerMessages)
        {
            if (message.type != CompilerMessageType.Error || messages.Count >= 100) continue;
            messages.Add(message.file + ":" + message.line + ":" + message.column + " " + message.message);
        }
        SaveMessages(messages);
    }

    private static void SaveMessages(List<string> messages)
    {
        SessionState.SetString(MessagesKey, JsonUtility.ToJson(new StoredMessages { messages = messages }));
    }

    [Serializable]
    private sealed class StoredMessages { public List<string> messages = new List<string>(); }
}
