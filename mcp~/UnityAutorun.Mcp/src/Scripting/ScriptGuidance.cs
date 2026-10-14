using System;
using System.Text.Json.Nodes;

namespace UnityAutorun.Mcp
{
    public static class ScriptGuidance
    {
        public static JsonObject Get()
        {
            string projectRoot = null;
            string pathError = null;
            try { projectRoot = ProjectPaths.ResolveProjectRootDirectory().Replace('\\', '/'); }
            catch (InvalidOperationException exception) { pathError = exception.Message; }
            return JsonUtil.Obj(("ok", true), ("code", "ok"), ("message", "Current Unity-compiled C# script generation and execution contract."),
                ("data", JsonUtil.Obj(("contractVersion", "1.0"), ("mcpVersion", McpVersion.Current),
                    ("projectRoot", projectRoot), ("projectRootError", pathError),
                    ("scriptDirectory", ProjectPaths.GeneratedScriptsRelativePath),
                    ("legacyScriptDirectory", ProjectPaths.LegacyGeneratedScriptsRelativePath),
                    ("scriptPlacement", "Create new business source files only under Assets/Editor/AutorunScripts. Existing scripts under Assets/AutorunScripts/Editor remain discoverable and readable for reuse; do not silently move or rewrite them."),
                    ("bridgeProfile", "Optional UNITY_AUTORUN_BRIDGE_PROFILE selects an isolated endpoint at Library/UnityAutorunTool/profiles/<profile>/bridge-endpoint.json. Unset or empty uses Library/UnityAutorunTool/bridge-endpoint.json. A profile is a simple 1-64 character name using ASCII letters, digits, hyphens and underscores; paths and whitespace are rejected. Set the same profile for Unity before the bridge's first initialization and for the MCP process. Unity captures the profile at initialization so later environment changes do not redirect publishing or cleanup. Never select a profile different from the intended Unity bridge."),
                    ("skills", new JsonArray { "gen-script", "run-script" }),
                    ("workflow", new JsonArray
                    {
                        "Read this guidance and unity_status; inspect list_scripts and get_script(includeSource=true) before writing new code.",
                        "gen-script generates, compiles, runs a self-test and repairs the newly generated file. run-script executes an existing script without modifying any script.",
                        "Read actual host-project sources and current UI evidence. Use real project APIs when necessary; do not invent APIs, view names or button selectors.",
                        "Before creating a source file, inspect ancestor .asmdef files for Assets/Editor/AutorunScripts. Scripts must compile in an Editor assembly that can reference UnityAutorunTool.Editor and the actual host API assemblies. An ancestor host .asmdef can absorb Editor subfolders; create a suitable Editor-only .asmdef with verified named assembly references when needed. If host code is in predefined Assembly-CSharp, keep scripts in predefined Assembly-CSharp-Editor rather than creating an .asmdef that references Assembly-CSharp (Unity forbids that reference). Do not invent assembly names; inspect actual project assembly layout.",
                        "Declare starting state, exact intended flow and observable postconditions. Every business script must describe its Preconditions and override CheckPreconditions. Wrong starting state fails before actions; never silently choose another route.",
                        "When the requested flow includes starting the game, declare PreparePlayMode=true on the root business script and declare/call the start-play-mode dependency. The runner prepares Play Mode before creating the business iterator and checking its post-preparation starting scene/state. Keep PreparePlayMode=false for a flow that requires a game already running. Merely depending on a script with PreparePlayMode=true never enables preparation.",
                        "Reuse an existing script only when its entire flow and starting state exactly match the needed segment. Never edit reused scripts. Same target from a different start/flow requires a different script ID.",
                        "For startup navigation, inspect the host's delayed automatic popup/loading queues and actual completion signals. With the user's dismissal authorization, close only known popups whose close controls and queue advancement are verified by source/runtime evidence, then wait for the next queued item. Reuse an independently runnable business dismissal segment when the full flow matches; unknown popups fail with evidence. Require queue/loading completion and a stable, interactable, unobstructed target before success; momentary home-screen clickability or a fixed sleep is insufficient. Keep host popup names and APIs in generated business scripts, not the generic core.",
                        "Use create_script with a path relative to the script directory. The creation tool never replaces a file. Repairs may edit only the new script from this generation task via the agent's file editor.",
                        "Call refresh_scripts, poll unity_status until isCompiling=false and inspect compilerMessages. Then list/get the registered script and resolve validationError before starting.",
                        "Run the generated script with start_script using an explicit starting state and parameters. Reuse runId only for transport retries of that same execution; use a fresh runId for every new self-test, especially after code repairs. Poll get_script_status until terminal=true.",
                        "Prove success with a final observable UI/project-state assertion and inspect logs/artifacts. Compilation or a successful button click alone does not prove the goal.",
                        "If the self-test cannot run because Unity/bridge/project is unavailable, report that limitation; do not claim the script is verified."
                    }),
                    ("contract", JsonUtil.Obj(
                        ("namespace", "UnityAutorun.Scripting"),
                        ("metadata", "[AutoRunScript(id, name, AutoRunScriptKind.Business|Basic, Description=..., Preconditions=..., PreparePlayMode=true|false, Dependencies=new[]{...})]. list_scripts/get_script expose the bool as preparePlayMode."),
                        ("baseClass", "AutoRunScript"),
                        ("execute", "public override IEnumerator Execute(AutoRunScriptContext context)"),
                        ("preconditions", "public override string CheckPreconditions(AutoRunScriptContext context): null means valid; a nonempty message means fail without executing."),
                        ("exit", "public override void OnExit(AutoRunScriptContext context, AutoRunScriptResult result): clean up only resources owned by this invocation. Runs once on success, failure, cancellation, timeout and failed preconditions."),
                        ("parameters", "String-valued JSON object. Context.GetString/GetInt/GetNumber/GetBool parse parameters; child parameters must be passed explicitly."),
                        ("composition", "yield return context.Call(scriptId, new Dictionary<string,string>{...}); all called IDs must appear in Dependencies. Child failure propagates. Missing/cyclic dependencies invalidate the script."),
                        ("waiting", "yield return context.Wait(seconds); yield return context.WaitUntil(Func<bool>, timeoutSeconds, message); yield return null for one editor tick. Use bounded observable waits. Keep each MoveNext short and do not block the Unity editor thread."),
                        ("host", "context.Host.IsPlaying; context.Host.ClickButton(AutoRunScriptButtonSelector); context.Host.EvaluateUiState(AutoRunScriptUiStateQuery); context.Host.CaptureScreenshot(relativePath); context.Host.StopPlayMode(). Host operations return AutoRunScriptOperationResult: use context.Require(result) for failures; UI assertions require result.Ok && result.Matched."),
                        ("buttonSelector", "Name or Text is required for uGUI; ObjectPath/Scope narrow the selector and do not replace it. Framework is ugui or fairygui. Prefer an evidence-backed path/scope and unique MatchPolicy. FairyGUI supports only a unique Name; Text/path/scope are unsupported."),
                        ("uiStateQuery", "Query/Scope/Exact/Types/Property/Comparison/Expected/IncludeInactive/IncludeSensitive. Verify actual hierarchy paths/properties with get_ui_state first."),
                        ("logging", "context.Log(message), context.AddArtifact(path), context.Fail(code,message), context.Require(operationResult)."),
                        ("result", "data.scriptRun contains runId/scriptId/state/code/message/terminal/callStack/logs/artifacts. state is pending, running, succeeded, failed, canceled or timedout. Terminal failure returns ok=false with the same full scriptRun."),
                        ("playPreparation", "Root PreparePlayMode defaults to false. Before any script actions/preconditions, the service enters Play Mode only when caller ensurePlayMode=true OR the selected root script's preparePlayMode=true. Do not infer preparation from dependency metadata. Root start-play-mode has preparePlayMode=true; when called as a child it only confirms Play Mode is already prepared and never toggles Play Mode from a running iterator. An initial entry may survive domain reload because no script actions ran; running iterators never resume or replay after reload."),
                        ("gameState", "Success, failure, cancellation and timeout unwind scripts/resources while preserving the current game state. Stop Play Mode only through an explicit requested action. Caller ensurePlayMode defaults to false; false does not override a root script's declared preparePlayMode=true."))),
                    ("builtins", new JsonArray
                    {
                        Builtin("start-play-mode", "No parameters. Root preparePlayMode=true prepares initial entry before execution; a child call verifies Play Mode without switching it."),
                        Builtin("click-button", "name/text/objectPath/scope/framework/matchPolicy"),
                        Builtin("wait", "seconds"),
                        Builtin("wait-for-ui-state", "query/scope/exact/types(comma separated)/property/comparison/expected/includeInactive/includeSensitive/timeoutSeconds"),
                        Builtin("screenshot", "path(optional project-relative .png)/timeoutSeconds"),
                        Builtin("stop-play-mode", "Explicit stop action; no parameters."),
                        Builtin("log", "message")
                    }),
                    ("template", Template), ("coldStartTemplate", ColdStartTemplate))));
        }

