using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace UnityAutorun.Scripting
{
    [AutoRunScript("click-button", "点击 UI 按钮", AutoRunScriptKind.Basic,
        Description = "Parameters: name or text (required), objectPath/scope (optional refinements), framework(ugui|fairygui)/matchPolicy. UGUI uses a real EventSystem pointer click.",
        Preconditions = "Play Mode, an active EventSystem and a uniquely identifiable reachable button.")]
    public sealed class AutoRunClickButtonScript : AutoRunScript
    {
        public override string CheckPreconditions(AutoRunScriptContext context)
        {
            return context.Host.IsPlaying ? null : "Clicking a button requires Play Mode.";
        }
        public override IEnumerator Execute(AutoRunScriptContext context)
        {
            context.Require(context.Host.ClickButton(new AutoRunScriptButtonSelector
            {
                Name = context.GetString("name"), Text = context.GetString("text"), ObjectPath = context.GetString("objectPath"),
                Scope = context.GetString("scope"), Framework = context.GetString("framework", "ugui"),
                MatchPolicy = context.GetString("matchPolicy", "unique")
            }));
            yield break;
        }
    }

    [AutoRunScript("wait", "等待", AutoRunScriptKind.Basic, Description = "Parameters: seconds (default 1). Uses real editor time, independent of Time.timeScale.")]
    public sealed class AutoRunWaitScript : AutoRunScript
    {
        public override IEnumerator Execute(AutoRunScriptContext context) { yield return context.Wait(context.GetNumber("seconds", 1)); }
    }

    [AutoRunScript("wait-for-ui-state", "等待 UI 状态", AutoRunScriptKind.Basic,
        Description = "Parameters: query/scope/exact/types(comma-separated)/property/comparison/expected/includeInactive/includeSensitive/timeoutSeconds(default 15). Supports UGUI and TMP structured state.")]
    public sealed class AutoRunWaitForUiStateScript : AutoRunScript
    {
        public override IEnumerator Execute(AutoRunScriptContext context)
        {
            var query = new AutoRunScriptUiStateQuery
            {
                Query = context.GetString("query"), Scope = context.GetString("scope"), Exact = context.GetBool("exact"),
                Property = context.GetString("property", "value"), Comparison = context.GetString("comparison", "exists"),
                Expected = context.GetString("expected"), IncludeInactive = context.GetBool("includeInactive"),
                IncludeSensitive = context.GetBool("includeSensitive")
            };
            string types = context.GetString("types");
            if (!string.IsNullOrWhiteSpace(types))
                query.Types = new List<string>(types.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            yield return context.WaitUntil(() =>
            {
                AutoRunScriptOperationResult result = context.Host.EvaluateUiState(query);
                if (result == null || !result.Ok) context.Fail(result == null ? "operation_no_result" : result.Code, result == null ? "UI query returned no result." : result.Message);
                return result.Matched;
            }, context.GetNumber("timeoutSeconds", 15), "Expected UI state did not appear: " + query.Query);
            context.Log("Expected UI state matched: " + query.Query);
        }
    }

    [AutoRunScript("screenshot", "游戏视图截图", AutoRunScriptKind.Basic,
        Description = "Parameters: path(optional project-relative .png)/timeoutSeconds(default 10). Confirms a nonempty rendered PNG before recording its artifact.",
        Preconditions = "Play Mode with a rendering Game View.")]
    public sealed class AutoRunScreenshotScript : AutoRunScript
    {
        public override string CheckPreconditions(AutoRunScriptContext context) { return context.Host.IsPlaying ? null : "Game View screenshot requires Play Mode."; }
        public override IEnumerator Execute(AutoRunScriptContext context)
        {
            AutoRunScriptOperationResult result = context.Host.CaptureScreenshot(context.GetString("path"));
            if (result == null || !result.Ok) context.Fail(result == null ? "screenshot_failed" : result.Code, result == null ? "Capture returned no result." : result.Message);
            string path = result.ArtifactPath;
            if (string.IsNullOrEmpty(path)) context.Fail("screenshot_failed", "Capture returned no output path.");
            yield return context.WaitUntil(() => IsCompletePng(path), context.GetNumber("timeoutSeconds", 10),
                "Game View did not render a complete PNG before the timeout: " + path);
            context.AddArtifact(path);
            context.Log("Screenshot saved: " + path);
        }

        private static bool IsCompletePng(string path)
        {
            if (!File.Exists(path)) return false;
            try
            {
                using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length < 20) return false;
                    byte[] signature = new byte[8];
                    byte[] trailer = new byte[12];
                    if (stream.Read(signature, 0, signature.Length) != signature.Length) return false;
                    stream.Seek(-12, SeekOrigin.End);
                    if (stream.Read(trailer, 0, trailer.Length) != trailer.Length) return false;
                    byte[] expectedSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };
                    byte[] expectedTrailer = { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 };
                    for (int index = 0; index < signature.Length; index++) if (signature[index] != expectedSignature[index]) return false;
                    for (int index = 0; index < trailer.Length; index++) if (trailer[index] != expectedTrailer[index]) return false;
                    return true;
                }
            }
            catch (IOException) { return false; }
        }
    }

    [AutoRunScript("stop-play-mode", "停止 Play Mode", AutoRunScriptKind.Basic,
        Description = "Explicitly stops Play Mode. Other scripts never stop Play Mode automatically on success, failure or cancellation.")]
    public sealed class AutoRunStopPlayModeScript : AutoRunScript
    {
        public override IEnumerator Execute(AutoRunScriptContext context) { context.Require(context.Host.StopPlayMode()); yield break; }
    }

    [AutoRunScript("log", "记录日志", AutoRunScriptKind.Basic, Description = "Parameters: message.")]
    public sealed class AutoRunLogScript : AutoRunScript
    {
        public override IEnumerator Execute(AutoRunScriptContext context) { context.Log(context.GetString("message", string.Empty)); yield break; }
    }
}
