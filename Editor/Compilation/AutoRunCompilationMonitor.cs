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
    private const string RefreshPendingKey = "UnityAutorunTool.Scripts.RefreshPending";
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
        _refreshPending = SessionState.GetBool(RefreshPendingKey, false);
        if (_refreshPending) EditorApplication.update += RefreshOnNextUpdate;
        CompilationPipeline.compilationStarted += _ =>
            SessionState.SetString(MessagesKey, JsonUtility.ToJson(new StoredMessages()));
        CompilationPipeline.assemblyCompilationFinished += RecordMessages;
    }

    public static void RequestRefresh()
    {
        if (_refreshPending) return;
        _refreshPending = true;
        SessionState.SetBool(RefreshPendingKey, true);
        // delayCall depends on Inspector updates, which can stop while Unity is
        // unfocused. Run on the next main-thread update, after the RPC can reply.
        EditorApplication.update += RefreshOnNextUpdate;
    }

    private static void RefreshOnNextUpdate()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= RefreshOnNextUpdate;
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
        finally
        {
            _refreshPending = false;
            SessionState.SetBool(RefreshPendingKey, false);
        }
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