        private static JsonObject Builtin(string id, string parameters)
        {
            return JsonUtil.Obj(("id", id), ("parameters", parameters));
        }

        // This template uses only verified runtime contracts. Replace IDs/descriptions and
        // evidence-backed parameters or project API logic for the requested business flow.
        public const string Template = @"using System.Collections;
using System.Collections.Generic;
using UnityAutorun.Scripting;

[AutoRunScript(""business.example-flow"", ""Example business flow"", AutoRunScriptKind.Business,
    Description = ""One exact flow: starting UI -> click specified button -> assert target UI."",
    Preconditions = ""Play Mode, startQuery matches the required starting UI, and buttonName/objectPath identify the observed button."",
    PreparePlayMode = false,
    Dependencies = new[] { ""click-button"", ""wait-for-ui-state"" })]
public sealed class ExampleBusinessFlow : AutoRunScript
{
    public override string CheckPreconditions(AutoRunScriptContext context)
    {
        if (!context.Host.IsPlaying) return ""This flow requires Play Mode."";
        if (string.IsNullOrWhiteSpace(context.GetString(""startQuery""))
            || string.IsNullOrWhiteSpace(context.GetString(""buttonName""))
            || string.IsNullOrWhiteSpace(context.GetString(""objectPath""))
            || string.IsNullOrWhiteSpace(context.GetString(""targetQuery"")))
            return ""Provide observed startQuery, buttonName, objectPath and targetQuery."";
        var state = context.Host.EvaluateUiState(new AutoRunScriptUiStateQuery
        {
            Query = context.GetString(""startQuery""), Exact = true, Comparison = ""exists""
        });
        return state != null && state.Ok && state.Matched ? null : ""The required starting UI is not present."";
    }

