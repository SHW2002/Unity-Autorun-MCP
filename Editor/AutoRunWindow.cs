using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Text;

public partial class AutoRunWindow : EditorWindow
{
    private const int MaxConsoleChars = 20000;
    private const int MaxConsoleEntryChars = 1200;
    private const float ConsoleViewportHeight = 220f;
    private const float ConsoleViewportPadding = 3f;
    private const float ConsoleRowSpacing = 2f;
    private const float ConsoleScrollbarWidth = 16f;
    private const float WindowVerticalScrollbarWidth = 18f;
    private const float WindowContentPadding = 10f;
    private const float ResponsiveLayoutHorizontalInset = 20f;
    private const float ResponsiveLayoutItemSpacing = 4f;

    private readonly Queue<AutoRunConsoleEntry> _consoleEntries = new Queue<AutoRunConsoleEntry>();
    private readonly List<AutoRunConsoleEntry> _visibleConsoleEntries = new List<AutoRunConsoleEntry>();
    private int _consoleCharCount;
    private bool _consoleViewDirty = true;
    private bool _consoleShouldScrollToBottom;
    private bool _showDebugLogs;
    [SerializeField] private bool _showConsole;
    private bool _showInfoLogs = true;
    private bool _showWarningLogs = true;
    private bool _showErrorLogs = true;
    private const string WindowScrollXKey = "UnityAutorunTool.Window.ScrollX";
    private const string WindowScrollYKey = "UnityAutorunTool.Window.ScrollY";
    private static readonly Dictionary<AutoRunLogLevel, GUIStyle> ConsoleEntryStyles = new Dictionary<AutoRunLogLevel, GUIStyle>();
    private static readonly Dictionary<GUIStyle, GUIStyle> SqueezedStyles = new Dictionary<GUIStyle, GUIStyle>();
    private static readonly Dictionary<GUIStyle, GUIStyle> WrappedStyles = new Dictionary<GUIStyle, GUIStyle>();
    private Vector2 _windowScrollPosition;
    private Vector2 _consoleScrollPosition;

    [MenuItem("Window/Auto Run Window")]
    public static void ShowWindow()
    {
        GetWindow<AutoRunWindow>("Auto Run");
    }

    private void OnEnable()
    {
        _windowScrollPosition = new Vector2(
            0f,
            EditorPrefs.GetFloat(WindowScrollYKey, 0f)
        );
        LoadMcpInstallTargetPath();
        EnableScriptWindow();
        EnableUpdateCheck();
        EditorApplication.update -= PollWindowTasks;
        EditorApplication.update += PollWindowTasks;
    }

    private void OnDisable()
    {
        DisableUpdateCheck();
        DisableScriptWindow();
        EditorApplication.update -= PollWindowTasks;
        SaveWindowScrollPosition();
    }

    private void OnFocus()
    {
        InvalidateSkillInstallStatus();
        AutoRunUpdateCheckService.CheckIfDue();
        Repaint();

    }

    private void OnGUI()
    {
        Vector2 nextScrollPosition = GUILayout.BeginScrollView(
            _windowScrollPosition,
            false,
            true,
            GUIStyle.none,
            GUI.skin.verticalScrollbar
        );
        nextScrollPosition.x = 0f;
        if (nextScrollPosition != _windowScrollPosition)
        {
            _windowScrollPosition = nextScrollPosition;
            SaveWindowScrollPosition();
        }

        GUILayout.BeginVertical(GUILayout.Width(GetWindowContentWidth()), GUILayout.ExpandWidth(false));
        RenderScriptPanel();
        RenderMcpPanel();
        RenderConsole();

        GUILayout.EndVertical();
        GUILayout.EndScrollView();
    }

    private void SaveWindowScrollPosition()
    {
        EditorPrefs.SetFloat(WindowScrollXKey, 0f);
        EditorPrefs.SetFloat(WindowScrollYKey, _windowScrollPosition.y);
    }

    private float GetWindowContentWidth()
    {
        return Mathf.Max(1f, position.width - WindowVerticalScrollbarWidth - WindowContentPadding);
    }

    private float GetResponsiveContentWidth(float additionalInset = 0f)
    {
        return Mathf.Max(
            1f,
            GetWindowContentWidth() - ResponsiveLayoutHorizontalInset - additionalInset);
    }

    private ResponsiveRow BeginResponsiveRow(float additionalInset = 0f)
    {
        return new ResponsiveRow(GetResponsiveContentWidth(additionalInset));
    }

