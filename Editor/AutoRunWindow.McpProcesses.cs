using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public partial class AutoRunWindow
{
    private const float McpProcessColumnGap = 12f;
    private const float McpProcessColumnPadding = 4f;
    private const float McpProcessTablePadding = 28f;
    private const float McpProcessTableMinimumWidth = 520f;

    private List<McpProcessInfo> _mcpProcesses = new List<McpProcessInfo>();
    private double _mcpProcessLastRefreshAt;
    private Task<List<McpProcessInfo>> _mcpProcessTask;
    private bool _mcpProcessRefreshRequested;
    private string _mcpProcessError;

    private void RenderMcpProcesses()
    {
        ResponsiveRow summary = BeginResponsiveRow();
        summary.Add(140f);
        if (GUILayout.Button("Refresh Processes", GUILayout.Width(140)))
        {
            RefreshMcpProcesses();
        }

        summary.Add(240f);
        GUILayout.Label(
            new GUIContent(
                "MCP stdio processes and their server versions.",
                "MCP Version is read from the UnityAutorun.Mcp executable or DLL "
                + "loaded by each process."),
            GetWrappedStyle(EditorStyles.label),
            GUILayout.MinWidth(0),
            GUILayout.ExpandWidth(true)
        );
        summary.End();
        if (_mcpProcessTask != null) GUILayout.Label("Refreshing…", EditorStyles.miniLabel);
        if (!string.IsNullOrEmpty(_mcpProcessError)) EditorGUILayout.HelpBox(_mcpProcessError, MessageType.Warning);

        float tableWidth = Mathf.Max(1f, GetWindowContentWidth() - McpProcessTablePadding);
        if (tableWidth < McpProcessTableMinimumWidth)
        {
            RenderCompactMcpProcesses();
            return;
        }

        float[] widths = CalculateMcpProcessColumnWidths(tableWidth);
        RenderMcpProcessRow(
            "MCP PID",
            "MCP Process",
            "AI PID",
            "AI Process",
            "MCP Version",
            "Published",
            EditorStyles.boldLabel,
            widths);
        if (_mcpProcesses.Count == 0)
        {
            RenderMcpProcessRow(
                "-",
                "No UnityAutorun.Mcp process found.",
                "-",
                "-",
                "-",
                "-",
                EditorStyles.miniLabel,
                widths);
            return;
        }

        foreach (McpProcessInfo process in _mcpProcesses)
        {
            RenderMcpProcessRow(
                process.ProcessId.ToString(),
                process.ProcessName,
                process.AiProcessId.ToString(),
                process.AiProcessName,
                process.McpVersion,
                process.PublishedAt,
                EditorStyles.miniLabel,
                widths);
        }
    }

    private void RenderCompactMcpProcesses()
    {
        if (_mcpProcesses.Count == 0)
        {
            GUILayout.Label(
                "No UnityAutorun.Mcp process found.",
                GetWrappedStyle(EditorStyles.miniLabel),
                GUILayout.MinWidth(0),
                GUILayout.ExpandWidth(true));
            return;
        }

        foreach (McpProcessInfo process in _mcpProcesses)
        {
            ResponsiveRow row = BeginResponsiveRow();
            row.Add(180f);
            GUILayout.Label(
                "MCP: " + process.ProcessId + " | " + process.ProcessName,
                GetWrappedStyle(EditorStyles.miniBoldLabel),
                GUILayout.MinWidth(0),
                GUILayout.ExpandWidth(true));

            row.Add(150f);
            GUILayout.Label(
                "AI: " + process.AiProcessId + " | " + process.AiProcessName,
                GetWrappedStyle(EditorStyles.miniLabel),
                GUILayout.MinWidth(0),
                GUILayout.ExpandWidth(true));

            row.Add(200f);
            GUILayout.Label(
                "MCP Version: " + process.McpVersion + " | Published: " + process.PublishedAt,
                GetWrappedStyle(EditorStyles.miniLabel),
                GUILayout.MinWidth(0),
                GUILayout.ExpandWidth(true));
            row.End();
            GUILayout.Space(2f);
        }
    }

    private void RenderMcpProcessRow(
        string mcpPid,
        string mcpProcess,
        string aiPid,
        string aiProcess,
        string mcpVersion,
        string published,
        GUIStyle style,
        float[] widths)
    {
        GUILayout.BeginHorizontal();
        GUIStyle cellStyle = GetSqueezedStyle(style);
        GUILayout.Label(mcpPid, cellStyle, GUILayout.Width(widths[0]));
        GUILayout.Space(McpProcessColumnGap);
        GUILayout.Label(mcpProcess, cellStyle, GUILayout.Width(widths[1]));
        GUILayout.Space(McpProcessColumnGap);
        GUILayout.Label(aiPid, cellStyle, GUILayout.Width(widths[2]));
        GUILayout.Space(McpProcessColumnGap);
        GUILayout.Label(aiProcess, cellStyle, GUILayout.Width(widths[3]));
        GUILayout.Space(McpProcessColumnGap);
        GUILayout.Label(mcpVersion, cellStyle, GUILayout.Width(widths[4]));
        GUILayout.Space(McpProcessColumnGap);
        GUILayout.Label(published, cellStyle, GUILayout.Width(widths[5]));
        GUILayout.EndHorizontal();
    }

    private float[] CalculateMcpProcessColumnWidths(float availableWidth)
    {
        float[] widths =
        {
            MeasureMcpProcessCell("MCP PID", EditorStyles.boldLabel),
            MeasureMcpProcessCell("MCP Process", EditorStyles.boldLabel),
            MeasureMcpProcessCell("AI PID", EditorStyles.boldLabel),
            MeasureMcpProcessCell("AI Process", EditorStyles.boldLabel),
            MeasureMcpProcessCell("MCP Version", EditorStyles.boldLabel),
            MeasureMcpProcessCell("Published", EditorStyles.boldLabel),
        };

        if (_mcpProcesses.Count == 0)
        {
            widths[1] = Mathf.Max(widths[1], MeasureMcpProcessCell("No UnityAutorun.Mcp process found.", EditorStyles.miniLabel));
            return FitMcpProcessColumnWidths(widths, availableWidth);
        }

        foreach (McpProcessInfo process in _mcpProcesses)
        {
            widths[0] = MaxCell(widths[0], process.ProcessId.ToString());
            widths[1] = MaxCell(widths[1], process.ProcessName);
            widths[2] = MaxCell(widths[2], process.AiProcessId.ToString());
            widths[3] = MaxCell(widths[3], process.AiProcessName);
            widths[4] = MaxCell(widths[4], process.McpVersion);
            widths[5] = MaxCell(widths[5], process.PublishedAt);
        }

        return FitMcpProcessColumnWidths(widths, availableWidth);
    }

    private static float[] FitMcpProcessColumnWidths(float[] widths, float availableWidth)
    {
        float availableForColumns = Mathf.Max(120f, availableWidth - McpProcessColumnGap * (widths.Length - 1));
        float total = 0f;
        for (int i = 0; i < widths.Length; i++)
        {
            total += widths[i];
        }

        if (total <= availableForColumns)
        {
            return widths;
        }

        float[] minimums =
        {
            50f,
            70f,
            42f,
            70f,
            70f,
            70f,
        };
        float minimumTotal = 0f;
        float flexibleTotal = 0f;
        for (int i = 0; i < widths.Length; i++)
        {
            minimumTotal += minimums[i];
            flexibleTotal += Mathf.Max(0f, widths[i] - minimums[i]);
        }

        float remaining = availableForColumns - minimumTotal;
        if (remaining <= 0f || flexibleTotal <= 0f)
        {
            float scale = availableForColumns / minimumTotal;
            for (int i = 0; i < widths.Length; i++)
            {
                widths[i] = Mathf.Max(28f, minimums[i] * scale);
            }

            return widths;
        }

        for (int i = 0; i < widths.Length; i++)
        {
            float flexible = Mathf.Max(0f, widths[i] - minimums[i]);
            widths[i] = minimums[i] + remaining * (flexible / flexibleTotal);
        }

        return widths;
    }

    private static float MaxCell(float current, string text)
    {
        return Mathf.Max(current, MeasureMcpProcessCell(text, EditorStyles.miniLabel));
    }

    private static float MeasureMcpProcessCell(string text, GUIStyle style)
    {
        return style.CalcSize(new GUIContent(text ?? "")).x + McpProcessColumnPadding;
    }

    private void RefreshMcpProcesses()
    {
        _mcpProcessRefreshRequested = true;
    }

    private void PollWindowTasks()
    {
        if (_mcpProcessTask != null && _mcpProcessTask.IsCompleted)
        {
            if (_mcpProcessTask.IsFaulted)
                _mcpProcessError = _mcpProcessTask.Exception.GetBaseException().Message;
            else if (!_mcpProcessTask.IsCanceled)
            {
                _mcpProcesses = _mcpProcessTask.Result;
                _mcpProcessError = null;
            }
            _mcpProcessTask = null;
            Repaint();
        }
        if (_mcpProcessTask == null && (_mcpProcessRefreshRequested
            || (_showMcpSettings && EditorApplication.timeSinceStartup - _mcpProcessLastRefreshAt > 10)))
        {
            _mcpProcessRefreshRequested = false;
            _mcpProcessLastRefreshAt = EditorApplication.timeSinceStartup;
            _mcpProcessTask = McpProcessService.ListUnityAutorunProcessesAsync();
            Repaint();
        }
        PollMcpVersionTask();
    }
}
