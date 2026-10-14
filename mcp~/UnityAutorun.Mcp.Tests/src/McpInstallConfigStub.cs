using System;

public sealed class McpInstallConfig
{
    public const string ServerName = "unity-autorun";

    public static McpInstallConfig TestInstance;
    public static string TestToolRootDirectory;

    public static string GetToolRootDirectory()
    {
        if (string.IsNullOrEmpty(TestToolRootDirectory))
            throw new InvalidOperationException("McpInstallConfig.TestToolRootDirectory must be set by the test fixture.");
        return TestToolRootDirectory;
    }

    public string PublishedDllPath = "";
    public string ProjectRoot = "";
    public string ClaudeServerJson = "{}";

    public static McpInstallConfig Create()
    {
        if (TestInstance == null)
        {
            throw new InvalidOperationException(
                "McpInstallConfig.TestInstance must be set by the test fixture.");
        }

        return TestInstance;
    }

    public string ToCodexTomlBlock(string projectRoot)
    {
        return "[mcp_servers.unity_autorun]\n"
            + "command = \"dotnet\"\n\n"
            + "[mcp_servers.unity_autorun.env]\n"
            + "UNITY_AUTORUN_PROJECT_ROOT = \"" + projectRoot + "\"";
    }

    public string ToProjectClaudeServerJson(string projectRoot)
    {
        return "{\"command\":\"dotnet\",\"env\":{\"UNITY_AUTORUN_PROJECT_ROOT\":\""
            + projectRoot
            + "\"}}";
    }

    public string ToClaudeServerJson()
    {
        return ClaudeServerJson;
    }
}
