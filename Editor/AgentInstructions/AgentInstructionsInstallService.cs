using System;
using System.Globalization;
using System.IO;
using System.Text;

public sealed class AgentInstructionsInstallStatus
{
    public string FilePath = "";
    public bool IsTargetValid;
    public bool IsInstalled;
    public bool IsCurrent;
    public bool IsInstalledNewer;
    public int InstalledVersion;
    public string Error = "";
}

/// <summary>Maintains only the marked Unity Autorun rules in the host project's AGENTS.md.</summary>
public static class AgentInstructionsInstallService
{
    public const string BeginMarker = "<!-- unity-autorun:agent-instructions:begin -->";
    public const string EndMarker = "<!-- unity-autorun:agent-instructions:end -->";
    public const int Version = 1;
    private const string VersionPrefix = "<!-- unity-autorun:agent-instructions:version=";
    private const string Instructions = @"## Unity Autorun self-testing

When a task requires Unity gameplay or UI actions for self-testing, inspect available automation scripts before performing the test:

1. Read `get_script_guidance`, query `list_scripts`, and inspect candidates with `get_script(includeSource=true)`. Match the declared starting state, complete business flow, required parameters, dependencies, and observable final assertions. A shared target name or partial overlap does not establish a suitable test. For `preparePlayMode=true`, evaluate the declared business start after runner preparation.
2. If a compiled, valid script covers the requested test, use `run-script` to run it directly and verify its terminal result and assertions. Execution does not authorize edits. Do not create a duplicate test or quietly replace the flow.
3. If no matching script exists, report the missing coverage and ask whether the user wants a self-test script created for this exact flow. Do not silently generate one. If the user has already explicitly requested or authorized creating that script in the current task, proceed with `gen-script` without asking again.
4. An unavailable bridge, compilation failure, invalid dependency, or wrong starting state is a blocked test, not evidence that no script exists. Report the actual problem. Do not silently create a fallback script or change preconditions/assertions to hide it.

Use `gen-script` for an authorized new flow: generate, compile, self-test, and repair only files created by that task. Use `edit-script` for authorized maintenance of a specified existing business script while preserving its ID, starting state, flow, parameters, and result contract; inspect affected callers and verify them where applicable. A different starting state or flow requires a separate ID and source file. Do not modify reused dependencies or built-in scripts to fit a task.

Before editing script source, confirm `unity_status.data.scriptRunStateAvailable=true`, `data.hasActiveScriptRun=false`, and `data.isCompiling=false`. An unavailable run state must never be treated as an idle runner. Recompile after each change and use a fresh `runId` for each new self-test; reuse an ID only for transport retries of the same execution. Success, failure, timeout, and cancellation preserve the game state and Play Mode. Do not silently reset the game, replay completed actions, or stop Play Mode. Report actual compilation, execution, assertions, and any unverified coverage; compilation alone is not a passed self-test.";

    public static AgentInstructionsInstallStatus GetStatus(string projectRoot)
    {
        var status = new AgentInstructionsInstallStatus();
        try
        {
            status.FilePath = ResolveFilePath(projectRoot);
            Document document = ReadDocument(status.FilePath);
            Block block = FindBlock(document.Text);
            status.IsTargetValid = true;
            status.IsInstalled = block != null;
            status.InstalledVersion = block == null ? 0 : block.Version;
            status.IsInstalledNewer = status.InstalledVersion > Version;
            status.IsCurrent = block != null && !status.IsInstalledNewer
                && Normalize(document.Text.Substring(block.Start, block.End - block.Start)) == BuildBlock("\n");
        }
        catch (Exception exception) { status.Error = exception.Message; }
        return status;
    }

    public static bool Install(string projectRoot, out string message)
    {
        try
        {
            string path = ResolveFilePath(projectRoot);
            Document document = ReadDocument(path);
            Block block = FindBlock(document.Text);
            if (block != null && block.Version > Version)
                throw new InvalidOperationException("Installed Agent Rules v" + block.Version
                    + " are newer than bundled v" + Version + ". Update the Unity package first.");

            string newline = DetectNewline(document.Text);
            string updated;
            if (block == null)
            {
                string separator = document.Text.Length == 0 ? ""
                    : EndsWithNewline(document.Text) ? newline : newline + newline;
                updated = document.Text + separator + BuildBlock(newline) + newline;
            }
            else updated = document.Text.Substring(0, block.Start) + BuildBlock(newline) + document.Text.Substring(block.End);

            // An identical install is a read-only operation, including its encoding and BOM.
            if (updated == document.Text)
            {
                message = "Agent Rules are current: " + path;
                return true;
            }
            WriteAtomically(path, document, updated);
            message = (block == null ? "Installed" : "Updated") + " Agent Rules v" + Version + ": " + path;
            return true;
        }
        catch (Exception exception)
        {
            message = "Agent Rules install failed: " + exception.Message;
            return false;
        }
    }

