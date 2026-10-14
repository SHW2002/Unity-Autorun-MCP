using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class AutoRunWindow
{
    private const double SkillStatusRefreshIntervalSeconds = 2d;
    private List<SkillInstallStatus> _skillInstallStatuses;
    private string _skillInstallStatusTarget = "";
    private double _skillInstallStatusRefreshedAt = -1d;

    private void RenderSkillInstallButton()
    {
        List<SkillInstallStatus> statuses = GetSkillInstallStatuses();
        bool valid = statuses.TrueForAll(status => status.IsTargetValid && status.BundledVersion != "unknown");
        bool newer = statuses.Exists(status => status.IsInstalledNewer);
        bool current = statuses.TrueForAll(status => status.IsCurrent);
        bool installed = statuses.Exists(status => status.IsInstalled);
        string label = newer ? "Skills Newer" : current ? "Reinstall Skills" : installed ? "Update Skills" : "Install Skills";
        string tooltip = newer ? "An installed Skill is newer than this bundle. Update the Unity package first."
            : "Install gen-script and run-script as one batch. A failed install restores previous versions.";
        using (new EditorGUI.DisabledScope(!valid || newer))
        {
            if (!GUILayout.Button(new GUIContent(label, tooltip), GUILayout.Width(120))) return;
        }
        string message;
        if (SkillInstallService.Install(_mcpInstallTargetPath, out message))
        {
            InvalidateSkillInstallStatus();
            AppendConsoleText(message);
            EditorUtility.DisplayDialog("Skills Install", message, "OK");
        }
        else
        {
            AppendConsoleText(message, AutoRunLogLevel.Error);
            EditorUtility.DisplayDialog("Skills Install Failed", message, "OK");
        }
    }

    private List<SkillInstallStatus> GetSkillInstallStatuses()
    {
        string target = _mcpInstallTargetPath ?? "";
        if (_skillInstallStatuses == null || !string.Equals(target, _skillInstallStatusTarget, StringComparison.OrdinalIgnoreCase)
            || EditorApplication.timeSinceStartup - _skillInstallStatusRefreshedAt >= SkillStatusRefreshIntervalSeconds)
        {
            _skillInstallStatuses = SkillInstallService.GetStatuses(target);
            _skillInstallStatusTarget = target;
            _skillInstallStatusRefreshedAt = EditorApplication.timeSinceStartup;
        }
        return _skillInstallStatuses;
    }
    private void InvalidateSkillInstallStatus()
    {
        _skillInstallStatuses = null;
        _skillInstallStatusTarget = "";
        _skillInstallStatusRefreshedAt = -1d;
    }
}
