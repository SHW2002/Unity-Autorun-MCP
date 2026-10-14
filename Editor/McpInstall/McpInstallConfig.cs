using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor.PackageManager;

public sealed class McpInstallConfig
{
    public const string PackageName = "com.shthemw.unity-autorun-tool";
    public const string ServerName = "unity-autorun";
    private const string PackageFileName = "package.json";
    private static string _toolVersion;
    private static string _mcpSourceVersion;
    private static string _mcpProjectPath;
    private static string _toolRootDirectory;
    private static string _publishedDllPath;

    public string Command;
    public List<string> Args;
    public string ProjectRoot;
    public string ToolRoot;
    public string PublishedDllPath;

    public static McpInstallConfig Create()
    {
        string toolRoot = GetToolRootDirectory();
        string publishedDllPath = GetPublishedDllPath();

        return new McpInstallConfig
        {
            Command = "dotnet",
            Args = new List<string> { publishedDllPath, "mcp" },
            ProjectRoot = GetProjectRootDirectory(),
            ToolRoot = toolRoot,
            PublishedDllPath = publishedDllPath
        };
    }

    public static string GetMcpProjectPath()
    {
        if (string.IsNullOrEmpty(_mcpProjectPath))
        {
            _mcpProjectPath = FindMcpProjectPath();
        }

        return _mcpProjectPath;
    }

    public static string GetToolVersion()
    {
        if (!string.IsNullOrEmpty(_toolVersion))
        {
            return _toolVersion;
        }

        try
        {
            PackageInfo packageInfo =
                PackageInfo.FindForAssetPath("Packages/" + PackageName + "/package.json");
            if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.version))
            {
                _toolVersion = packageInfo.version;
                return _toolVersion;
            }

