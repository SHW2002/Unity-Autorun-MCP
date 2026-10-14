using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public static class McpProcessService
{
    private const int MaxMcpVersionLength = 36;
    private const int MaximumVersionCacheEntries = 128;
    private const int ProcessScanTimeoutMilliseconds = 5000;
    private static readonly StringComparer BinaryPathComparer = Environment.OSVersion.Platform == PlatformID.Win32NT
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly object ProcessScanLock = new object();
    private static readonly object VersionCacheLock = new object();
    private static readonly object VersionTasksLock = new object();
    private static readonly Dictionary<string, VersionCacheEntry> McpVersionCache =
        new Dictionary<string, VersionCacheEntry>(BinaryPathComparer);
    private static readonly Dictionary<string, Task<string>> VersionTasks =
        new Dictionary<string, Task<string>>(BinaryPathComparer);
    private static Task<List<McpProcessInfo>> _activeProcessScan;
    private static long _versionCacheAccess;

    /// <summary>
    /// Starts an explicit background refresh. Concurrent refreshes share one scan;
    /// callers receive independent snapshots. This method never touches Unity APIs.
    /// Unlike the legacy synchronous entry point, scan failures fault the task so
    /// the UI can report a diagnostic rather than displaying a false empty list.
    /// </summary>
    public static Task<List<McpProcessInfo>> ListUnityAutorunProcessesAsync(string binaryPath = null)
    {
        Task<List<McpProcessInfo>> scan;
        lock (ProcessScanLock)
        {
            if (_activeProcessScan == null || _activeProcessScan.IsCompleted)
                _activeProcessScan = Task.Run(ListFromPowerShell);
            scan = _activeProcessScan;
        }
        return ReadProcessSnapshotAsync(scan, binaryPath);
    }

    private static async Task<List<McpProcessInfo>> ReadProcessSnapshotAsync(
        Task<List<McpProcessInfo>> scan, string binaryPath)
    {
        List<McpProcessInfo> processes = await scan.ConfigureAwait(false);
        return processes.Where(info => string.IsNullOrEmpty(binaryPath) || IsSameBinaryPath(info.BinaryPath, binaryPath))
            .Select(CloneProcessInfo).ToList();
    }

    public static Task<string> GetMcpVersionAsync(string binaryPath)
    {
        string path = NormalizePath(binaryPath);
        if (string.IsNullOrEmpty(path)) return Task.FromResult("unknown");
        lock (VersionTasksLock)
        {
            if (VersionTasks.TryGetValue(path, out Task<string> pending) && !pending.IsCompleted)
                return pending;
            // Keep only in-flight reads; the bounded metadata cache stores results.
            foreach (string completedPath in VersionTasks.Where(pair => pair.Value.IsCompleted).Select(pair => pair.Key).ToArray())
                VersionTasks.Remove(completedPath);
            Task<string> task = Task.Run(() => GetMcpVersion(path));
            if (VersionTasks.Count < MaximumVersionCacheEntries) VersionTasks[path] = task;
            return task;
        }
    }

    public static List<McpProcessInfo> ListUnityAutorunProcesses()
    {
        try
        {
            return ListFromPowerShell();
        }
        catch
        {
            return new List<McpProcessInfo>();
        }
    }

    public static List<McpProcessInfo> ListUnityAutorunProcesses(string binaryPath)
    {
        return ListUnityAutorunProcesses()
            .Where(info => IsSameBinaryPath(info.BinaryPath, binaryPath))
            .ToList();
    }

    public static bool TryTerminateUnityAutorunProcesses(
        string binaryPath,
        int waitForExitMilliseconds,
        out List<McpProcessInfo> terminatedProcesses,
        out string error)
    {
        List<McpProcessInfo> matchingProcesses = ListUnityAutorunProcesses(binaryPath);
        terminatedProcesses = new List<McpProcessInfo>();
        var failures = new List<string>();

        foreach (McpProcessInfo processInfo in matchingProcesses)
        {
            if (TryTerminateProcess(processInfo, waitForExitMilliseconds, out string processError))
            {
                terminatedProcesses.Add(processInfo);
            }
            else
            {
                failures.Add(processError);
            }
        }

        error = string.Join("; ", failures.ToArray());
        return failures.Count == 0;
    }

    private static List<McpProcessInfo> ListFromPowerShell()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            return new List<McpProcessInfo>();
        }

        // Process name discovery is cheap and avoids starting PowerShell when no
        // dotnet/apphost process could be a Unity Autorun MCP server.
        if (!HasPotentialMcpProcesses()) return new List<McpProcessInfo>();

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \""
                + "[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false);"
                + "$ErrorActionPreference='Stop';"
                + "$all=Get-CimInstance Win32_Process -Property ProcessId,ParentProcessId,Name,CommandLine,ExecutablePath;"
                + "$byId=@{};foreach($process in $all){$byId[[int]$process.ProcessId]=$process};"
                + "$p=$all | Where-Object {($_.Name -eq 'UnityAutorun.Mcp.exe' -and $_.CommandLine -like '* mcp*')"
                + " -or ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -like '*UnityAutorun.Mcp.dll*' -and $_.CommandLine -like '* mcp*')};"
                + "$p | ForEach-Object {"
                + "$item=$_;$parent=$byId[[int]$item.ParentProcessId];"
                + "$ai=$parent;$depth=0;while($ai -and $depth -lt 64 -and $ai.Name -notmatch '^(codex(?:-cli)?|claude(?:-code)?|cursor|code(?: - insiders)?|windsurf)(\\.exe)?$'){"
                + "$ai=$byId[[int]$ai.ParentProcessId];$depth++};"
                + "if($depth -ge 64){$ai=$null};"
                + "$aiPid=if($ai){$ai.ProcessId}else{0};$aiName=if($ai){$ai.Name}else{'unknown'};"
                + "$parentName=if($parent){$parent.Name}else{'unknown'};"
                + "$exe=if($item.ExecutablePath){$item.ExecutablePath}else{''};"
                + "Write-Output ($item.ProcessId.ToString()+'|'+$item.Name+'|'+$item.ParentProcessId.ToString()+'|'+$parentName+'|'+$aiPid.ToString()+'|'+$aiName+'|'+$exe+'|'+$item.CommandLine)}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using Process process = Process.Start(startInfo);
        if (process == null) throw new InvalidOperationException("Could not start MCP process discovery.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(ProcessScanTimeoutMilliseconds))
        {
            try { process.Kill(); }
            catch { /* The helper may already have exited; never kill MCP servers here. */ }
            throw new TimeoutException("MCP process discovery exceeded " + ProcessScanTimeoutMilliseconds + " ms.");
        }
        if (!Task.WaitAll(new Task[] { outputTask, errorTask }, 1000))
            throw new TimeoutException("MCP process discovery output did not close after the helper exited.");
        string output = outputTask.GetAwaiter().GetResult();
        string error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("MCP process discovery failed: " + Shorten(CleanCommandLine(error)));
        return output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseLine)
            .Where(info => info != null)
            .OrderBy(info => info.ParentProcessName)
            .ThenBy(info => info.ParentProcessId)
            .ThenBy(info => info.ProcessId)
            .ToList();
    }

    private static bool HasPotentialMcpProcesses()
    {
        bool found = false;
        foreach (string name in new[] { "dotnet", "UnityAutorun.Mcp" })
        {
            foreach (Process process in Process.GetProcessesByName(name))
            {
                using (process) { found = true; }
            }
            if (found) return true;
        }
        return false;
    }

    private static McpProcessInfo CloneProcessInfo(McpProcessInfo info)
    {
        return new McpProcessInfo
        {
            ProcessId = info.ProcessId, ProcessName = info.ProcessName,
            ParentProcessId = info.ParentProcessId, ParentProcessName = info.ParentProcessName,
            AiProcessId = info.AiProcessId, AiProcessName = info.AiProcessName,
            McpVersion = info.McpVersion, PublishedAt = info.PublishedAt,
            BinaryPath = info.BinaryPath, CommandLine = info.CommandLine
        };
    }

    private static McpProcessInfo ParseLine(string line)
    {
        string[] parts = line.Split(new[] { '|' }, 8);
        if (parts.Length < 8 || !int.TryParse(parts[0], out int processId))
        {
            return null;
        }

        int.TryParse(parts[2], out int parentId);
        int.TryParse(parts[4], out int aiId);
        string binaryPath = NormalizePath(ResolveMainProgramPath(parts[1], parts[6], parts[7]));
        return new McpProcessInfo
        {
            ProcessId = processId,
            ProcessName = Safe(parts[1]),
            ParentProcessId = parentId,
            ParentProcessName = Safe(parts[3]),
            AiProcessId = aiId,
            AiProcessName = Safe(parts[5]),
            McpVersion = GetMcpVersion(binaryPath),
            PublishedAt = GetPublishedAt(binaryPath),
            BinaryPath = binaryPath,
            CommandLine = Shorten(CleanCommandLine(parts[7])),
        };
    }

    private static bool TryTerminateProcess(
        McpProcessInfo processInfo,
        int waitForExitMilliseconds,
        out string error)
    {
        try
        {
            using Process process = Process.GetProcessById(processInfo.ProcessId);
            if (!process.HasExited)
            {
                process.Kill();
            }

            if (!process.WaitForExit(waitForExitMilliseconds))
            {
                error = "PID " + processInfo.ProcessId + " did not exit within "
                    + waitForExitMilliseconds + " ms";
                return false;
            }

            error = "";
            return true;
        }
        catch (ArgumentException)
        {
            // 进程可能在枚举结束后自行退出，此时目标文件同样已经释放。
            error = "";
            return true;
        }
        catch (InvalidOperationException)
        {
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = "PID " + processInfo.ProcessId + " could not be terminated: " + ex.Message;
            return false;
        }
    }

    private static string GetPublishedAt(string binaryPath)
    {
        return string.IsNullOrEmpty(binaryPath) || !File.Exists(binaryPath)
            ? "unknown"
            : File.GetLastWriteTime(binaryPath).ToString("MMdd HHmm");
    }

    public static string GetMcpVersion(string binaryPath)
    {
        string path = NormalizePath(binaryPath);
        if (string.IsNullOrEmpty(path))
        {
            return "unknown";
        }

        try
        {
            var file = new FileInfo(path);
            file.Refresh();
            if (!file.Exists) return "unknown";
            long length = file.Length;
            long lastWriteTimeUtcTicks = file.LastWriteTimeUtc.Ticks;
            lock (VersionCacheLock)
            {
                if (McpVersionCache.TryGetValue(path, out VersionCacheEntry cached)
                    && cached.Length == length && cached.LastWriteTimeUtcTicks == lastWriteTimeUtcTicks)
                {
                    cached.LastAccess = ++_versionCacheAccess;
                    return cached.Version;
                }

                string version = NormalizeMcpVersion(ReadFileVersion(path));
                if (string.IsNullOrEmpty(version)) version = "unknown";
                if (!McpVersionCache.ContainsKey(path) && McpVersionCache.Count >= MaximumVersionCacheEntries)
                    McpVersionCache.Remove(McpVersionCache.OrderBy(pair => pair.Value.LastAccess).First().Key);
                McpVersionCache[path] = new VersionCacheEntry
                {
                    Length = length, LastWriteTimeUtcTicks = lastWriteTimeUtcTicks,
                    Version = version, LastAccess = ++_versionCacheAccess
                };
                return version;
            }
        }
        catch
        {
            return "unknown";
        }
    }

    private sealed class VersionCacheEntry
    {
        public long Length;
        public long LastWriteTimeUtcTicks;
        public string Version;
        public long LastAccess;
    }

    private static string ReadFileVersion(string executablePath)
    {
        try
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(executablePath);
            return !string.IsNullOrWhiteSpace(info.ProductVersion)
                ? info.ProductVersion
                : info.FileVersion;
        }
        catch
        {
            return "";
        }
    }

    private static string NormalizeMcpVersion(string value)
    {
        string compact = Regex.Replace(value ?? "", "\\s+", " ").Trim();
        Match match = Regex.Match(
            compact,
            "(?<!\\d)(\\d+(?:\\.\\d+)+(?:-[0-9A-Za-z.-]+)?)",
            RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        return compact.Length <= MaxMcpVersionLength
            ? compact
            : compact.Substring(0, MaxMcpVersionLength - 3) + "...";
    }

    private static string ResolveMainProgramPath(string processName, string executablePath, string commandLine)
    {
        if (string.Equals(processName, "UnityAutorun.Mcp.exe", StringComparison.OrdinalIgnoreCase)
            && File.Exists(executablePath))
        {
            return executablePath;
        }

        string fromCommandLine = MatchUnityAutorunBinary(commandLine);
        if (!string.IsNullOrEmpty(fromCommandLine))
        {
            return fromCommandLine;
        }

        return executablePath;
    }

    private static string MatchUnityAutorunBinary(string commandLine)
    {
        Match match = Regex.Match(
            Safe(commandLine),
            "((?:\"[^\"]*UnityAutorun\\.Mcp\\.(?:dll|exe)\")|(?:\\S*UnityAutorun\\.Mcp\\.(?:dll|exe)))",
            RegexOptions.IgnoreCase);

        return match.Success ? match.Value.Trim('"') : "";
    }

    private static bool IsSameBinaryPath(string left, string right)
    {
        string normalizedLeft = NormalizePath(left);
        string normalizedRight = NormalizePath(right);
        if (string.IsNullOrEmpty(normalizedLeft) || string.IsNullOrEmpty(normalizedRight))
        {
            return false;
        }

        StringComparison comparison = Environment.OSVersion.Platform == PlatformID.Win32NT
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(normalizedLeft, normalizedRight, comparison);
    }

    private static string NormalizePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        value = value.Trim().Trim('"');
        try
        {
            return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static string Shorten(string value)
    {
        value = Safe(value);
        return value.Length <= 140 ? value : value.Substring(0, 137) + "...";
    }

    private static string CleanCommandLine(string value)
    {
        return Regex.Replace(Safe(value), "\\s+", " ");
    }

    private static string Safe(string value)
    {
        return string.IsNullOrEmpty(value) ? "unknown" : value;
    }
}