    private static string ResolveFilePath(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot)) throw new InvalidOperationException("Unity project root is required.");
        string root = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(Path.Combine(root, "Assets")) || !Directory.Exists(Path.Combine(root, "ProjectSettings")))
            throw new InvalidOperationException("Agent Rules require an existing Unity project root with Assets and ProjectSettings.");
        string path = Path.Combine(root, "AGENTS.md");
        if (Directory.Exists(path)) throw new InvalidOperationException("AGENTS.md is a directory: " + path);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("AGENTS.md is a link; select a regular host-project file before installing rules.");
        return path;
    }

    private static Document ReadDocument(string path)
    {
        if (!File.Exists(path)) return new Document { Encoding = new UTF8Encoding(false, true), Bytes = new byte[0] };
        byte[] bytes = File.ReadAllBytes(path);
        using (var stream = new MemoryStream(bytes))
        using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true))
        {
            // BOM detection may select a decoder that replaces invalid bytes. Decode
            // strictly so updating rules never rewrites damaged user text as U+FFFD.
            reader.Peek();
            Encoding encoding = (Encoding)reader.CurrentEncoding.Clone();
            encoding.DecoderFallback = DecoderFallback.ExceptionFallback;
            encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
            byte[] preamble = encoding.GetPreamble();
            bool hasBom = preamble.Length > 0 && bytes.Length >= preamble.Length;
            for (int i = 0; hasBom && i < preamble.Length; i++) hasBom = bytes[i] == preamble[i];
            int offset = hasBom ? preamble.Length : 0;
            string text = encoding.GetString(bytes, offset, bytes.Length - offset);
            return new Document { Exists = true, Text = text, Bytes = bytes, Encoding = encoding, HasBom = hasBom };
        }
    }

    private static Block FindBlock(string text)
    {
        int begin = text.IndexOf(BeginMarker, StringComparison.Ordinal);
        int end = text.IndexOf(EndMarker, StringComparison.Ordinal);
        if (begin < 0 && end < 0) return null;
        if (begin < 0 || end < begin
            || text.IndexOf(BeginMarker, begin + BeginMarker.Length, StringComparison.Ordinal) >= 0
            || text.IndexOf(EndMarker, end + EndMarker.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("Agent Rules markers are missing, duplicated, or out of order. Repair the marked block before installing.");
        int start = LineStart(text, begin);
        int finish = LineEnd(text, end + EndMarker.Length);
        if (text.Substring(start, LineEnd(text, begin) - start).Trim() != BeginMarker
            || text.Substring(LineStart(text, end), finish - LineStart(text, end)).Trim() != EndMarker)
            throw new InvalidOperationException("Agent Rules markers must each occupy their own line.");

        string managed = text.Substring(start, finish - start);
        int versionStart = managed.IndexOf(VersionPrefix, StringComparison.Ordinal);
        int version = 0;
        if (versionStart >= 0)
        {
            int valueStart = versionStart + VersionPrefix.Length;
            int valueEnd = managed.IndexOf(" -->", valueStart, StringComparison.Ordinal);
            if (valueEnd < 0 || managed.IndexOf(VersionPrefix, valueStart, StringComparison.Ordinal) >= 0
                || !int.TryParse(managed.Substring(valueStart, valueEnd - valueStart), NumberStyles.None,
                    CultureInfo.InvariantCulture, out version))
                throw new InvalidOperationException("Agent Rules version marker is invalid. Repair the marked block before installing.");
        }
        return new Block { Start = start, End = finish, Version = version };
    }

    private static string BuildBlock(string newline)
    {
        return BeginMarker + newline + VersionPrefix + Version.ToString(CultureInfo.InvariantCulture)
            + " -->" + newline + Normalize(Instructions).Replace("\n", newline) + newline + EndMarker;
    }

    private static void WriteAtomically(string path, Document document, string text)
    {
        string temporary = path + ".unity-autorun-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] body = document.Encoding.GetBytes(text);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (document.HasBom)
                {
                    byte[] preamble = document.Encoding.GetPreamble();
                    stream.Write(preamble, 0, preamble.Length);
                }
                stream.Write(body, 0, body.Length);
                stream.Flush(true);
            }
            if (document.Exists)
            {
                if (!File.Exists(path) || !EqualBytes(document.Bytes, File.ReadAllBytes(path)))
                    throw new IOException("AGENTS.md changed during installation. Refresh and try again.");
                File.Replace(temporary, path, null);
            }
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool EqualBytes(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
        return true;
    }
    private static string Normalize(string value) { return value.Replace("\r\n", "\n").Replace("\r", "\n"); }
    private static string DetectNewline(string text)
    {
        int index = text.IndexOfAny(new[] { '\r', '\n' });
        if (index < 0) return Environment.NewLine;
        return text[index] == '\n' ? "\n" : index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : "\r";
    }
    private static bool EndsWithNewline(string text) { return text.EndsWith("\n", StringComparison.Ordinal) || text.EndsWith("\r", StringComparison.Ordinal); }
    private static int LineStart(string text, int index)
    {
        while (index > 0 && text[index - 1] != '\r' && text[index - 1] != '\n') index--;
        return index;
    }
    private static int LineEnd(string text, int index)
    {
        while (index < text.Length && text[index] != '\r' && text[index] != '\n') index++;
        return index;
    }
    private sealed class Block { public int Start, End, Version; }
    private sealed class Document
    {
        public bool Exists, HasBom;
        public string Text = "";
        public byte[] Bytes;
        public Encoding Encoding;
    }
}
