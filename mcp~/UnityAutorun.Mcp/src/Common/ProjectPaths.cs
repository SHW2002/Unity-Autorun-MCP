using System;
using System.IO;

namespace UnityAutorun.Mcp
{
    public static class ProjectPaths
    {
        public const string GeneratedScriptsRelativePath = "Assets/AutorunScripts/Editor";

        public static string ResolveProjectRootDirectory()
        {
            string configured = Environment.GetEnvironmentVariable("UNITY_AUTORUN_PROJECT_ROOT");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (Path.IsPathRooted(configured))
                {
                    string absolute = Path.GetFullPath(configured);
                    if (IsProjectRoot(absolute)) return absolute;
                }
                else
                {
                    for (DirectoryInfo directory = new DirectoryInfo(Environment.CurrentDirectory);
                         directory != null; directory = directory.Parent)
                    {
                        string candidate = Path.GetFullPath(Path.Combine(directory.FullName, configured));
                        if (IsProjectRoot(candidate)) return candidate;
                    }
                }
                throw new InvalidOperationException("UNITY_AUTORUN_PROJECT_ROOT does not resolve to a Unity project: " + configured);
            }

            foreach (string start in new[] {
                Environment.GetEnvironmentVariable("UNITY_AUTORUN_TOOL_ROOT"),
                Environment.CurrentDirectory, AppContext.BaseDirectory })
            {
                if (string.IsNullOrWhiteSpace(start)) continue;
                for (DirectoryInfo directory = new DirectoryInfo(Path.GetFullPath(start));
                     directory != null; directory = directory.Parent)
                {
                    if (IsProjectRoot(directory.FullName)) return directory.FullName;
                }
            }
            throw new InvalidOperationException("Cannot resolve the Unity project root. Set UNITY_AUTORUN_PROJECT_ROOT or run from inside a Unity project.");
        }

        public static string ResolveGeneratedScriptPath(string requestedPath)
        {
            if (string.IsNullOrWhiteSpace(requestedPath))
                throw new ArgumentException("A relative C# script path is required.");
            if (Path.IsPathRooted(requestedPath))
                throw new ArgumentException("Script path must be relative to Assets/AutorunScripts/Editor.");

            // Treat both separator styles consistently, including when the MCP runs on macOS/Linux.
            string normalized = requestedPath.Replace('\\', '/');
            string[] segments = normalized.Split('/');
            foreach (string segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == ".."
                    || segment.IndexOfAny(new[] { ':', '*', '?', '"', '<', '>', '|' }) >= 0
                    || segment.EndsWith(".", StringComparison.Ordinal)
                    || segment.EndsWith(" ", StringComparison.Ordinal))
                    throw new ArgumentException("Script path contains an invalid or traversing segment.");
            }
            if (!string.Equals(Path.GetExtension(normalized), ".cs", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Only .cs scripts can be created.");

            string root = Path.GetFullPath(Path.Combine(ResolveProjectRootDirectory(), GeneratedScriptsRelativePath));
            string target = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
            EnsureWithin(root, target);
            EnsureNoLinkedAncestors(root);
            EnsureNoLinkedAncestors(target);
            return target;
        }

        public static string ResolveGeneratedSourcePath(string sourcePath)
        {
            string projectRoot = ResolveProjectRootDirectory();
            string root = Path.GetFullPath(Path.Combine(projectRoot, GeneratedScriptsRelativePath));
            string target = Path.IsPathRooted(sourcePath) ? Path.GetFullPath(sourcePath)
                : Path.GetFullPath(Path.Combine(projectRoot, sourcePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
            EnsureWithin(root, target);
            if (!string.Equals(Path.GetExtension(target), ".cs", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Script source must be a .cs file.");
            EnsureNoLinkedAncestors(target);
            return target;
        }

        private static void EnsureWithin(string root, string target)
        {
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
                throw new ArgumentException("Script path is outside Assets/AutorunScripts/Editor.");
        }

        private static void EnsureNoLinkedAncestors(string path)
        {
            for (string current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            {
                if ((Directory.Exists(current) || File.Exists(current))
                    && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Script paths must not pass through symbolic links or junctions.");
            }
        }

        private static bool IsProjectRoot(string path)
        {
            return Directory.Exists(Path.Combine(path, "Assets"))
                && Directory.Exists(Path.Combine(path, "ProjectSettings"));
        }
    }
}
