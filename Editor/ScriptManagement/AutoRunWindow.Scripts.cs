using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityAutorun.Scripting;

public partial class AutoRunWindow
{
    [SerializeField] private string _selectedScriptId = "";
    [SerializeField] private string _scriptSearch = "";
    [SerializeField] private int _scriptKindFilter = 1;
    [SerializeField] private string _scriptParametersJson = "{}";
    [SerializeField] private bool _scriptEnsurePlayMode;
    [SerializeField] private double _scriptTimeoutSeconds = 120;
    [SerializeField] private string _selectedScriptRunId = "";
    private List<AutoRunScriptDefinition> _scriptDefinitions = new List<AutoRunScriptDefinition>();
    private List<AutoRunScriptRunStatus> _scriptRuns = new List<AutoRunScriptRunStatus>();
    private readonly HashSet<string> _collapsedScriptNodes = new HashSet<string>();
    private double _scriptLastPoll;
    private bool _showScriptLogs;
    private string _scriptWindowError;

    private void EnableScriptWindow()
    {
        EditorApplication.update -= PollScriptWindow;
        EditorApplication.update += PollScriptWindow;
        RefreshScriptDefinitions();
    }
    private void DisableScriptWindow() { EditorApplication.update -= PollScriptWindow; }

    private void RefreshScriptDefinitions()
    {
        try
        {
            AutoRunScriptService.Refresh();
            _scriptDefinitions = AutoRunScriptService.List();
            _scriptWindowError = null;
            RefreshScriptRuns();
        }
        catch (Exception exception) { _scriptWindowError = exception.Message; }
        Repaint();
    }
    private void PollScriptWindow()
    {
        if (EditorApplication.timeSinceStartup - _scriptLastPoll < 0.5) return;
        _scriptLastPoll = EditorApplication.timeSinceStartup;
        RefreshScriptRuns();
        if (AutoRunScriptService.HasActiveRun || _scriptRuns.Count > 0) Repaint();
    }
    private void RefreshScriptRuns()
    {
        _scriptRuns = AutoRunScriptService.ListRuns().OrderByDescending(run => run.StartedAt).ToList();
    }

    private void RenderScriptPanel()
    {
        BeginPanel("Autorun Scripts");
        ResponsiveRow header = BeginResponsiveRow();
        header.Add(180);
        _scriptSearch = EditorGUILayout.TextField(_scriptSearch ?? "", GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));
        header.Add(95);
        _scriptKindFilter = EditorGUILayout.Popup(_scriptKindFilter, new[] { "All", "Business", "Basic" }, GUILayout.Width(95));
        header.Add(70);
        if (GUILayout.Button("Refresh", GUILayout.Width(70))) RefreshScriptDefinitions();
        header.End();

