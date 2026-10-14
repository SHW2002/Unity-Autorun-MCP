using System;
using UnityEditor;
using UnityEngine;

public partial class AutoRunWindow
{
    private AgentInstructionsInstallStatus _agentInstructionsStatus;

    private void RenderAgentInstructionsControls()
    {
        AgentInstructionsInstallStatus status = GetAgentInstructionsStatus();
        string state = !status.IsTargetValid ? status.Error : status.IsInstalledNewer ? "Newer version installed"
            : status.IsCurrent ? "Current" : status.IsInstalled ? "Update available" : "Not installed";
        GUILayout.Label(new GUIContent("Host AGENTS.md | Agent Rules v" + AgentInstructionsInstallService.Version
            + " | " + state, status.FilePath), GetWrappedStyle(EditorStyles.label),
            GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));

        ResponsiveRow actions = BeginResponsiveRow();
        actions.Add(160f);
        string label = status.IsInstalledNewer ? "Agent Rules Newer" : status.IsCurrent ? "Agent Rules Current"
            : status.IsInstalled ? "Update Agent Rules" : "Install Agent Rules";
        bool install;
        using (new EditorGUI.DisabledScope(!status.IsTargetValid || status.IsInstalledNewer || status.IsCurrent))
        {
            install = GUILayout.Button(new GUIContent(label,
                "Write the self-test instructions into the host Unity project's AGENTS.md. Preserve content outside the marked block."),
                GUILayout.Width(160));
        }
        actions.Add(70f);
        if (GUILayout.Button("Refresh", GUILayout.Width(70))) InvalidateAgentInstructionsStatus();
        actions.End();

        if (!string.IsNullOrEmpty(status.FilePath))
            GUILayout.Label(status.FilePath, GetWrappedStyle(EditorStyles.miniLabel),
                GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));
        if (!install) return;

        string message;
        bool installed = AgentInstructionsInstallService.Install(McpInstallConfig.GetProjectRootDirectory(), out message);
        InvalidateAgentInstructionsStatus();
        AppendConsoleText(message, installed ? AutoRunLogLevel.Info : AutoRunLogLevel.Error);
        EditorUtility.DisplayDialog(installed ? "Agent Rules Installed" : "Agent Rules Install Failed", message, "OK");
    }

    private AgentInstructionsInstallStatus GetAgentInstructionsStatus()
    {
        if (_agentInstructionsStatus != null) return _agentInstructionsStatus;
        try { _agentInstructionsStatus = AgentInstructionsInstallService.GetStatus(McpInstallConfig.GetProjectRootDirectory()); }
        catch (Exception exception) { _agentInstructionsStatus = new AgentInstructionsInstallStatus { Error = exception.Message }; }
        return _agentInstructionsStatus;
    }

    private void InvalidateAgentInstructionsStatus() { _agentInstructionsStatus = null; }
}