            string packagePath = Path.Combine(GetToolRootDirectory(), PackageFileName);
            if (File.Exists(packagePath))
            {
                PackageVersionDocument document =
                    UnityEngine.JsonUtility.FromJson<PackageVersionDocument>(
                        File.ReadAllText(packagePath));
                if (document != null && !string.IsNullOrEmpty(document.version))
                {
                    _toolVersion = document.version;
                    return _toolVersion;
                }
            }
        }
        catch
        {
            // 版本展示不应阻断 MCP 面板的其它功能。
        }

        _toolVersion = "unknown";
        return _toolVersion;
    }

    public static string GetMcpSourceVersion()
    {
        if (!string.IsNullOrEmpty(_mcpSourceVersion))
        {
            return _mcpSourceVersion;
        }

        try
        {
            string project = File.ReadAllText(GetMcpProjectPath());
            Match match = Regex.Match(
                project,
                "<Version>\\s*([^<]+?)\\s*</Version>",
                RegexOptions.IgnoreCase);
            if (match.Success)
            {
                _mcpSourceVersion = match.Groups[1].Value.Trim();
                return _mcpSourceVersion;
            }
        }
        catch
        {
            // 版本展示不应阻断安装面板的其它功能。
        }

        _mcpSourceVersion = "unknown";
        return _mcpSourceVersion;
    }

    public static string GetToolRootDirectory()
    {
        if (!string.IsNullOrEmpty(_toolRootDirectory))
        {
            return _toolRootDirectory;
        }

        string projectPath = GetMcpProjectPath();
        string projectDirectory = Path.GetDirectoryName(projectPath);
        DirectoryInfo mcpFolder = Directory.GetParent(projectDirectory);
        if (mcpFolder == null || mcpFolder.Parent == null)
        {
            throw new InvalidOperationException("Cannot resolve Unity AutoRun tool root directory.");
        }

        _toolRootDirectory = mcpFolder.Parent.FullName;
        return _toolRootDirectory;
    }

    public static string GetProjectRootDirectory()
    {
        DirectoryInfo projectRoot = Directory.GetParent(UnityEngine.Application.dataPath);
        if (projectRoot == null)
        {
            throw new InvalidOperationException("Cannot resolve the Unity project root directory.");
        }

        return projectRoot.FullName;
    }

    public static string GetPublishedDllPath()
    {
        if (!string.IsNullOrEmpty(_publishedDllPath))
        {
            return _publishedDllPath;
        }

        string projectPath = GetMcpProjectPath();
        string projectDirectory = Path.GetDirectoryName(projectPath);
        _publishedDllPath = Path.Combine(
            projectDirectory,
            "bin",
            "Release",
            "net8.0",
            "publish",
            "UnityAutorun.Mcp.dll");
        return _publishedDllPath;
    }

    public string ToCodexTomlBlock(string projectRoot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[mcp_servers.unity_autorun]");
        builder.AppendLine("command = " + TomlString(Command));
        builder.AppendLine("args = [" + TomlStringList(Args) + "]");
        builder.AppendLine("startup_timeout_sec = 20");
        builder.AppendLine("tool_timeout_sec = 60");
        builder.AppendLine("enabled = true");
        builder.AppendLine();
        builder.AppendLine("[mcp_servers.unity_autorun.env]");
        builder.AppendLine("UNITY_AUTORUN_PROJECT_ROOT = " + TomlString(projectRoot));
        return builder.ToString().TrimEnd();
    }

    public string ToProjectClaudeServerJson(string projectRoot)
    {
        return BuildClaudeServerJson(projectRoot, false);
    }

    public string ToClaudeServerJson()
    {
        return BuildClaudeServerJson(ProjectRoot, true);
    }

    private string BuildClaudeServerJson(string projectRoot, bool includeToolRoot)
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"command\":\"").Append(JsonEscape(Command)).Append("\",");
        builder.Append("\"args\":").Append(JsonStringArray(Args)).Append(",");
        builder.Append("\"env\":{");
        builder.Append("\"UNITY_AUTORUN_PROJECT_ROOT\":\"").Append(JsonEscape(projectRoot)).Append("\"");
        if (includeToolRoot)
        {
            builder.Append(",\"UNITY_AUTORUN_TOOL_ROOT\":\"").Append(JsonEscape(ToolRoot)).Append("\"");
        }
        builder.Append("}");
        builder.Append("}");
        return builder.ToString();
    }

    private static string FindMcpProjectPath()
    {
        string packageRoot = FindPackageRootDirectory();
        if (!string.IsNullOrEmpty(packageRoot))
        {
            string packageProjectPath = Path.Combine(packageRoot, "mcp~", "UnityAutorun.Mcp", "UnityAutorun.Mcp.csproj");
            if (File.Exists(packageProjectPath))
            {
                return Path.GetFullPath(packageProjectPath).Replace('\\', '/');
            }
        }

        string[] matches = Directory.GetFiles(UnityEngine.Application.dataPath, "UnityAutorun.Mcp.csproj", SearchOption.AllDirectories);
        if (matches.Length == 0)
        {
            throw new InvalidOperationException("Cannot find UnityAutorun.Mcp.csproj under the Unity AutoRun package or Assets.");
        }

        return Path.GetFullPath(matches[0]).Replace('\\', '/');
    }

    private static string FindPackageRootDirectory()
    {
        PackageInfo packageInfo = PackageInfo.FindForAssetPath("Packages/" + PackageName + "/package.json");
        if (packageInfo == null || string.IsNullOrEmpty(packageInfo.resolvedPath))
        {
            return null;
        }

        return Path.GetFullPath(packageInfo.resolvedPath);
    }

    private static string TomlStringList(List<string> values)
    {
        var parts = new List<string>();
        foreach (string value in values)
        {
            parts.Add(TomlString(value));
        }

        return string.Join(", ", parts.ToArray());
    }

    private static string TomlString(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static string JsonStringArray(List<string> values)
    {
        var parts = new List<string>();
        foreach (string value in values)
        {
            parts.Add("\"" + JsonEscape(value) + "\"");
        }

        return "[" + string.Join(",", parts.ToArray()) + "]";
    }

    private static string JsonEscape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    [Serializable]
    private sealed class PackageVersionDocument
    {
        public string version = "";
    }
}