        if (!string.IsNullOrEmpty(_scriptWindowError)) EditorGUILayout.HelpBox(_scriptWindowError, MessageType.Error);
        foreach (string error in AutoRunScriptService.CatalogErrors) EditorGUILayout.HelpBox(error, MessageType.Error);
        List<AutoRunScriptDefinition> filtered = _scriptDefinitions.Where(MatchesScriptFilter).ToList();
        if (filtered.Count == 0)
        {
            GUILayout.Label("No matching scripts. Use gen-script to create a business script.", GetWrappedStyle(EditorStyles.miniLabel));
        }
        else
        {
            int index = filtered.FindIndex(definition => definition.Id == _selectedScriptId);
            if (index < 0) index = 0;
            index = EditorGUILayout.Popup("Script", index, filtered.Select(definition => definition.Name + " [" + definition.Id + "]").ToArray());
            _selectedScriptId = filtered[index].Id;
            RenderScriptDetails(filtered[index]);
        }
        RenderRecentScriptRuns();
        EndPanel();
    }
    private bool MatchesScriptFilter(AutoRunScriptDefinition definition)
    {
        if (_scriptKindFilter == 1 && definition.Kind != AutoRunScriptKind.Business) return false;
        if (_scriptKindFilter == 2 && definition.Kind != AutoRunScriptKind.Basic) return false;
        string query = (_scriptSearch ?? "").Trim();
        return query.Length == 0 || (definition.Id + " " + definition.Name + " " + definition.Description)
            .IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }
    private void RenderScriptDetails(AutoRunScriptDefinition definition)
    {
        GUILayout.Label(definition.Kind + " | " + definition.Id, EditorStyles.miniBoldLabel);
        if (!string.IsNullOrEmpty(definition.Description)) GUILayout.Label(definition.Description, GetWrappedStyle(EditorStyles.label));
        GUILayout.Label("Starting state: " + (definition.Preconditions ?? "None declared"), GetWrappedStyle(EditorStyles.miniLabel));
        if (!string.IsNullOrEmpty(definition.ValidationError)) EditorGUILayout.HelpBox(definition.ValidationError, MessageType.Error);
        ResponsiveRow source = BeginResponsiveRow();
        source.Add(190);
        GUILayout.Label(definition.SourcePath ?? definition.ClassName, GetWrappedStyle(EditorStyles.miniLabel), GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));
        source.Add(90);
        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(definition.SourcePath) || !File.Exists(definition.SourcePath)))
        {
            if (GUILayout.Button("Open Source", GUILayout.Width(90))) OpenScriptSource(definition.SourcePath);
        }
        source.End();

        GUILayout.Label("Dependencies", EditorStyles.miniBoldLabel);
        if (definition.Dependencies == null || definition.Dependencies.Length == 0) GUILayout.Label("None", EditorStyles.miniLabel);
        else RenderScriptDependencyTree(definition.Id, new HashSet<string>(), definition.Id, 0);
        string[] callers = _scriptDefinitions.Where(item => item.Dependencies != null && item.Dependencies.Contains(definition.Id))
            .Select(item => item.Name + " [" + item.Id + "]").ToArray();
        GUILayout.Label("Called by: " + (callers.Length == 0 ? "None" : string.Join(", ", callers)), GetWrappedStyle(EditorStyles.miniLabel));
        GUILayout.Label("Parameters JSON (string values)", EditorStyles.miniBoldLabel);
        _scriptParametersJson = EditorGUILayout.TextArea(_scriptParametersJson, GUILayout.MinHeight(48), GUILayout.ExpandWidth(true));
        _scriptEnsurePlayMode = EditorGUILayout.Toggle("Ensure Play Mode", _scriptEnsurePlayMode);
        _scriptTimeoutSeconds = EditorGUILayout.DoubleField("Timeout (seconds)", _scriptTimeoutSeconds);

        ResponsiveRow actions = BeginResponsiveRow();
        actions.Add(100);
        using (new EditorGUI.DisabledScope(AutoRunScriptService.HasActiveRun || EditorApplication.isCompiling
            || EditorApplication.isUpdating || !string.IsNullOrEmpty(definition.ValidationError)))
        {
            if (GUILayout.Button("Run Script", GUILayout.Width(100))) StartSelectedScript(definition);
        }
        actions.Add(100);
        string deleteReason;
        bool canDelete = AutoRunScriptService.CanDelete(definition.Id, out deleteReason);
        using (new EditorGUI.DisabledScope(!canDelete))
        {
            if (GUILayout.Button(new GUIContent("Delete", deleteReason ?? "Delete this business script source"), GUILayout.Width(100)))
                DeleteBusinessScript(definition);
        }
        actions.End();
        GUILayout.Label("Completion, failure, and cancellation preserve Play Mode and the current game state.", GetWrappedStyle(EditorStyles.miniLabel));
    }

    private void RenderScriptDependencyTree(string id, HashSet<string> ancestors, string key, int depth)
    {
        AutoRunScriptDefinition definition = _scriptDefinitions.Find(item => item.Id == id);
        if (!ancestors.Add(id)) { EditorGUILayout.HelpBox("Dependency cycle: " + id, MessageType.Error); return; }
        if (definition == null) { EditorGUILayout.HelpBox("Missing dependency: " + id, MessageType.Error); ancestors.Remove(id); return; }
        if (depth > 64) { EditorGUILayout.HelpBox("Dependency nesting exceeds 64 levels.", MessageType.Error); ancestors.Remove(id); return; }
        if (depth > 0)
        {
            EditorGUI.indentLevel++;
            bool open = EditorGUILayout.Foldout(!_collapsedScriptNodes.Contains(key), definition.Name + " [" + id + "]", true);
            if (open) _collapsedScriptNodes.Remove(key); else _collapsedScriptNodes.Add(key);
            if (!open) { EditorGUI.indentLevel--; ancestors.Remove(id); return; }
        }
        foreach (string dependency in definition.Dependencies ?? new string[0])
            RenderScriptDependencyTree(dependency, ancestors, key + "/" + dependency, depth + 1);
        if (depth > 0) EditorGUI.indentLevel--;
        ancestors.Remove(id);
    }

    private void StartSelectedScript(AutoRunScriptDefinition definition)
    {
        try
        {
            if (double.IsNaN(_scriptTimeoutSeconds) || double.IsInfinity(_scriptTimeoutSeconds) || _scriptTimeoutSeconds <= 0)
                throw new InvalidOperationException("Timeout must be a positive finite number.");
            var source = AutoRunJsonParser.Parse(_scriptParametersJson) as Dictionary<string, object>;
            if (source == null) throw new InvalidOperationException("Parameters must be a JSON object with string values.");
            var parameters = new Dictionary<string, string>();
            foreach (KeyValuePair<string, object> item in source)
            {
                if (!(item.Value is string)) throw new InvalidOperationException("Parameter '" + item.Key + "' must be a string.");
                parameters.Add(item.Key, (string)item.Value);
            }
            AutoRunScriptRunStatus status = AutoRunScriptService.Start(definition.Id, parameters, _scriptEnsurePlayMode, _scriptTimeoutSeconds);
            _selectedScriptRunId = status.RunId;
            AppendConsoleText("[Scripts] " + status.ScriptId + ": " + status.State + " " + status.Message,
                status.State == AutoRunScriptRunState.Failed ? AutoRunLogLevel.Error : AutoRunLogLevel.Info);
            RefreshScriptRuns();
        }
        catch (Exception exception) { AppendConsoleText("Script start failed: " + exception.Message, AutoRunLogLevel.Error); }
    }

    private static string GetScriptAssetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string fullPath = Path.GetFullPath(path).Replace('\\', '/');
        string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
        return fullPath.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase) ? "Assets/" + fullPath.Substring(assets.Length + 1) : null;
    }
    private static void OpenScriptSource(string path)
    {
        string assetPath = GetScriptAssetPath(path);
        UnityEngine.Object asset = assetPath == null ? null : AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
        if (asset != null) AssetDatabase.OpenAsset(asset);
        else UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(path, 1);
    }
    private void DeleteBusinessScript(AutoRunScriptDefinition definition)
    {
        string reason;
        if (!AutoRunScriptService.CanDelete(definition.Id, out reason)) { AppendConsoleText(reason, AutoRunLogLevel.Warning); return; }
        string assetPath = GetScriptAssetPath(definition.SourcePath);
        if (assetPath == null || !assetPath.StartsWith("Assets/AutorunScripts/Editor/", StringComparison.Ordinal))
        { AppendConsoleText("Only business sources under Assets/AutorunScripts/Editor can be deleted here.", AutoRunLogLevel.Error); return; }
        if (!EditorUtility.DisplayDialog("Delete Business Script", "Delete " + definition.Name + "?\n" + assetPath, "Delete", "Cancel")) return;
        if (!AutoRunScriptService.CanDelete(definition.Id, out reason)) { AppendConsoleText(reason, AutoRunLogLevel.Warning); return; }
        if (AssetDatabase.DeleteAsset(assetPath)) { AppendConsoleText("Deleted script: " + definition.Id); _selectedScriptId = ""; RefreshScriptDefinitions(); }
        else AppendConsoleText("Cannot delete script source: " + assetPath, AutoRunLogLevel.Error);
    }

    private void RenderRecentScriptRuns()
    {
        GUILayout.Space(6);
        GUILayout.Label("Recent Results", EditorStyles.boldLabel);
        if (_scriptRuns.Count == 0) { GUILayout.Label("No runs in this editor session.", EditorStyles.miniLabel); return; }
        foreach (AutoRunScriptRunStatus run in _scriptRuns.Take(8))
        {
            if (GUILayout.Button(run.ScriptId + " | " + run.State + " | " + run.Code, GetWrappedStyle(GUI.skin.button)))
                _selectedScriptRunId = run.RunId;
        }
        AutoRunScriptRunStatus selected = _scriptRuns.Find(run => run.RunId == _selectedScriptRunId) ?? _scriptRuns[0];
        GUILayout.Label(selected.RunId + " | " + selected.State + " | " + selected.ElapsedSeconds.ToString("0.0") + "s", GetWrappedStyle(EditorStyles.miniLabel));
        GUILayout.Label(selected.Message ?? "", GetWrappedStyle(EditorStyles.label));
        if (!selected.IsTerminal && GUILayout.Button("Cancel Run"))
        {
            AutoRunScriptService.Cancel(selected.RunId);
            AppendConsoleText("Cancellation requested: " + selected.RunId);
            RefreshScriptRuns();
        }
        foreach (AutoRunScriptInvocation call in selected.Calls)
            GUILayout.Label(new string(' ', call.Depth * 2) + call.ScriptId + " | " + call.State + " | " + call.Message, GetWrappedStyle(EditorStyles.miniLabel));
        foreach (string artifact in selected.Artifacts)
        {
            if (GUILayout.Button("Open artifact: " + artifact, GetWrappedStyle(GUI.skin.button)) && File.Exists(artifact)) EditorUtility.RevealInFinder(artifact);
        }
        _showScriptLogs = EditorGUILayout.Foldout(_showScriptLogs, "Run Logs (" + selected.Logs.Count + ")", true);
        if (_showScriptLogs)
            foreach (string log in selected.Logs) GUILayout.Label(log, GetWrappedStyle(EditorStyles.miniLabel));
    }
}
