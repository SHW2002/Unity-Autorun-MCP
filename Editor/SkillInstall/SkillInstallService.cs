using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public sealed class SkillInstallStatus
{
    public string Name = "";
    public bool IsTargetValid;
    public bool IsInstalled;
    public string ClientName = "";
    public string InstallDirectory = "";
    public string BundledVersion = "unknown";
    public string InstalledVersion = "";
    public string Error = "";
    public bool IsCurrent { get { return CompareVersion(0); } }
    public bool IsInstalledNewer
    {
        get
        {
            int comparison;
            return AutoRunVersionComparer.TryCompare(InstalledVersion, BundledVersion, out comparison) && comparison > 0;
        }
    }
    public bool IsUpdateAvailable { get { return CompareVersion(-1); } }

    private bool CompareVersion(int expected)
    {
        int comparison;
        return IsInstalled && AutoRunVersionComparer.TryCompare(InstalledVersion, BundledVersion, out comparison)
            && Math.Sign(comparison) == expected;
    }
}

public static class SkillInstallService
{
    public const string SkillName = "gen-script";
    public const string VersionFileName = "skill-version.json";
    private static readonly string[] Names = { "gen-script", "run-script" };

    public static List<SkillInstallStatus> GetStatuses(string targetFolder)
    {
        var result = new List<SkillInstallStatus>();
        foreach (string name in Names) result.Add(GetStatus(targetFolder, name));
        return result;
    }
    public static SkillInstallStatus GetStatus(string targetFolder) { return GetStatus(targetFolder, SkillName); }
    public static SkillInstallStatus GetStatus(string targetFolder, string name)
    {
        var status = new SkillInstallStatus { Name = name };
        try
        {
            status.BundledVersion = ReadVersion(BundledDirectory(name), name);
            if (string.IsNullOrEmpty(status.BundledVersion)) status.BundledVersion = "unknown";
            string client, directory, error;
            status.IsTargetValid = TryResolveInstallDirectory(targetFolder, name, out client, out directory, out error);
            status.ClientName = client;
            status.InstallDirectory = directory;
            status.Error = error;
            if (!status.IsTargetValid) return status;
            status.IsInstalled = File.Exists(Path.Combine(directory, "SKILL.md"));
            status.InstalledVersion = Directory.Exists(directory) ? ReadVersion(directory, name) : "";
            if (status.IsInstalled && string.IsNullOrEmpty(status.InstalledVersion)) status.InstalledVersion = "unknown";
        }
        catch (Exception exception) { status.IsTargetValid = false; status.Error = exception.Message; }
        return status;
    }