    private static GUIStyle GetSqueezedStyle(GUIStyle baseStyle)
    {
        if (SqueezedStyles.TryGetValue(baseStyle, out GUIStyle style))
        {
            return style;
        }

        style = new GUIStyle(baseStyle)
        {
            clipping = TextClipping.Clip,
            wordWrap = false,
        };
        SqueezedStyles[baseStyle] = style;
        return style;
    }

    private static GUIStyle GetWrappedStyle(GUIStyle baseStyle)
    {
        if (WrappedStyles.TryGetValue(baseStyle, out GUIStyle style))
        {
            return style;
        }

        style = new GUIStyle(baseStyle)
        {
            clipping = TextClipping.Clip,
            wordWrap = true,
        };
        WrappedStyles[baseStyle] = style;
        return style;
    }

    private struct ResponsiveRow
    {
        private readonly float _availableWidth;
        private float _usedWidth;
        private bool _isOpen;
        private bool _hasItems;

        public ResponsiveRow(float availableWidth)
        {
            _availableWidth = Mathf.Max(1f, availableWidth);
            _usedWidth = 0f;
            _isOpen = false;
            _hasItems = false;
        }

        public void Add(float minimumWidth)
        {
            float itemWidth = Mathf.Min(Mathf.Max(0f, minimumWidth), _availableWidth);
            if (!_isOpen)
            {
                BeginLine();
            }
            else if (_hasItems
                && _usedWidth + ResponsiveLayoutItemSpacing + itemWidth > _availableWidth)
            {
                GUILayout.EndHorizontal();
                BeginLine();
            }

            if (_hasItems)
            {
                _usedWidth += ResponsiveLayoutItemSpacing;
            }

            _usedWidth += itemWidth;
            _hasItems = true;
        }

        public void End()
        {
            if (!_isOpen)
            {
                return;
            }

            GUILayout.EndHorizontal();
            _isOpen = false;
            _hasItems = false;
        }

        private void BeginLine()
        {
            GUILayout.BeginHorizontal();
            _usedWidth = 0f;
            _isOpen = true;
            _hasItems = false;
        }
    }

    private void BeginPanel(string title)
    {
        GUILayout.Space(8);
        GUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label(title, EditorStyles.boldLabel);
    }

    private void EndPanel()
    {
        GUILayout.EndVertical();
    }

    private void ClearConsoleText()
    {
        _consoleEntries.Clear();
        _visibleConsoleEntries.Clear();
        _consoleCharCount = 0;
        _consoleViewDirty = false;
        _consoleShouldScrollToBottom = false;
        _consoleScrollPosition = Vector2.zero;
    }

    private void AppendConsoleText(string text)
    {
        AppendConsoleText(text, AutoRunLogLevel.Info);
    }

    private void AppendConsoleText(string text, AutoRunLogLevel level)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        text = text.TrimEnd('\r', '\n');
        if (text.Length == 0)
        {
            return;
        }

        if (text.Length > MaxConsoleEntryChars)
        {
            text = text.Substring(0, MaxConsoleEntryChars) + "... [truncated]";
        }

        var entry = new AutoRunConsoleEntry(level, text);
        _consoleEntries.Enqueue(entry);
        _consoleCharCount += entry.Text.Length;
        while (_consoleCharCount > MaxConsoleChars && _consoleEntries.Count > 0)
        {
            _consoleCharCount -= _consoleEntries.Dequeue().Text.Length;
        }

