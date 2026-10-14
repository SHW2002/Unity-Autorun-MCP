using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public partial class AutoRunWindow
{
    private const string McpInstallTargetKey = "UnityAutorunTool.McpInstallTarget";
    private string _mcpInstallTargetPath;
    [SerializeField] private bool _showMcpSettings;

    private void RenderMcpInstallControls()
    {
        LoadMcpInstallTargetPath();

        ResponsiveRow targetRow = BeginResponsiveRow();
        targetRow.Add(180f);
        string nextTargetPath = GUILayout.TextField(
            _mcpInstallTargetPath,
            GUILayout.MinWidth(0),
            GUILayout.ExpandWidth(true));
        if (!string.Equals(
                nextTargetPath,
                _mcpInstallTargetPath,
                StringComparison.OrdinalIgnoreCase))
        {
            _mcpInstallTargetPath = nextTargetPath;
            EditorPrefs.SetString(McpInstallTargetKey, nextTargetPath);
            InvalidateSkillInstallStatus();
        }

        targetRow.Add(70f);
        if (GUILayout.Button("Select", GUILayout.Width(70)))
        {
            string selected = EditorUtility.OpenFolderPanel(
                "Select .codex, project .claude, or Claude Desktop config folder",
                _mcpInstallTargetPath,
                "");
            if (!string.IsNullOrEmpty(selected))
            {
                _mcpInstallTargetPath = selected;
                EditorPrefs.SetString(McpInstallTargetKey, selected);
                InvalidateSkillInstallStatus();
            }
        }

        targetRow.End();

        ResponsiveRow installActions = BeginResponsiveRow();
        installActions.Add(100f);

        if (GUILayout.Button("Install MCP", GUILayout.Width(100)))
        {
            string message;
            if (McpInstallService.Install(_mcpInstallTargetPath, out message))
            {
                AppendConsoleText(message);
                EditorUtility.DisplayDialog("MCP Install", message, "OK");
            }
            else
            {
                AppendConsoleText(message, AutoRunLogLevel.Error);
                EditorUtility.DisplayDialog("MCP Install Failed", message, "OK");
            }
        }

        installActions.Add(120f);
        RenderSkillInstallButton();
        installActions.End();
        GUILayout.Label(
            "Select .codex or a project .claude folder for MCP + Skills; a Claude Desktop config folder supports MCP only.",
            GetWrappedStyle(EditorStyles.label),
            GUILayout.MinWidth(0),
            GUILayout.ExpandWidth(true)
        );
    }

    private void LoadMcpInstallTargetPath()
    {
        if (_mcpInstallTargetPath == null)
        {
            _mcpInstallTargetPath = EditorPrefs.GetString(
                McpInstallTargetKey,
                "");
        }
    }

    private void RenderMcpTools()
    {
        ResponsiveRow row = BeginResponsiveRow();

        row.Add(110f);
        if (GUILayout.Button(
                new GUIContent(
                    "Publish MCP",
                    "Stops matching MCP processes before publishing and waits for clients to reconnect."),
                GUILayout.Width(110)))
        {
            string message;
            bool published = McpPublishService.PublishCurrentVersion(out message, out bool reconnectPending);
            RefreshMcpProcesses();
            _mcpVersionRefreshRequested = true;

            if (published)
            {
                AppendConsoleText(
                    message,
                    reconnectPending ? AutoRunLogLevel.Warning : AutoRunLogLevel.Info);
                if (reconnectPending)
                {
                    EditorUtility.DisplayDialog(
                        "MCP Published - Reconnect Required",
                        message,
                        "OK");
                }
            }
            else
            {
                AppendConsoleText(message, AutoRunLogLevel.Error);
                EditorUtility.DisplayDialog("Publish MCP Failed", message, "OK");
            }
        }

        row.Add(95f);
        if (GUILayout.Button("Open Root", GUILayout.Width(95)))
        {
            string message;
            if (OpenToolRootDirectory(out message))
            {
                AppendConsoleText(message);
            }
            else
            {
                AppendConsoleText(message, AutoRunLogLevel.Error);
                EditorUtility.DisplayDialog("Open Root Failed", message, "OK");
            }
        }

        row.Add(260f);
        GUILayout.Label(
            "Publish the MCP server or open its root folder.",
            GetWrappedStyle(EditorStyles.label),
            GUILayout.MinWidth(0),
            GUILayout.ExpandWidth(true)
        );
        row.End();
    }

    private static bool OpenToolRootDirectory(out string message)
    {
        try
        {
            string root = McpInstallConfig.GetToolRootDirectory();
            if (!Directory.Exists(root))
            {
                message = "Tool root directory does not exist: " + root;
                return false;
            }

            EditorUtility.RevealInFinder(root);
            message = "Opened tool root directory: " + root;
            return true;
        }
        catch (Exception ex)
        {
            message = "Open tool root directory failed: " + ex.Message;
            return false;
        }
    }

    private void RenderMcpPanel()
    {
        BeginPanel("Connection");
        GUILayout.Label("Bridge " + (AutoRunBridgeController.IsRunning ? "Running" : "Stopped")
            + " | v" + McpInstallConfig.GetToolVersion(), EditorStyles.miniLabel);
        bool wasOpen = _showMcpSettings;
        _showMcpSettings = EditorGUILayout.Foldout(_showMcpSettings, "MCP Settings & Diagnostics", true);
        if (_showMcpSettings && !wasOpen)
        {
            RefreshMcpProcesses();
            _mcpVersionRefreshRequested = true;
            InvalidateSkillInstallStatus();
        }
        if (!_showMcpSettings)
        {
            EndPanel();
            return;
        }

        GUILayout.Label("Bridge");
        RenderBridgeControls();
        GUILayout.Space(4);
        GUILayout.Label("Versions");
        RenderVersionStatus();
        GUILayout.Space(4);
        GUILayout.Label("Processes");
        RenderMcpProcesses();
        GUILayout.Space(4);
        GUILayout.Label("Install");
        RenderMcpInstallControls();
        GUILayout.Space(4);
        GUILayout.Label("Tools");
        RenderMcpTools();
        EndPanel();
    }
}
