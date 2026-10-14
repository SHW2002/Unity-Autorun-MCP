using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityAutorun.Scripting
{
    public sealed class AutoRunUnityScriptHost : IAutoRunScriptHost
    {
        public bool IsPlaying { get { return EditorApplication.isPlaying; } }

        public AutoRunScriptOperationResult ClickButton(AutoRunScriptButtonSelector selector)
        {
            if (!IsPlaying) return AutoRunScriptOperationResult.Failure("play_mode_required", "Button clicks require Play Mode.");
            if (selector == null) return AutoRunScriptOperationResult.Failure("selector_required", "Button selector is required.");
            if (string.IsNullOrWhiteSpace(selector.Name) && string.IsNullOrWhiteSpace(selector.Text))
                return AutoRunScriptOperationResult.Failure("selector_required", "Provide a button name or text; objectPath and scope refine that selector.");
            if (!string.IsNullOrEmpty(selector.MatchPolicy) && selector.MatchPolicy != AutoRunParam.MATCH_UNIQUE
                && selector.MatchPolicy != AutoRunParam.MATCH_FIRST_INTERACTABLE)
                return AutoRunScriptOperationResult.Failure("invalid_match_policy", "Match policy must be unique or first-interactable.");
            string framework = (selector.Framework ?? "ugui").ToLowerInvariant();
            if (framework != "ugui" && framework != "fairygui")
                return AutoRunScriptOperationResult.Failure("unsupported_framework", "Supported click frameworks are ugui and fairygui.");
            if (framework == "fairygui" && (!string.IsNullOrEmpty(selector.ObjectPath) || !string.IsNullOrEmpty(selector.Scope)))
                return AutoRunScriptOperationResult.Failure("unsupported_selector", "FairyGUI supports unique button names; path and scope selectors are unavailable.");
            var param = new AutoRunParam
            {
                buttonName = string.IsNullOrWhiteSpace(selector.Name) ? AutoRunParam.DEFAULT_NAME : selector.Name,
                buttonText = string.IsNullOrWhiteSpace(selector.Text) ? AutoRunParam.DEFAULT_TEXT : selector.Text,
                isFairyGUI = framework == "fairygui",
                objectPath = selector.ObjectPath,
                scopeRootName = selector.Scope,
                matchPolicy = string.IsNullOrEmpty(selector.MatchPolicy) ? AutoRunParam.MATCH_UNIQUE : selector.MatchPolicy
            };
            AutoRunButtonResult result = AutoRunButtonService.ClickWithPointer(param, 0);
            return new AutoRunScriptOperationResult { Ok = result.ok, Code = result.code, Message = result.message };
        }

        public AutoRunScriptOperationResult EvaluateUiState(AutoRunScriptUiStateQuery query)
        {
            if (query == null) return AutoRunScriptOperationResult.Failure("query_required", "UI state query is required.");
            string comparison;
            string error;
            if (!AutoRunUiStateService.TryValidateComparison(query.Comparison, out comparison, out error))
                return AutoRunScriptOperationResult.Failure("invalid_comparison", error);
            AutoRunUiStateQueryResult state = AutoRunUiStateService.Query(new AutoRunUiStateQuery
            {
                Query = query.Query, Scope = query.Scope, Exact = query.Exact,
                IncludeInactive = query.IncludeInactive, IncludeSensitive = query.IncludeSensitive,
                Types = query.Types, Limit = 100
            });
            AutoRunUiStateAssertionResult result = AutoRunUiStateService.Evaluate(state, query.Property, comparison, query.Expected);
            return new AutoRunScriptOperationResult { Ok = true, Matched = result.Matched, Code = "ok",
                Message = "UI assertion " + (result.Matched ? "matched" : "did not match") + "; actual: " + result.ActualValue };
        }

        /// <summary>Queues capture on the next rendered Game View frame. The caller must wait for the artifact to exist.</summary>
        public AutoRunScriptOperationResult CaptureScreenshot(string relativePath)
        {
            if (!IsPlaying) return AutoRunScriptOperationResult.Failure("play_mode_required", "Game View screenshots require Play Mode.");
            try
            {
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string requested = string.IsNullOrWhiteSpace(relativePath)
                    ? "AutorunToolData/Screenshots/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".png"
                    : relativePath;
                if (Path.IsPathRooted(requested)) return AutoRunScriptOperationResult.Failure("invalid_screenshot_path", "Screenshot path must be relative to this Unity project.");
                string path = Path.GetFullPath(Path.Combine(root, requested));
                StringComparison pathComparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!path.StartsWith(root + Path.DirectorySeparatorChar, pathComparison)
                    || !string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                    return AutoRunScriptOperationResult.Failure("invalid_screenshot_path", "Screenshot must be a .png file inside this Unity project.");
                if (File.Exists(path)) return AutoRunScriptOperationResult.Failure("screenshot_already_exists", "Screenshot path already exists; choose a new path to avoid stale capture results.");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                ScreenCapture.CaptureScreenshot(path);
                return AutoRunScriptOperationResult.Success("Game View capture requested; waiting for the rendered PNG.", path);
            }
            catch (Exception exception) { return AutoRunScriptOperationResult.Failure("screenshot_failed", exception.Message); }
        }

        public AutoRunScriptOperationResult StopPlayMode()
        {
            // Finish the calling basic script and persist its terminal status before Unity exits Play Mode.
            EditorApplication.update -= StopOnNextUpdate;
            EditorApplication.update += StopOnNextUpdate;
            return AutoRunScriptOperationResult.Success("Stop Play Mode explicitly requested by this script.");
        }

        private static void StopOnNextUpdate()
        {
            EditorApplication.update -= StopOnNextUpdate;
            EditorApplication.isPlaying = false;
        }
    }
}
