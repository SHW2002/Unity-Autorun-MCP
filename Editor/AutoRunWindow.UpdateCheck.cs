using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public partial class AutoRunWindow
{
    private string _mcpVersionText = "Open settings to check versions";
    private string _mcpVersionSource;
    private Task<string> _mcpVersionTask;
    private bool _mcpVersionRefreshRequested;
    private void EnableUpdateCheck()
    {
        AutoRunUpdateCheckService.StatusChanged -= HandleUpdateStatusChanged;
        AutoRunUpdateCheckService.StatusChanged += HandleUpdateStatusChanged;
        AutoRunUpdateCheckService.CheckIfDue();
    }

    private void DisableUpdateCheck()
    {
        AutoRunUpdateCheckService.StatusChanged -= HandleUpdateStatusChanged;
    }

    private void HandleUpdateStatusChanged()
    {
        Repaint();
    }

    private void RenderVersionStatus()
    {
        GUILayout.BeginVertical(EditorStyles.helpBox);
        RenderVersionRow(
            "MCP",
            BuildMcpVersionText(),
            "Source version comes from UnityAutorun.Mcp.csproj. Published version "
                + "comes from the current Release DLL.");

        foreach (SkillInstallStatus skillStatus in GetSkillInstallStatuses())
        {
            RenderVersionRow("Skill", skillStatus.Name + " | " + BuildSkillVersionText(skillStatus),
                skillStatus.IsTargetValid ? skillStatus.InstallDirectory : skillStatus.Error);
        }
        if (_hasLegacySkill)
        {
            EditorGUILayout.HelpBox("Legacy unity-autorun is still installed. Review custom changes and uninstall it manually; this installer preserves it.", MessageType.Info);
        }

        ResponsiveRow updates = BeginResponsiveRow(8f);
        updates.Add(52f);
        GUILayout.Label("Updates", EditorStyles.miniLabel, GUILayout.Width(52));
        updates.Add(130f);
        GUILayout.Label(
            new GUIContent(BuildUpdateStatusText(), BuildUpdateStatusTooltip()),
            GetWrappedStyle(EditorStyles.miniLabel),
            GUILayout.MinWidth(0),
            GUILayout.ExpandWidth(true));
        updates.Add(90f);
        using (new EditorGUI.DisabledScope(AutoRunUpdateCheckService.IsChecking))
        {
            if (GUILayout.Button("Check Now", GUILayout.Width(90)))
            {
                AutoRunUpdateCheckService.CheckNow();
            }
        }

        updates.End();
        if (GUILayout.Button("Refresh Installed Versions", GUILayout.Width(180)))
        {
            _mcpVersionRefreshRequested = true;
            InvalidateSkillInstallStatus();
        }
        GUILayout.EndVertical();
    }

    private void RenderVersionRow(
        string label,
        string value,
        string tooltip)
    {
        ResponsiveRow row = BeginResponsiveRow(8f);
        row.Add(52f);
        GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(52));
        row.Add(130f);
        GUILayout.Label(
            new GUIContent(value, tooltip),
            GetWrappedStyle(EditorStyles.miniLabel),
            GUILayout.MinWidth(0),
            GUILayout.ExpandWidth(true));
        row.End();
    }

    private string BuildMcpVersionText() { return _mcpVersionText; }

    private void PollMcpVersionTask()
    {
        if (_mcpVersionTask != null && _mcpVersionTask.IsCompleted)
        {
            _mcpVersionText = _mcpVersionTask.IsFaulted
                ? "Version check failed: " + _mcpVersionTask.Exception.GetBaseException().Message
                : _mcpVersionTask.IsCanceled ? "Version check cancelled"
                : FormatMcpVersionText(_mcpVersionSource, _mcpVersionTask.Result);
            _mcpVersionTask = null;
            Repaint();
        }
        if (_mcpVersionTask != null || !_mcpVersionRefreshRequested) return;
        _mcpVersionRefreshRequested = false;
        try
        {
            _mcpVersionSource = McpInstallConfig.GetMcpSourceVersion();
            string publishedPath = McpInstallConfig.GetPublishedDllPath();
            if (!File.Exists(publishedPath)) _mcpVersionText = "source v" + _mcpVersionSource + " | not published";
            else
            {
                _mcpVersionText = "Checking versions…";
                _mcpVersionTask = McpProcessService.GetMcpVersionAsync(publishedPath);
            }
        }
        catch (Exception exception) { _mcpVersionText = "Version check failed: " + exception.Message; }
        Repaint();
    }

    private static string FormatMcpVersionText(string sourceVersion, string publishedVersion)
    {
        string suffix = "";
        if (AutoRunVersionComparer.TryCompare(
                sourceVersion,
                publishedVersion,
                out int comparison))
        {
            if (comparison > 0)
            {
                suffix = " | publish required";
            }
            else if (comparison < 0)
            {
                suffix = " | published is newer";
            }
            else
            {
                suffix = " | current";
            }
        }

        return "source v"
            + sourceVersion
            + " | published v"
            + publishedVersion
            + suffix;
    }

    private static string BuildSkillVersionText(SkillInstallStatus status)
    {
        string prefix = "bundled v" + status.BundledVersion + " | ";
        if (!status.IsTargetValid)
        {
            return prefix + "select target";
        }

        if (!status.IsInstalled)
        {
            return prefix + "not installed";
        }

        string suffix = "";
        if (status.IsCurrent)
        {
            suffix = " | current";
        }
        else if (status.IsUpdateAvailable)
        {
            suffix = " | update available";
        }
        else if (status.IsInstalledNewer)
        {
            suffix = " | installed is newer";
        }

        return prefix + "installed v" + status.InstalledVersion + suffix;
    }

    private static string BuildUpdateStatusText()
    {
        if (AutoRunUpdateCheckService.IsChecking)
        {
            return "checking...";
        }

        string latestVersion = AutoRunUpdateCheckService.LatestVersion;
        string currentVersion = McpInstallConfig.GetToolVersion();
        if (!string.IsNullOrEmpty(AutoRunUpdateCheckService.LastError))
        {
            return string.IsNullOrEmpty(latestVersion)
                ? "check failed"
                : "cached v" + latestVersion + " | check failed";
        }

        if (string.IsNullOrEmpty(latestVersion))
        {
            return "not checked";
        }

        if (!AutoRunVersionComparer.TryCompare(
                latestVersion,
                currentVersion,
                out int comparison))
        {
            return "latest v" + latestVersion;
        }

        if (comparison > 0)
        {
            return "v" + latestVersion + " available";
        }

        if (comparison < 0)
        {
            return "local v" + currentVersion + " is newer";
        }

        return "up to date | v" + currentVersion;
    }

    private static string BuildUpdateStatusTooltip()
    {
        string tooltip = "Automatically checks once every 24 hours. Source: "
            + AutoRunUpdateCheckService.LatestPackageUrl
            + "\nRepository: "
            + AutoRunUpdateCheckService.RepositoryUrl;
        DateTime? checkedUtc = AutoRunUpdateCheckService.LastSuccessfulCheckUtc;
        if (checkedUtc.HasValue)
        {
            tooltip += "\nLast successful check: "
                + checkedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }

        if (!string.IsNullOrEmpty(AutoRunUpdateCheckService.LastError))
        {
            tooltip += "\nLast error: " + AutoRunUpdateCheckService.LastError;
        }

        if (AutoRunUpdateCheckService.IsUpdateAvailable)
        {
            tooltip += "\nUpdate the Unity package, then publish MCP and update the Skill.";
        }

        return tooltip;
    }
}
