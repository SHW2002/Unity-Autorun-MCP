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
    // 保留旧配置窗口使用的序列化字段，避免参数应用因 FindProperty 返回 null 而中断。
    [SerializeField, HideInInspector] private string _scriptSearch = "";
    [SerializeField] private int _scriptKindFilter = 1;
    [SerializeField] private string _scriptParametersJson = "{}";
    [SerializeField] private bool _scriptEnsurePlayMode;
    [SerializeField] private double _scriptTimeoutSeconds = 120;
    [SerializeField] private string _selectedScriptRunId = "";
    [SerializeField] private string _observedScriptRunId = "";
    private string _reportedScriptRunId = "";
    private List<AutoRunScriptDefinition> _scriptDefinitions = new List<AutoRunScriptDefinition>();
    private List<string> _scriptCatalogErrors = new List<string>();
    private List<AutoRunScriptRunStatus> _scriptRuns = new List<AutoRunScriptRunStatus>();
    private readonly HashSet<string> _collapsedScriptNodes = new HashSet<string>();
    private static readonly string[] ScriptKindLabels = { "All", "Business", "Basic" };
    private AutoRunScriptCatalogTree _filteredScriptTree;
    private int _cachedScriptKind = -1;
    private bool _scriptFilterDirty = true;
    private long _catalogRevision = -1;
    private long _runsRevision = -1;
    [SerializeField] private bool _showScriptDetails;
    [SerializeField] private bool _showScriptRunOptions;
    [SerializeField] private bool _showScriptRunDetails;
    [SerializeField] private bool _showScriptHistory;
    private double _scriptLastPoll;
    private bool _showScriptLogs;
    private string _scriptWindowError;

    private void EnableScriptWindow()
    {
        EditorApplication.update -= PollScriptWindow;
        EditorApplication.update += PollScriptWindow;
        AutoRunScriptService.CatalogChanged -= HandleScriptCatalogChanged;
        AutoRunScriptService.CatalogChanged += HandleScriptCatalogChanged;
        AutoRunScriptService.RunsChanged -= HandleScriptRunsChanged;
        AutoRunScriptService.RunsChanged += HandleScriptRunsChanged;
        ReadScriptDefinitions();
        RefreshScriptRuns();
    }
    private void DisableScriptWindow()
    {
        EditorApplication.update -= PollScriptWindow;
        AutoRunScriptService.CatalogChanged -= HandleScriptCatalogChanged;
        AutoRunScriptService.RunsChanged -= HandleScriptRunsChanged;
    }
    private void HandleScriptCatalogChanged() { _catalogRevision = -1; }
    private void HandleScriptRunsChanged() { _runsRevision = -1; }

    private void ReadScriptDefinitions()
    {
        _scriptDefinitions = AutoRunScriptService.List();
        _scriptCatalogErrors = AutoRunScriptService.CatalogErrors;
        _catalogRevision = AutoRunScriptService.CatalogRevision;
        _scriptFilterDirty = true;
    }

    private void RefreshScriptDefinitions()
    {
        try
        {
            AutoRunScriptService.Refresh();
            ReadScriptDefinitions();
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
        bool changed = false;
        if (_catalogRevision != AutoRunScriptService.CatalogRevision)
        {
            ReadScriptDefinitions();
            changed = true;
        }
        if (_runsRevision != AutoRunScriptService.RunsRevision || AutoRunScriptService.HasActiveRun)
        {
            RefreshScriptRuns();
            changed = true;
        }
        if (changed) Repaint();
    }
    private void RefreshScriptRuns()
    {
        _scriptRuns = AutoRunScriptService.ListRuns();
        _runsRevision = AutoRunScriptService.RunsRevision;
        ReportCompletedScriptRun();
    }
    private void ReportCompletedScriptRun()
    {
        if (string.IsNullOrEmpty(_observedScriptRunId) || _reportedScriptRunId == _observedScriptRunId) return;
        AutoRunScriptRunStatus status = AutoRunScriptService.GetStatus(_observedScriptRunId);
        if (status == null || !status.IsTerminal) return;
        _reportedScriptRunId = status.RunId;
        AutoRunLogLevel level = status.State == AutoRunScriptRunState.Failed || status.State == AutoRunScriptRunState.TimedOut
            ? AutoRunLogLevel.Error : status.State == AutoRunScriptRunState.Canceled ? AutoRunLogLevel.Warning : AutoRunLogLevel.Info;
        AppendConsoleText("[Scripts] " + status.ScriptId + ": " + status.State + " [" + status.Code + "] " + status.Message, level);
    }

    private void RenderScriptPanel()
    {
        BeginPanel("Autorun Scripts");
        AutoRunScriptDefinition selected = _scriptDefinitions.Find(definition => definition.Id == _selectedScriptId);
        ResponsiveRow header = BeginResponsiveRow();
        header.Add(250);
        Rect selectorRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight,
            GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));
        Rect dropdownRect = EditorGUI.PrefixLabel(selectorRect, new GUIContent("Script"));
        string selectionTooltip = selected == null ? "Choose a script from its directory."
            : selected.Id + "\n" + selected.SourcePath;
        var selectionLabel = new GUIContent(selected == null ? "Choose a script..." : selected.Name, selectionTooltip);
        bool openPicker = GUI.Button(dropdownRect, selectionLabel, EditorStyles.popup);
        header.Add(95);
        _scriptKindFilter = EditorGUILayout.Popup(_scriptKindFilter, ScriptKindLabels, GUILayout.Width(95));
        header.Add(70);
        if (GUILayout.Button("Refresh", GUILayout.Width(70))) RefreshScriptDefinitions();
        header.End();

        if (!string.IsNullOrEmpty(_scriptWindowError)) EditorGUILayout.HelpBox(_scriptWindowError, MessageType.Error);
        foreach (string error in _scriptCatalogErrors) EditorGUILayout.HelpBox(error, MessageType.Error);
        if (selected != null)
        {
            GUILayout.Space(6);
            RenderScriptDetails(selected);
        }
        else GUILayout.Label("Choose a script to view its details and run options.", GetWrappedStyle(EditorStyles.miniLabel));
        RenderRecentScriptRuns();
        EndPanel();
        if (openPicker)
        {
            ShowScriptSelectionMenu(dropdownRect);
            GUIUtility.ExitGUI();
        }
    }
    private void UpdateScriptFilter()
    {
        if (!_scriptFilterDirty && _cachedScriptKind == _scriptKindFilter) return;
        AutoRunScriptKind? kind = _scriptKindFilter == 1 ? AutoRunScriptKind.Business
            : _scriptKindFilter == 2 ? AutoRunScriptKind.Basic : (AutoRunScriptKind?)null;
        _filteredScriptTree = AutoRunScriptCatalogTree.Build(_scriptDefinitions, Path.GetDirectoryName(Application.dataPath), null, kind);
        _cachedScriptKind = _scriptKindFilter;
        _scriptFilterDirty = false;
    }
    private void SelectScript(AutoRunScriptDefinition definition)
    {
        if (_selectedScriptId == definition.Id) return;
        _selectedScriptId = definition.Id;
        _showScriptDetails = false;
        _showScriptRunOptions = false;
    }

    private GenericMenu CreateScriptSelectionMenu()
    {
        UpdateScriptFilter();
        var menu = new GenericMenu();
        foreach (AutoRunScriptCatalogEntry entry in _filteredScriptTree.Scripts
            .OrderBy(item => item.DirectoryPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase))
        {
            AutoRunScriptDefinition definition = entry.Definition;
            // GenericMenu uses slash as its submenu delimiter; slashes in a display name belong to the leaf label.
            string label = entry.Label.Replace('/', '\u2215');
            string menuPath = string.IsNullOrEmpty(entry.DirectoryPath) ? label : entry.DirectoryPath + "/" + label;
            var content = new GUIContent(menuPath, definition.Id + "\n" + definition.SourcePath);
            menu.AddItem(content, definition.Id == _selectedScriptId, () =>
            {
                SelectScript(definition);
                Repaint();
            });
        }
        if (_filteredScriptTree.Scripts.Count == 0) menu.AddDisabledItem(new GUIContent("No matching scripts"));
        return menu;
    }

    private void ShowScriptSelectionMenu(Rect dropdownRect)
    {
        CreateScriptSelectionMenu().DropDown(dropdownRect);
    }
    private void RenderScriptDetails(AutoRunScriptDefinition definition)
    {
        if (!string.IsNullOrEmpty(definition.ValidationError)) EditorGUILayout.HelpBox(definition.ValidationError, MessageType.Error);
        if (!string.IsNullOrWhiteSpace(definition.ConfigurationMenuPath) && GUILayout.Button("打开配置界面"))
        {
            _scriptWindowError = EditorApplication.ExecuteMenuItem(definition.ConfigurationMenuPath)
                ? null : "Configuration menu could not be opened: " + definition.ConfigurationMenuPath;
        }
        ResponsiveRow runActions = BeginResponsiveRow();
        runActions.Add(135);
        using (new EditorGUI.DisabledScope(AutoRunScriptService.HasActiveRun || EditorApplication.isCompiling
            || EditorApplication.isUpdating || !string.IsNullOrEmpty(definition.ValidationError)))
        {
            string action = (definition.PreparePlayMode || _scriptEnsurePlayMode) && !EditorApplication.isPlaying
                ? "Start Game & Run" : "Run Script";
            if (GUILayout.Button(action, GUILayout.Width(135))) StartSelectedScript(definition);
        }
        runActions.Add(175);
        if (definition.PreparePlayMode)
            GUILayout.Label(new GUIContent("Starts game automatically", "This script declares Play Mode preparation before its starting conditions are checked."),
                EditorStyles.miniLabel, GUILayout.Width(175));
        else _scriptEnsurePlayMode = GUILayout.Toggle(_scriptEnsurePlayMode,
            new GUIContent("Start game first", "Enter Play Mode before checking the script's starting conditions."), GUILayout.Width(175));
        runActions.End();
        _showScriptRunOptions = EditorGUILayout.Foldout(_showScriptRunOptions, "Run Options", true);
        if (_showScriptRunOptions)
        {
            GUILayout.Label("Parameters JSON (string values)", EditorStyles.miniBoldLabel);
            _scriptParametersJson = EditorGUILayout.TextArea(_scriptParametersJson, GUILayout.MinHeight(48), GUILayout.ExpandWidth(true));
            _scriptTimeoutSeconds = EditorGUILayout.DoubleField("Timeout (seconds)", _scriptTimeoutSeconds);
        }
        int dependencyCount = definition.Dependencies == null ? 0 : definition.Dependencies.Length;
        _showScriptDetails = EditorGUILayout.Foldout(_showScriptDetails,
            dependencyCount == 0 ? "Script Details" : "Script Details (" + dependencyCount + " dependencies)", true);
        if (!_showScriptDetails) return;

        GUILayout.Label(definition.Kind + " | " + definition.Id, EditorStyles.miniBoldLabel);
        if (!string.IsNullOrEmpty(definition.Description)) GUILayout.Label(definition.Description, GetWrappedStyle(EditorStyles.label));
        GUILayout.Label("Starting state: " + (definition.Preconditions ?? "None declared"), GetWrappedStyle(EditorStyles.miniLabel));
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
        string deleteReason;
        bool canDelete = AutoRunScriptService.CanDelete(definition.Id, out deleteReason);
        using (new EditorGUI.DisabledScope(!canDelete))
        {
            if (GUILayout.Button(new GUIContent("Delete", deleteReason ?? "Delete this business script source"), GUILayout.Width(100)))
                DeleteBusinessScript(definition);
        }
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
            ReportCompletedScriptRun();
            _scriptWindowError = null;
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
            _observedScriptRunId = status.RunId;
            if (status.IsTerminal) ReportCompletedScriptRun();
            else AppendConsoleText("[Scripts] " + status.ScriptId + ": " + status.State + " " + status.Message, AutoRunLogLevel.Info);
            RefreshScriptRuns();
        }
        catch (Exception exception)
        {
            _scriptWindowError = "Script start failed: " + exception.Message;
            AppendConsoleText(_scriptWindowError, AutoRunLogLevel.Error);
        }
    }

    private static string GetScriptAssetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string fullPath = Path.GetFullPath(path).Replace('\\', '/');
        string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
        StringComparison pathComparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return fullPath.StartsWith(assets + "/", pathComparison) ? "Assets/" + fullPath.Substring(assets.Length + 1) : null;
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
        if (assetPath == null || (!assetPath.StartsWith("Assets/Editor/AutorunScripts/", StringComparison.Ordinal)
            && !assetPath.StartsWith("Assets/AutorunScripts/Editor/", StringComparison.Ordinal)))
        { AppendConsoleText("Only business sources under Assets/Editor/AutorunScripts or the legacy Assets/AutorunScripts/Editor can be deleted here.", AutoRunLogLevel.Error); return; }
        if (!EditorUtility.DisplayDialog("Delete Business Script", "Delete " + definition.Name + "?\n" + assetPath, "Delete", "Cancel")) return;
        if (!AutoRunScriptService.CanDelete(definition.Id, out reason)) { AppendConsoleText(reason, AutoRunLogLevel.Warning); return; }
        if (AssetDatabase.DeleteAsset(assetPath)) { AppendConsoleText("Deleted script: " + definition.Id); _selectedScriptId = ""; RefreshScriptDefinitions(); }
        else AppendConsoleText("Cannot delete script source: " + assetPath, AutoRunLogLevel.Error);
    }

    private void RenderRecentScriptRuns()
    {
        GUILayout.Space(6);
        if (_scriptRuns.Count == 0) { GUILayout.Label("No runs in this editor session.", EditorStyles.miniLabel); return; }
        // 历史选择只控制详情，摘要始终跟随当前或最新运行，包括从外部入口启动的运行。
        AutoRunScriptRunStatus current = _scriptRuns.Find(run => !run.IsTerminal) ?? _scriptRuns[0];
        AutoRunScriptDefinition definition = _scriptDefinitions.Find(item => item.Id == current.ScriptId);
        GUILayout.Label((definition == null ? current.ScriptId : definition.Name) + " | " + current.State
            + " | " + current.ElapsedSeconds.ToString("0.0") + "s", GetWrappedStyle(EditorStyles.miniBoldLabel));
        string message = !current.IsTerminal && current.Logs.Count > 0
            ? current.Logs[current.Logs.Count - 1] : current.Message ?? "";
        string summary = message.Replace('\r', ' ').Replace('\n', ' ');
        if (summary.Length > 100) summary = summary.Substring(0, 100) + "…";
        if (current.State == AutoRunScriptRunState.Failed || current.State == AutoRunScriptRunState.TimedOut)
            EditorGUILayout.HelpBox(summary, MessageType.Error);
        else GUILayout.Label(new GUIContent(summary, message), EditorStyles.miniLabel);
        if (!current.IsTerminal && GUILayout.Button("Cancel Run"))
        {
            _selectedScriptRunId = current.RunId;
            AutoRunScriptService.Cancel(current.RunId);
            AppendConsoleText("Cancellation requested: " + current.RunId);
            RefreshScriptRuns();
        }
        _showScriptHistory = EditorGUILayout.Foldout(_showScriptHistory, "Recent Runs (" + _scriptRuns.Count + ")", true);
        if (_showScriptHistory)
        {
            foreach (AutoRunScriptRunStatus run in _scriptRuns.Take(8))
            {
                AutoRunScriptDefinition script = _scriptDefinitions.Find(item => item.Id == run.ScriptId);
                if (GUILayout.Button((script == null ? run.ScriptId : script.Name) + " | " + run.State, GetWrappedStyle(GUI.skin.button)))
                {
                    _selectedScriptRunId = run.RunId;
                    _showScriptRunDetails = true;
                }
            }
        }
        _showScriptRunDetails = EditorGUILayout.Foldout(_showScriptRunDetails, "Result Details", true);
        if (!_showScriptRunDetails) return;
        AutoRunScriptRunStatus selected = _scriptRuns.Find(run => run.RunId == _selectedScriptRunId) ?? current;
        AutoRunScriptDefinition selectedDefinition = _scriptDefinitions.Find(item => item.Id == selected.ScriptId);
        GUILayout.Label((selectedDefinition == null ? selected.ScriptId : selectedDefinition.Name) + " | " + selected.State,
            GetWrappedStyle(EditorStyles.miniBoldLabel));
        GUILayout.Label(selected.RunId + " | " + selected.Code, GetWrappedStyle(EditorStyles.miniLabel));
        message = !selected.IsTerminal && selected.Logs.Count > 0
            ? selected.Logs[selected.Logs.Count - 1] : selected.Message ?? "";
        GUILayout.Label(message, GetWrappedStyle(EditorStyles.label));
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