        _consoleViewDirty = true;
        _consoleShouldScrollToBottom = true;
    }

    public static void AppendBridgeConsoleText(string text)
    {
        AppendBridgeConsoleText(text, AutoRunLogLevel.Info);
    }

    public static void AppendBridgeConsoleText(string text, AutoRunLogLevel level)
    {
        foreach (AutoRunWindow window in Resources.FindObjectsOfTypeAll<AutoRunWindow>())
        {
            window.AppendConsoleText(text, level);
            window.Repaint();
        }
    }

    public static void RepaintAllWindows()
    {
        foreach (AutoRunWindow window in Resources.FindObjectsOfTypeAll<AutoRunWindow>())
        {
            window.Repaint();
        }
    }

    private void RenderConsole()
    {
        _showConsole = EditorGUILayout.Foldout(_showConsole, "Console (" + _consoleEntries.Count + ")", true);
        if (!_showConsole) return;
        RenderConsoleLevelFilters();

        EnsureVisibleConsoleEntries();
        ResponsiveRow actions = BeginResponsiveRow();
        actions.Add(95f);
        using (new EditorGUI.DisabledScope(_visibleConsoleEntries.Count == 0))
        {
            if (GUILayout.Button("Copy Visible", GUILayout.Width(95)))
            {
                CopyConsoleEntries(_visibleConsoleEntries);
            }
        }

        actions.Add(75f);
        using (new EditorGUI.DisabledScope(_consoleEntries.Count == 0))
        {
            if (GUILayout.Button("Copy All", GUILayout.Width(75)))
            {
                CopyConsoleEntries(_consoleEntries);
            }
        }

        actions.Add(55f);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Clear", GUILayout.Width(55)))
        {
            ClearConsoleText();
        }
        actions.End();

        Rect consoleRect = GUILayoutUtility.GetRect(
            GUIContent.none,
            EditorStyles.helpBox,
            GUILayout.Height(ConsoleViewportHeight),
            GUILayout.ExpandWidth(true));
        GUI.Box(consoleRect, GUIContent.none, EditorStyles.helpBox);

        Rect viewportRect = new Rect(
            consoleRect.x + ConsoleViewportPadding,
            consoleRect.y + ConsoleViewportPadding,
            Mathf.Max(1f, consoleRect.width - ConsoleViewportPadding * 2f),
            Mathf.Max(1f, consoleRect.height - ConsoleViewportPadding * 2f));
        float rowHeight = Mathf.Ceil(EditorGUIUtility.singleLineHeight) + ConsoleRowSpacing;
        int rowCount = Mathf.Max(1, _visibleConsoleEntries.Count);
        float contentHeight = Mathf.Max(viewportRect.height, rowCount * rowHeight);
        float contentWidth = Mathf.Max(1f, viewportRect.width - ConsoleScrollbarWidth);
        Rect contentRect = new Rect(0f, 0f, contentWidth, contentHeight);

        if (_consoleShouldScrollToBottom)
        {
            _consoleScrollPosition.y = Mathf.Max(0f, contentHeight - viewportRect.height);
            _consoleShouldScrollToBottom = false;
        }

        _consoleScrollPosition.x = 0f;
        _consoleScrollPosition = GUI.BeginScrollView(
            viewportRect,
            _consoleScrollPosition,
            contentRect,
            false,
            true);

        if (_visibleConsoleEntries.Count == 0)
        {
            string emptyMessage = _consoleEntries.Count == 0
                ? "No logs."
                : "No logs match the selected levels.";
            Rect emptyRect = new Rect(0f, 0f, contentWidth, rowHeight);
            EditorGUI.SelectableLabel(emptyRect, emptyMessage, GetConsoleEntryStyle(AutoRunLogLevel.Debug));
        }
        else
        {
            DrawVisibleConsoleRows(contentWidth, rowHeight, viewportRect.height);
        }

        GUI.EndScrollView();
    }

    private void RenderConsoleLevelFilters()
    {
        EditorGUI.BeginChangeCheck();
        ResponsiveRow filters = BeginResponsiveRow();
        filters.Add(134f);
        _showDebugLogs = GUILayout.Toggle(_showDebugLogs, "Debug", GUILayout.Width(70));
        _showInfoLogs = GUILayout.Toggle(_showInfoLogs, "Info", GUILayout.Width(60));
        filters.Add(154f);
        _showWarningLogs = GUILayout.Toggle(_showWarningLogs, "Warning", GUILayout.Width(85));
        _showErrorLogs = GUILayout.Toggle(_showErrorLogs, "Error", GUILayout.Width(65));
        filters.End();

        if (EditorGUI.EndChangeCheck())
        {
            _consoleViewDirty = true;
            _consoleShouldScrollToBottom = true;
        }
    }

    private void EnsureVisibleConsoleEntries()
    {
        if (!_consoleViewDirty)
        {
            return;
        }

        _visibleConsoleEntries.Clear();
        foreach (AutoRunConsoleEntry entry in _consoleEntries)
        {
            if (ShouldShowConsoleEntry(entry.Level))
            {
                _visibleConsoleEntries.Add(entry);
            }
        }

        _consoleViewDirty = false;
    }

    private void DrawVisibleConsoleRows(float contentWidth, float rowHeight, float viewportHeight)
    {
        int firstVisibleIndex = Mathf.Clamp(
            Mathf.FloorToInt(_consoleScrollPosition.y / rowHeight),
            0,
            _visibleConsoleEntries.Count - 1);
        int lastVisibleIndex = Mathf.Clamp(
            Mathf.CeilToInt((_consoleScrollPosition.y + viewportHeight) / rowHeight),
            firstVisibleIndex,
            _visibleConsoleEntries.Count - 1);

        for (int index = firstVisibleIndex; index <= lastVisibleIndex; index++)
        {
            AutoRunConsoleEntry entry = _visibleConsoleEntries[index];
            Rect rowRect = new Rect(0f, index * rowHeight, contentWidth, rowHeight);
            HandleConsoleRowContextMenu(rowRect, entry);
            EditorGUI.SelectableLabel(rowRect, entry.DisplayText, GetConsoleEntryStyle(entry.Level));
        }
    }

    private void HandleConsoleRowContextMenu(Rect rowRect, AutoRunConsoleEntry entry)
    {
        Event currentEvent = Event.current;
        if (currentEvent.type != EventType.ContextClick || !rowRect.Contains(currentEvent.mousePosition))
        {
            return;
        }

        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("Copy Log"), false, () => CopyConsoleEntry(entry));
        menu.AddItem(new GUIContent("Copy Visible"), false, () => CopyConsoleEntries(_visibleConsoleEntries));
        menu.AddItem(new GUIContent("Copy All"), false, () => CopyConsoleEntries(_consoleEntries));
        menu.ShowAsContext();
        currentEvent.Use();
    }

    private void CopyConsoleEntry(AutoRunConsoleEntry entry)
    {
        EditorGUIUtility.systemCopyBuffer = entry.DisplayText;
        ShowNotification(new GUIContent("Log copied."));
    }

    private void CopyConsoleEntries(IEnumerable<AutoRunConsoleEntry> entries)
    {
        StringBuilder text = new StringBuilder();
        int entryCount = 0;
        foreach (AutoRunConsoleEntry entry in entries)
        {
            if (entryCount > 0)
            {
                text.AppendLine();
            }

            text.Append(entry.DisplayText);
            entryCount++;
        }

        if (entryCount == 0)
        {
            return;
        }

        EditorGUIUtility.systemCopyBuffer = text.ToString();
        ShowNotification(new GUIContent(entryCount + " logs copied."));
    }

    private bool ShouldShowConsoleEntry(AutoRunLogLevel level)
    {
        switch (level)
        {
            case AutoRunLogLevel.Debug:
                return _showDebugLogs;
            case AutoRunLogLevel.Info:
                return _showInfoLogs;
            case AutoRunLogLevel.Warning:
                return _showWarningLogs;
            case AutoRunLogLevel.Error:
                return _showErrorLogs;
            default:
                return true;
        }
    }

    private static string FormatConsoleEntry(AutoRunLogLevel level, string text)
    {
        return "[" + GetConsoleLevelPrefix(level) + "] " + text;
    }

    private static string GetConsoleLevelPrefix(AutoRunLogLevel level)
    {
        switch (level)
        {
            case AutoRunLogLevel.Debug:
                return "D";
            case AutoRunLogLevel.Info:
                return "I";
            case AutoRunLogLevel.Warning:
                return "W";
            case AutoRunLogLevel.Error:
                return "E";
            default:
                return "?";
        }
    }

    private static GUIStyle GetConsoleEntryStyle(AutoRunLogLevel level)
    {
        if (ConsoleEntryStyles.TryGetValue(level, out GUIStyle style))
        {
            return style;
        }

        style = new GUIStyle(EditorStyles.label)
        {
            wordWrap = false,
            richText = false,
            clipping = TextClipping.Clip,
            alignment = TextAnchor.MiddleLeft,
        };
        Color textColor = GetConsoleTextColor(level);
        style.normal.textColor = textColor;
        style.hover.textColor = textColor;
        style.active.textColor = textColor;
        style.focused.textColor = textColor;
        ConsoleEntryStyles[level] = style;
        return style;
    }

    private static Color GetConsoleTextColor(AutoRunLogLevel level)
    {
        switch (level)
        {
            case AutoRunLogLevel.Debug:
                return new Color(0.55f, 0.55f, 0.55f);
            case AutoRunLogLevel.Warning:
                return new Color(1f, 0.72f, 0.16f);
            case AutoRunLogLevel.Error:
                return new Color(1f, 0.25f, 0.25f);
            default:
                return EditorStyles.label.normal.textColor;
        }
    }

    private sealed class AutoRunConsoleEntry
    {
        public AutoRunConsoleEntry(AutoRunLogLevel level, string text)
        {
            Level = level;
            Text = text;
            DisplayText = FormatConsoleEntry(level, text);
        }

        public AutoRunLogLevel Level { get; }
        public string Text { get; }
        public string DisplayText { get; }
    }
}

public enum AutoRunLogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}
