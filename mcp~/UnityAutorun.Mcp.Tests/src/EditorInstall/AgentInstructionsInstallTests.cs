using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UnityAutorun.Mcp.Tests
{
    internal static class AgentInstructionsInstallTests
    {
        public static IEnumerable<(string Name, Action Test)> Cases()
        {
            yield return ("agent instructions status is read-only and first install creates a managed block", FirstInstall);
            yield return ("agent instructions append preserves user rules and repeat install preserves LF bytes", () => AppendAndRepeat("\n"));
            yield return ("agent instructions append preserves user rules and repeat install preserves CRLF bytes", () => AppendAndRepeat("\r\n"));
            yield return ("agent instructions update preserves surrounding UTF-8 BOM text", () => UpdatePreservesEncoding(new UTF8Encoding(true), "\r\n"));
            yield return ("agent instructions update preserves surrounding UTF-16 little-endian BOM text", () => UpdatePreservesEncoding(new UnicodeEncoding(false, true), "\n"));
            yield return ("agent instructions update preserves surrounding UTF-16 big-endian BOM text", () => UpdatePreservesEncoding(new UnicodeEncoding(true, true), "\r\n"));
            yield return ("agent instructions status detects changed content even at current version", CurrentVersionChangedContent);
            yield return ("agent instructions refuse future-version downgrade without changing bytes", FutureVersion);
            yield return ("agent instructions reject damaged or inline markers without changing bytes", DamagedMarkers);
            yield return ("agent instructions reject damaged BOM-encoded text without changing bytes", DamagedEncodedText);
            yield return ("agent instructions reject invalid roots and AGENTS directories without writes", InvalidTargets);
            yield return ("agent instructions modify only the requested Unity host root", HostIsolation);
            yield return ("agent instructions failed replacement preserves original file and removes staging files", FailedReplacement);
        }

        private static void FirstInstall()
        {
            WithUnityProject(root =>
            {
                string path = Path.Combine(root, "AGENTS.md");
                var missing = AgentInstructionsInstallService.GetStatus(root);
                Assert(missing.IsTargetValid && !missing.IsInstalled && !missing.IsCurrent, "Missing installation has incorrect status.");
                Assert(Path.GetFullPath(missing.FilePath) == Path.GetFullPath(path), "Status points outside the host root.");
                Assert(!File.Exists(path), "Reading status created the host rules file.");

                Install(root);
                string text = File.ReadAllText(path);
                Assert(Count(text, AgentInstructionsInstallService.BeginMarker) == 1, "First install did not create one begin marker.");
                Assert(Count(text, AgentInstructionsInstallService.EndMarker) == 1, "First install did not create one end marker.");
                Assert(text.Contains(VersionMarker(AgentInstructionsInstallService.Version)), "Installed block has no current version marker.");
                AssertCurrent(root);
            });
        }

        private static void AppendAndRepeat(string newline)
        {
            WithUnityProject(root =>
            {
                string path = Path.Combine(root, "AGENTS.md");
                string rules = "# 用户已有规则" + newline + newline + "保持用户约定，不删除此行。";
                File.WriteAllText(path, rules, new UTF8Encoding(false));
                byte[] original = File.ReadAllBytes(path);
                var missing = AgentInstructionsInstallService.GetStatus(root);
                Assert(!missing.IsInstalled && !missing.IsCurrent, "Unmanaged user rules were treated as installed.");
                Install(root);
                byte[] first = File.ReadAllBytes(path);
                string text = File.ReadAllText(path);
                Assert(text.StartsWith(rules, StringComparison.Ordinal), "Append changed the existing user rules.");
                Assert(first.Take(original.Length).SequenceEqual(original), "Append changed the original user-rule bytes or introduced a BOM.");
                Assert(Count(text, AgentInstructionsInstallService.BeginMarker) == 1, "Append created duplicate management blocks.");
                Assert(text.Substring(rules.Length).Replace(newline, "").IndexOfAny(new[] { '\r', '\n' }) < 0, "Managed block changed the host newline convention.");
                AssertCurrent(root);
                Install(root);
                Assert(first.SequenceEqual(File.ReadAllBytes(path)), "Repeated install changed bytes of a current installation.");
            });
        }

        private static void UpdatePreservesEncoding(Encoding encoding, string newline)
        {
            WithUnityProject(root =>
            {
                string path = Path.Combine(root, "AGENTS.md");
                string before = "# 用户前置规则" + newline + "规则 α" + newline + newline;
                string after = newline + newline + "## 用户后置规则" + newline + "规则 β";
                string old = before + Block("旧提示词", 0, newline) + after;
                File.WriteAllText(path, old, encoding);
                byte[] oldBytes = File.ReadAllBytes(path);
                var stale = AgentInstructionsInstallService.GetStatus(root);
                Assert(stale.IsInstalled && !stale.IsCurrent && !stale.IsInstalledNewer && stale.InstalledVersion == 0, "Old version has incorrect update status.");
                Assert(oldBytes.SequenceEqual(File.ReadAllBytes(path)), "Reading stale status modified the file.");
                Install(root);
                byte[] updatedBytes = File.ReadAllBytes(path);
                string updated = File.ReadAllText(path);
                Assert(updated.StartsWith(before, StringComparison.Ordinal) && updated.EndsWith(after, StringComparison.Ordinal), "Update changed text outside the managed block.");
                Assert(!updated.Contains("旧提示词"), "Update kept obsolete managed content.");
                Assert(updatedBytes.Take(encoding.GetPreamble().Length).SequenceEqual(encoding.GetPreamble()), "Update changed or removed the encoding BOM.");
                Assert(updatedBytes.Take(encoding.GetPreamble().Length + encoding.GetByteCount(before)).SequenceEqual(oldBytes.Take(encoding.GetPreamble().Length + encoding.GetByteCount(before))), "Update changed original bytes before the managed block.");
                Assert(updatedBytes.Skip(updatedBytes.Length - encoding.GetByteCount(after)).SequenceEqual(oldBytes.Skip(oldBytes.Length - encoding.GetByteCount(after))), "Update changed original bytes after the managed block.");
                Assert(updated.Replace(newline, "").IndexOfAny(new[] { '\r', '\n' }) < 0, "Update changed the newline convention.");
                AssertCurrent(root);
                Install(root);
                Assert(updatedBytes.SequenceEqual(File.ReadAllBytes(path)), "Repeated encoded install changed bytes.");
            });
        }

        private static void CurrentVersionChangedContent()
        {
            WithUnityProject(root =>
            {
                string path = Path.Combine(root, "AGENTS.md");
                File.WriteAllText(path, Block("提示词已被手动修改", AgentInstructionsInstallService.Version, "\n"), new UTF8Encoding(false));
                var stale = AgentInstructionsInstallService.GetStatus(root);
                Assert(stale.IsInstalled && !stale.IsCurrent && stale.InstalledVersion == AgentInstructionsInstallService.Version, "Version alone incorrectly marked altered instructions as current.");
                Install(root);
                Assert(!File.ReadAllText(path).Contains("提示词已被手动修改"), "Install did not refresh changed current-version content.");
                AssertCurrent(root);
            });
        }

        private static void FutureVersion()
        {
            WithUnityProject(root =>
            {
                string path = Path.Combine(root, "AGENTS.md");
                int future = AgentInstructionsInstallService.Version + 1;
                File.WriteAllText(path, Block("来自更高版本的提示词", future, "\n"), new UTF8Encoding(true));
                byte[] original = File.ReadAllBytes(path);
                var status = AgentInstructionsInstallService.GetStatus(root);
                Assert(status.IsInstalled && status.IsInstalledNewer && !status.IsCurrent && status.InstalledVersion == future, "Future-version status is incorrect.");
                Assert(!AgentInstructionsInstallService.Install(root, out string message), "Installer downgraded newer instructions.");
                Assert(!string.IsNullOrEmpty(message), "Downgrade refusal has no diagnostic.");
                Assert(original.SequenceEqual(File.ReadAllBytes(path)), "Downgrade refusal changed host rules bytes.");
            });
        }

        private static void DamagedMarkers()
        {
            WithUnityProject(root =>
            {
                string begin = AgentInstructionsInstallService.BeginMarker;
                string end = AgentInstructionsInstallService.EndMarker;
                string block = Block("preserved", AgentInstructionsInstallService.Version, "\n");
                string[] damaged =
                {
                    "User rules\n" + begin + "\nunfinished",
                    "User rules\n" + end + "\n",
                    end + "\n" + begin + "\n",
                    block + "\n" + block,
                    begin + "\n" + begin + "\n" + end + "\n",
                    begin + "\n" + end + "\n" + end + "\n",
                    "inline prefix " + begin + "\n" + end,
                    begin + " trailing content\n" + end,
                    begin + "\ninline prefix " + end,
                    begin + "\n" + end + " trailing content"
                };
                string path = Path.Combine(root, "AGENTS.md");
                foreach (string text in damaged)
                {
                    File.WriteAllText(path, text, new UTF8Encoding(false));
                    byte[] original = File.ReadAllBytes(path);
                    var status = AgentInstructionsInstallService.GetStatus(root);
                    Assert(!string.IsNullOrEmpty(status.Error) && !status.IsCurrent, "Damaged markers were not diagnosed: " + text);
                    Assert(original.SequenceEqual(File.ReadAllBytes(path)), "Reading damaged status modified the host rules.");
                    Assert(!AgentInstructionsInstallService.Install(root, out string message), "Installer accepted damaged markers: " + text);
                    Assert(!string.IsNullOrEmpty(message), "Damaged marker refusal has no diagnostic.");
                    Assert(original.SequenceEqual(File.ReadAllBytes(path)), "Rejected damaged installation changed user rules.");
                }
            });
        }

        private static void InvalidTargets()
        {
            WithTemporaryDirectory(temp =>
            {
                string nonexistent = Path.Combine(temp, "missing");
                string plain = Path.Combine(temp, "plain");
                string onlyAssets = Path.Combine(temp, "only-assets");
                string onlySettings = Path.Combine(temp, "only-settings");
                Directory.CreateDirectory(plain);
                Directory.CreateDirectory(Path.Combine(onlyAssets, "Assets"));
                Directory.CreateDirectory(Path.Combine(onlySettings, "ProjectSettings"));
                foreach (string target in new[] { null, "", nonexistent, plain, onlyAssets, onlySettings })
                {
                    Assert(!AgentInstructionsInstallService.GetStatus(target).IsTargetValid, "Invalid host root was accepted.");
                    Assert(!AgentInstructionsInstallService.Install(target, out string message), "Installer wrote to an invalid host root.");
                    Assert(!string.IsNullOrEmpty(message), "Invalid host root has no diagnostic.");
                }
                Assert(!Directory.Exists(nonexistent), "Invalid host installation created a missing directory.");
                Assert(!Directory.GetFiles(temp, "AGENTS.md", SearchOption.AllDirectories).Any(), "Invalid root installation wrote AGENTS.md.");

                string host = Path.Combine(temp, "host");
                CreateUnityProject(host);
                string directoryPath = Path.Combine(host, "AGENTS.md");
                Directory.CreateDirectory(directoryPath);
                string sentinel = Path.Combine(directoryPath, "preserve.txt");
                File.WriteAllText(sentinel, "preserved");
                Assert(!string.IsNullOrEmpty(AgentInstructionsInstallService.GetStatus(host).Error), "AGENTS.md directory collision has no diagnostic.");
                Assert(!AgentInstructionsInstallService.Install(host, out string collisionMessage), "Installer replaced an AGENTS.md directory.");
                Assert(!string.IsNullOrEmpty(collisionMessage) && File.ReadAllText(sentinel) == "preserved", "Rejected directory collision changed existing data.");
            });
        }

        private static void DamagedEncodedText()
        {
            WithUnityProject(root =>
            {
                string path = Path.Combine(root, "AGENTS.md");
                byte[][] malformed =
                {
                    new byte[] { 0xef, 0xbb, 0xbf, 0xff }, // UTF-8 BOM followed by an invalid byte.
                    new byte[] { 0xff, 0xfe, 0x41 }, // UTF-16 BOM followed by an incomplete code unit.
                    new byte[] { 0xff, 0xfe, 0x00, 0x00, 0x00, 0x00, 0x11, 0x00 } // UTF-32 LE U+110000 exceeds the Unicode range.
                };
                foreach (byte[] original in malformed)
                {
                    File.WriteAllBytes(path, original);
                    var status = AgentInstructionsInstallService.GetStatus(root);
                    Assert(!string.IsNullOrEmpty(status.Error) && !status.IsCurrent, "Damaged BOM-encoded text was not diagnosed.");
                    Assert(original.SequenceEqual(File.ReadAllBytes(path)), "Reading damaged encoded status modified the original bytes.");
                    Assert(!AgentInstructionsInstallService.Install(root, out string message), "Installer replaced invalid encoded bytes with decoded text.");
                    Assert(!string.IsNullOrEmpty(message), "Invalid encoded text refusal has no diagnostic.");
                    Assert(original.SequenceEqual(File.ReadAllBytes(path)), "Rejected encoded installation changed original bytes.");
                    Assert(Directory.GetFiles(root).Length == 1, "Rejected encoded installation leaked a staged file.");
                }
            });
        }

        private static void HostIsolation()
        {
            WithTemporaryDirectory(temp =>
            {
                string host = Path.Combine(temp, "host");
                string client = Path.Combine(temp, "client");
                CreateUnityProject(host);
                Directory.CreateDirectory(client);
                string clientRules = Path.Combine(client, "AGENTS.md");
                File.WriteAllText(clientRules, "# 客户端规则\n请保留原文。", new UTF8Encoding(true));
                byte[] original = File.ReadAllBytes(clientRules);
                string previousDirectory = Environment.CurrentDirectory;
                try
                {
                    Environment.CurrentDirectory = client;
                    Install(host);
                    AssertCurrent(host);
                    Assert(original.SequenceEqual(File.ReadAllBytes(clientRules)), "Host install changed the client's AGENTS.md.");
                    Assert(!File.Exists(Path.Combine(temp, "AGENTS.md")), "Host install wrote rules to the ancestor directory.");
                }
                finally { Environment.CurrentDirectory = previousDirectory; }
            });
        }

        private static void FailedReplacement()
        {
            // FileShare.Read blocks atomic replacement on Windows while permitting the status read.
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
            WithUnityProject(root =>
            {
                string path = Path.Combine(root, "AGENTS.md");
                File.WriteAllText(path, "# 用户规则\n" + Block("旧提示词", 0, "\n"), new UTF8Encoding(false));
                byte[] original = File.ReadAllBytes(path);
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Assert(!AgentInstructionsInstallService.Install(root, out string message), "Installer reported success despite a locked replacement target.");
                    Assert(!string.IsNullOrEmpty(message), "Failed replacement has no diagnostic.");
                }
                Assert(original.SequenceEqual(File.ReadAllBytes(path)), "Failed replacement damaged original host rules.");
                Assert(Directory.GetFiles(root).Length == 1, "Failed replacement leaked a staged file into the project root.");
            });
        }

        private static string VersionMarker(int version)
        {
            return "<!-- unity-autorun:agent-instructions:version=" + version + " -->";
        }

        private static string Block(string content, int version, string newline)
        {
            return AgentInstructionsInstallService.BeginMarker + newline + VersionMarker(version) + newline + content + newline + AgentInstructionsInstallService.EndMarker;
        }

        private static void Install(string root)
        {
            Assert(AgentInstructionsInstallService.Install(root, out string message), "Agent instructions installation failed: " + message);
        }

        private static void AssertCurrent(string root)
        {
            var status = AgentInstructionsInstallService.GetStatus(root);
            Assert(status.IsTargetValid && status.IsInstalled && status.IsCurrent && !status.IsInstalledNewer && status.InstalledVersion == AgentInstructionsInstallService.Version && string.IsNullOrEmpty(status.Error), "Installed instructions do not report a current valid status: " + status.Error);
        }

        private static int Count(string text, string value)
        {
            int count = 0;
            for (int index = 0; (index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length) count++;
            return count;
        }

        private static void WithUnityProject(Action<string> action)
        {
            WithTemporaryDirectory(root => { CreateUnityProject(root); action(root); });
        }

        private static void CreateUnityProject(string root)
        {
            Directory.CreateDirectory(Path.Combine(root, "Assets"));
            Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
        }

        private static void WithTemporaryDirectory(Action<string> action)
        {
            string root = Path.Combine(Path.GetTempPath(), "unity-autorun-agent-rules-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try { action(root); }
            finally { Directory.Delete(root, true); }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