    /// <summary>Stage the whole bundle first; a failed exchange restores all prior installs.</summary>
    public static bool Install(string targetFolder, out string message)
    {
        var plans = new List<InstallPlan>();
        string operationId = Guid.NewGuid().ToString("N");
        try
        {
            foreach (SkillInstallStatus status in GetStatuses(targetFolder))
            {
                if (!status.IsTargetValid) throw new InvalidOperationException(status.Error);
                if (status.IsInstalledNewer)
                    throw new InvalidOperationException(status.Name + " installed v" + status.InstalledVersion
                        + " is newer than bundled v" + status.BundledVersion + ". Update the package first.");
                string source = BundledDirectory(status.Name);
                if (!File.Exists(Path.Combine(source, "SKILL.md")) || status.BundledVersion == "unknown")
                    throw new InvalidOperationException("Bundled Skill is incomplete: " + status.Name);
                if (File.Exists(status.InstallDirectory))
                    throw new InvalidOperationException("Skill destination is a file: " + status.InstallDirectory);
                DirectoryInfo owner = Directory.GetParent(Path.GetDirectoryName(status.InstallDirectory));
                if (owner == null) throw new InvalidOperationException("Cannot resolve installer directory.");
                string work = Path.Combine(owner.FullName, ".unity-autorun-installer", operationId);
                plans.Add(new InstallPlan { Status = status, Source = source, Work = work,
                    Staging = Path.Combine(work, status.Name + ".installing"), Backup = Path.Combine(work, status.Name + ".backup") });
            }
            foreach (InstallPlan plan in plans) CopyDirectory(plan.Source, plan.Staging);
            foreach (InstallPlan plan in plans)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(plan.Status.InstallDirectory));
                if (Directory.Exists(plan.Status.InstallDirectory))
                {
                    Directory.Move(plan.Status.InstallDirectory, plan.Backup);
                    plan.PreviousMoved = true;
                }
                Directory.Move(plan.Staging, plan.Status.InstallDirectory);
                plan.NewMoved = true;
            }
        }
        catch (Exception exception)
        {
            var errors = new List<string>();
            for (int i = plans.Count - 1; i >= 0; i--) Rollback(plans[i], errors);
            CleanupEmptyDirectories(plans);
            message = "Skill bundle install failed: " + exception.Message;
            if (errors.Count > 0) message += " Rollback issues: " + string.Join("; ", errors.ToArray());
            return false;
        }
        var warnings = new List<string>();
        foreach (InstallPlan plan in plans)
        {
            try { if (Directory.Exists(plan.Backup)) Directory.Delete(plan.Backup, true); }
            catch (Exception exception) { warnings.Add(plan.Status.Name + ": " + exception.Message); }
        }
        CleanupEmptyDirectories(plans);
        message = "Installed gen-script and run-script for " + plans[0].Status.ClientName + ".";
        if (HasLegacySkill(targetFolder)) message += " Legacy unity-autorun was preserved; uninstall it manually after reviewing custom changes.";
        if (warnings.Count > 0) message += " Backup cleanup skipped: " + string.Join("; ", warnings.ToArray());
        return true;
    }

    public static bool HasLegacySkill(string targetFolder)
    {
        string client, directory, error;
        if (!TryResolveInstallDirectory(targetFolder, SkillName, out client, out directory, out error)) return false;
        return File.Exists(Path.Combine(Path.GetDirectoryName(directory), "unity-autorun", "SKILL.md"));
    }
    public static bool TryResolveInstallDirectory(string targetFolder, out string clientName, out string installDirectory, out string error)
    { return TryResolveInstallDirectory(targetFolder, SkillName, out clientName, out installDirectory, out error); }
    public static bool TryResolveInstallDirectory(string targetFolder, string name, out string clientName, out string installDirectory, out string error)
    {
        clientName = ""; installDirectory = ""; error = "";
        if (Array.IndexOf(Names, name) < 0) { error = "Unknown bundled Skill: " + name; return false; }
        if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder))
        { error = "Select an existing .codex or Claude Code project .claude folder first."; return false; }
        var target = new DirectoryInfo(Path.GetFullPath(targetFolder));
        if (string.Equals(target.Name, ".codex", StringComparison.OrdinalIgnoreCase) && target.Parent != null)
        {
            clientName = "Codex";
            installDirectory = Path.Combine(target.Parent.FullName, ".agents", "skills", name);
            return true;
        }
        if (string.Equals(target.Name, ".claude", StringComparison.OrdinalIgnoreCase))
        {
            clientName = "Claude Code";
            installDirectory = Path.Combine(target.FullName, "skills", name);
            return true;
        }
        error = File.Exists(Path.Combine(target.FullName, "claude_desktop_config.json"))
            ? "Claude Desktop supports MCP here, but filesystem Skills require a Claude Code project .claude folder."
            : "Target folder must be named .codex or .claude.";
        return false;
    }
    public static string GetBundledVersion() { return GetBundledVersion(SkillName); }
    public static string GetBundledVersion(string name)
    {
        try { string version = ReadVersion(BundledDirectory(name), name); return string.IsNullOrEmpty(version) ? "unknown" : version; }
        catch { return "unknown"; }
    }
    private static string BundledDirectory(string name) { return Path.Combine(McpInstallConfig.GetToolRootDirectory(), "skill~", name); }
    private static string ReadVersion(string directory, string name)
    {
        string path = Path.Combine(directory, VersionFileName);
        if (!File.Exists(path)) return "";
        try
        {
            SkillVersionDocument document = JsonUtility.FromJson<SkillVersionDocument>(File.ReadAllText(path));
            return document != null && string.Equals(document.name, name, StringComparison.Ordinal) ? (document.version ?? "").Trim() : "";
        }
        catch { return ""; }
    }
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (string child in Directory.GetDirectories(source)) CopyDirectory(child, Path.Combine(destination, Path.GetFileName(child)));
    }
    private static void Rollback(InstallPlan plan, List<string> errors)
    {
        try { if (plan.NewMoved && Directory.Exists(plan.Status.InstallDirectory)) Directory.Delete(plan.Status.InstallDirectory, true); }
        catch (Exception exception) { errors.Add(plan.Status.Name + " remove new: " + exception.Message); }
        try
        {
            if (plan.PreviousMoved && Directory.Exists(plan.Backup) && !Directory.Exists(plan.Status.InstallDirectory))
                Directory.Move(plan.Backup, plan.Status.InstallDirectory);
        }
        catch (Exception exception) { errors.Add(plan.Status.Name + " restore previous: " + exception.Message); }
        try { if (Directory.Exists(plan.Staging)) Directory.Delete(plan.Staging, true); }
        catch (Exception exception) { errors.Add(plan.Status.Name + " remove staging: " + exception.Message); }
    }
    private static void CleanupEmptyDirectories(List<InstallPlan> plans)
    {
        foreach (InstallPlan plan in plans)
        {
            try { if (Directory.Exists(plan.Work) && Directory.GetFileSystemEntries(plan.Work).Length == 0) Directory.Delete(plan.Work); }
            catch { /* Leave a recoverable backup if cleanup fails. */ }
        }
    }
    private sealed class InstallPlan
    {
        public SkillInstallStatus Status;
        public string Source, Work, Staging, Backup;
        public bool PreviousMoved, NewMoved;
    }
    [Serializable] private sealed class SkillVersionDocument { public string name = ""; public string version = ""; }
}