    public override IEnumerator Execute(AutoRunScriptContext context)
    {
        yield return context.Call(""click-button"", new Dictionary<string, string>
        {
            { ""name"", context.GetString(""buttonName"") },
            { ""objectPath"", context.GetString(""objectPath"") }, { ""framework"", ""ugui"" }
        });
        yield return context.Call(""wait-for-ui-state"", new Dictionary<string, string>
        {
            { ""query"", context.GetString(""targetQuery"") }, { ""exact"", ""true"" },
            { ""comparison"", ""exists"" }, { ""timeoutSeconds"", ""15"" }
        });
        context.Log(""The requested target UI was observed."");
    }

    public override void OnExit(AutoRunScriptContext context, AutoRunScriptResult result)
    {
        // Release only resources/event handlers created by this script. Preserve game state.
    }
}
";

        public const string ColdStartTemplate = @"using System.Collections;
using System.Collections.Generic;
using UnityAutorun.Scripting;
using UnityEngine.SceneManagement;

[AutoRunScript(""business.cold-start-to-observed-ui"", ""Start game and assert observed UI"", AutoRunScriptKind.Business,
    Description = ""Prepare Play Mode from the observed startup scene, then wait for the declared target landmark."",
    Preconditions = ""After Play Mode preparation, the active scene path equals startScenePath; targetQuery identifies the observed target UI."",
    PreparePlayMode = true,
    Dependencies = new[] { ""start-play-mode"", ""wait-for-ui-state"" })]
public sealed class ColdStartToObservedUi : AutoRunScript
{
    public override string CheckPreconditions(AutoRunScriptContext context)
    {
        if (!context.Host.IsPlaying) return ""Initial Play Mode preparation did not finish."";
        string startScenePath = context.GetString(""startScenePath"");
        if (string.IsNullOrWhiteSpace(startScenePath) || string.IsNullOrWhiteSpace(context.GetString(""targetQuery"")))
            return ""Provide observed startScenePath and targetQuery."";
        return SceneManager.GetActiveScene().path == startScenePath ? null : ""The required post-preparation startup scene is not active."";
    }

    public override IEnumerator Execute(AutoRunScriptContext context)
    {
        yield return context.Call(""start-play-mode"");
        // Add only the verified project's exact startup/business actions here.
        yield return context.Call(""wait-for-ui-state"", new Dictionary<string, string>
        {
            { ""query"", context.GetString(""targetQuery"") }, { ""exact"", ""true"" },
            { ""comparison"", ""exists"" }, { ""timeoutSeconds"", ""30"" }
        });
        context.Log(""The declared target UI was observed."");
    }
}
";
    }
}
