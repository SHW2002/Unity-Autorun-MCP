using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UnityAutorun.Mcp.Tests
{
    internal static class SkillInstallTests
    {
        private static readonly string[] Names = { "gen-script", "edit-script", "run-script" };

        public static IEnumerable<(string Name, Action Test)> Cases()
        {
            yield return ("Codex installs all three self-contained skills in .agents/skills", () => InstallsAll(".codex"));
            yield return ("Claude Code installs all three self-contained skills in .claude/skills", () => InstallsAll(".claude"));
            yield return ("newer installed edit-script blocks the entire skill batch", NewerEditBlocksBatch);
            yield return ("a damaged third skill prevents writing any package in the batch", DamagedThirdPackage);
            yield return ("a third-package staging failure preserves every installed skill", StagingFailure);
            yield return ("an edit-script exchange failure rolls back the earlier gen-script exchange", () => ExchangeFailure("edit-script"));
            yield return ("a run-script exchange failure rolls back both gen-script and edit-script", () => ExchangeFailure("run-script"));
        }

        private static void InstallsAll(string clientFolder)
        {
            WithFixture(clientFolder, fixture =>
            {
                List<SkillInstallStatus> missing = SkillInstallService.GetStatuses(fixture.Target);
                Assert(missing.Select(status => status.Name).SequenceEqual(Names), "Status does not expose the complete three-skill bundle.");
                Assert(missing.All(status => status.IsTargetValid && !status.IsInstalled), "Initial client status is incorrect.");
                Assert(!Directory.Exists(fixture.InstallRoot), "Reading status created a client install directory.");
                Assert(SkillInstallService.Install(fixture.Target, out string message), "Batch install failed: " + message);
                foreach (string name in Names)
                {
                    string destination = Path.Combine(fixture.InstallRoot, name);
                    var status = SkillInstallService.GetStatus(fixture.Target, name);
                    Assert(status.IsTargetValid && status.IsInstalled && status.IsCurrent, "Installed Skill does not report current status: " + name);
                    Assert(Path.GetFullPath(status.InstallDirectory) == Path.GetFullPath(destination), "Skill was routed to the wrong client directory: " + name);
                    Assert(status.ClientName == (clientFolder == ".codex" ? "Codex" : "Claude Code"), "Wrong client name was reported.");
                    AssertSame(Snapshot(Path.Combine(fixture.ToolRoot, "skill~", name)), destination, "Install did not copy the self-contained package: " + name);
                }
                if (clientFolder == ".codex")
                    Assert(!Directory.Exists(Path.Combine(fixture.Target, "skills")), "Codex Skills were written into the MCP config directory.");
                else Assert(!Directory.Exists(Path.Combine(fixture.HostRoot, ".agents")), "Claude Code installation wrote Codex Skills.");
                AssertNoStaging(fixture.HostRoot);
            });
        }

        private static void NewerEditBlocksBatch()
        {
            WithFixture(".codex", fixture =>
            {
                SeedInstalled(fixture);
                WriteVersion(Path.Combine(fixture.InstallRoot, "edit-script"), "edit-script", "999.0.0");
                Dictionary<string, byte[]> original = Snapshot(fixture.InstallRoot);
                var status = SkillInstallService.GetStatus(fixture.Target, "edit-script");
                Assert(status.IsInstalledNewer, "Future edit-script version was not recognized.");
                Assert(!SkillInstallService.Install(fixture.Target, out string message), "Installer downgraded edit-script and replaced the batch.");
                Assert(message.Contains("edit-script"), "Batch refusal did not identify the newer Skill.");
                AssertSame(original, fixture.InstallRoot, "Future edit-script rejection modified an installed Skill.");
                AssertNoStaging(fixture.HostRoot);
            });
        }

        private static void DamagedThirdPackage()
        {
            WithFixture(".claude", fixture =>
            {
                // Validation must inspect the complete batch before creating any destination.
                File.Delete(Path.Combine(fixture.ToolRoot, "skill~", "run-script", "SKILL.md"));
                Assert(!SkillInstallService.Install(fixture.Target, out string message), "Installer accepted an incomplete third package.");
                Assert(message.Contains("run-script"), "Incomplete package refusal did not identify the third Skill.");
                Assert(!Directory.Exists(fixture.InstallRoot), "Earlier packages were installed before third-package validation failed.");
                Assert(!Directory.GetFiles(fixture.HostRoot, "*", SearchOption.AllDirectories).Any(), "Invalid batch wrote files into the client project.");
                AssertNoStaging(fixture.HostRoot);
            });
        }

        private static void StagingFailure()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
            WithFixture(".codex", fixture =>
            {
                SeedInstalled(fixture);
                Dictionary<string, byte[]> original = Snapshot(fixture.InstallRoot);
                string source = Path.Combine(fixture.ToolRoot, "skill~", "run-script", "SKILL.md");
                using (new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Assert(!SkillInstallService.Install(fixture.Target, out string message), "Installer succeeded despite unreadable third-package content.");
                    Assert(!string.IsNullOrEmpty(message), "Staging failure has no diagnostic.");
                }
                AssertSame(original, fixture.InstallRoot, "Staging failure changed a previously installed Skill.");
                AssertNoStaging(fixture.HostRoot);
            });
        }

        private static void ExchangeFailure(string lockedSkill)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
            WithFixture(".codex", fixture =>
            {
                SeedInstalled(fixture);
                Dictionary<string, byte[]> original = Snapshot(fixture.InstallRoot);
                string lockedFile = Path.Combine(fixture.InstallRoot, lockedSkill, "SKILL.md");
                // Denying delete sharing prevents the package's backup-directory exchange.
                using (new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Assert(!SkillInstallService.Install(fixture.Target, out string message), "Installer succeeded despite a locked package exchange: " + lockedSkill);
                    Assert(!string.IsNullOrEmpty(message), "Exchange failure has no diagnostic.");
                    Assert(!message.Contains("Rollback issues:"), "The earlier exchanges could not be rolled back: " + message);
                }
                AssertSame(original, fixture.InstallRoot, "Failed " + lockedSkill + " exchange did not restore every installed Skill.");
                AssertNoStaging(fixture.HostRoot);
            });
        }

        private static void SeedInstalled(Fixture fixture)
        {
            foreach (string name in Names)
            {
                string path = Path.Combine(fixture.InstallRoot, name);
                Directory.CreateDirectory(Path.Combine(path, "references"));
                File.WriteAllText(Path.Combine(path, "SKILL.md"), "# 用户已有 " + name + "\n用户修改必须被保留。", new UTF8Encoding(true));
                File.WriteAllText(Path.Combine(path, "references", "custom.txt"), "original " + name, new UTF8Encoding(false));
                WriteVersion(path, name, "0.0.0");
            }
        }

        private static void WriteVersion(string directory, string name, string version)
        {
            File.WriteAllText(Path.Combine(directory, SkillInstallService.VersionFileName),
                "{\"name\":\"" + name + "\",\"version\":\"" + version + "\"}", new UTF8Encoding(false));
        }

        private static Dictionary<string, byte[]> Snapshot(string directory)
        {
            return Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetRelativePath(directory, path), File.ReadAllBytes, StringComparer.Ordinal);
        }

        private static void AssertSame(Dictionary<string, byte[]> expected, string directory, string message)
        {
            Assert(Directory.Exists(directory), message + " Destination directory is missing.");
            Dictionary<string, byte[]> actual = Snapshot(directory);
            Assert(expected.Count == actual.Count, message + " File count changed.");
            foreach (KeyValuePair<string, byte[]> pair in expected)
                Assert(actual.TryGetValue(pair.Key, out byte[] bytes) && pair.Value.SequenceEqual(bytes), message + " Changed file: " + pair.Key);
        }

        private static void AssertNoStaging(string hostRoot)
        {
            Assert(!Directory.GetDirectories(hostRoot, "*.installing", SearchOption.AllDirectories).Any(), "Installer leaked staged packages after completing the operation.");
            Assert(!Directory.GetDirectories(hostRoot, "*.backup", SearchOption.AllDirectories).Any(), "Installer leaked a package backup after a recoverable operation.");
        }

        private static void WithFixture(string clientFolder, Action<Fixture> action)
        {
            string root = Path.Combine(Path.GetTempPath(), "unity-autorun-skill-install-tests-" + Guid.NewGuid().ToString("N"));
            string previousToolRoot = McpInstallConfig.TestToolRootDirectory;
            Directory.CreateDirectory(root);
            try
            {
                var fixture = new Fixture
                {
                    ToolRoot = Path.Combine(root, "tool"),
                    HostRoot = Path.Combine(root, "host")
                };
                fixture.Target = Path.Combine(fixture.HostRoot, clientFolder);
                fixture.InstallRoot = clientFolder == ".codex"
                    ? Path.Combine(fixture.HostRoot, ".agents", "skills")
                    : Path.Combine(fixture.Target, "skills");
                Directory.CreateDirectory(fixture.Target);
                string sourceRoot = FindToolRoot();
                foreach (string name in Names)
                    CopyDirectory(Path.Combine(sourceRoot, "skill~", name), Path.Combine(fixture.ToolRoot, "skill~", name));
                McpInstallConfig.TestToolRootDirectory = fixture.ToolRoot;
                action(fixture);
            }
            finally
            {
                McpInstallConfig.TestToolRootDirectory = previousToolRoot;
                Directory.Delete(root, true);
            }
        }

        private static string FindToolRoot()
        {
            for (DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
                if (Names.All(name => File.Exists(Path.Combine(directory.FullName, "skill~", name, "SKILL.md")))) return directory.FullName;
            throw new InvalidOperationException("Cannot find the repository's three bundled Skills for installer tests.");
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            foreach (string child in Directory.GetDirectories(source)) CopyDirectory(child, Path.Combine(destination, Path.GetFileName(child)));
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class Fixture
        {
            public string ToolRoot, HostRoot, Target, InstallRoot;
        }
    }
}
