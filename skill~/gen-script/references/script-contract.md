# Unity C# Script Contract

The connected MCP's `get_script_guidance` is authoritative for the current version. This reference is self-contained within the installed Skill.

## Unity assembly placement

Create new business scripts under `Assets/Editor/AutorunScripts/`. Source reads and script discovery remain compatible with existing scripts under `Assets/AutorunScripts/Editor/`; the generation workflow does not silently migrate them. `create_script.path` remains relative to the new directory, such as `Business/OpenObservedUi.cs`.

Inspect ancestor `.asmdef` files before creating source under `Assets/Editor/AutorunScripts/`. An ancestor host assembly definition can absorb an Editor subfolder; the generated script must compile in an Editor assembly that can reference `UnityAutorunTool.Editor` and the actual host API assemblies. When needed, create an Editor-only assembly definition with verified named assembly references. If host APIs are in predefined `Assembly-CSharp`, keep scripts in predefined `Assembly-CSharp-Editor` rather than creating an `.asmdef` that references `Assembly-CSharp`; Unity forbids that reference. Do not invent assembly names or modify unrelated host assembly definitions to force compilation. Resolve an incompatible layout explicitly before claiming generation success.

## Authoring contract

Use `UnityAutorun.Scripting`. A public parameterless script class derives from `AutoRunScript` and has `[AutoRunScript(id, name, AutoRunScriptKind.Business, ...)]`. Its metadata includes `Description`, required `Preconditions`, `PreparePlayMode` (default false), and `Dependencies = new[] { ... }`. MCP metadata exposes the preparation flag as `preparePlayMode`. Every business script overrides `CheckPreconditions(AutoRunScriptContext)`; return null when its exact post-preparation start is valid, otherwise return a concrete failure explanation.

`Execute(AutoRunScriptContext)` returns `IEnumerator`. Supported yield values are null, nested `IEnumerator`, `context.Wait(seconds)`, `context.WaitUntil(condition, timeoutSeconds, message)`, and `context.Call(scriptId, Dictionary<string,string>)`. Do not yield `WaitForSeconds`, `WaitForEndOfFrame`, `Task`, or Unity coroutine handles: execution uses the Editor runner, not a MonoBehaviour coroutine.

Declare every called script in `Dependencies`, including calls from branches. Missing dependencies, duplicate IDs, cycles, or undeclared calls are errors. Child parameters are explicitly supplied and are not inherited. A failed dependency fails its caller. `OnExit(context, AutoRunScriptResult)` runs when a script exits, including precondition failure; cleanup must tolerate partial initialization and preserve game state.

For a requested startup flow, put `PreparePlayMode=true` on the root business script, declare `start-play-mode` in its dependencies, and yield `context.Call("start-play-mode")`. The runner enters Play Mode before checking preconditions or creating the business iterator when the selected root metadata is true or caller `ensurePlayMode=true`. A false caller option does not override true root metadata; adding a dependency with preparation metadata does not enable preparation. A child `start-play-mode` call only verifies Play Mode is already prepared, and fails if it is not. Never switch Play Mode or trigger asset recompilation from a running `IEnumerator`: arbitrary C# iterators cannot be reconstructed after a Domain Reload. Initial preparation can recover across reload because no script actions have executed. An explicit requested terminal `stop-play-mode` action is supported by the runner; do not resume business actions after stopping. Mid-run reload or unexpected Play Mode changes terminate execution and preserve the failure rather than replaying actions.

Context members include `RunId`, `ScriptId`, `Host`, `GetString`, `GetInt`, `GetNumber`, `GetBool`, `Log`, `AddArtifact`, `Fail(code,message)`, and `Require(operationResult)`. `Require` checks operation success; an assertion of UI state additionally requires `result.Matched`.

Host operations include `IsPlaying`, `ClickButton(AutoRunScriptButtonSelector)`, `EvaluateUiState(AutoRunScriptUiStateQuery)`, `CaptureScreenshot(projectRelativePath)`, and `StopPlayMode()`. Use actual project APIs for project-specific behavior. The core runtime remains generic.

## Startup completion and delayed popups

Inspect the host's delayed automatic popup queue and loading completion signals before defining the final startup assertion. With the user's dismissal authorization, close only popup types whose actual close controls and queue advancement have been verified from source and runtime evidence. Wait for the next queued item rather than assuming one close empties the queue. Unknown popups fail instead of being dismissed by a guessed selector.

Keep an exact, independently runnable popup-dismissal business segment reusable through declared `context.Call` dependencies. After it completes, require the verified popup/loading queues to finish and the target to stay visible, interactable and unobstructed for an evidence-backed bounded stability interval. A momentarily clickable home screen before a delayed advertisement appears is not success; do not substitute a fixed sleep for queue/state completion evidence or place host popup names/APIs in the generic core.

## Built-in dependencies

All parameter values are strings, including numbers and booleans.

| Script ID | Parameters |
| --- | --- |
| `start-play-mode` | None; root execution prepares Play Mode, child execution only verifies the prepared state |
| `click-button` | At least `name` or `text`; optional `objectPath`/`scope`, `framework` (`ugui` or `fairygui`), `matchPolicy` (default `unique`) |
| `wait` | `seconds` (default `1`) |
| `wait-for-ui-state` | `query`, `scope`, `exact`, comma-separated `types`, `property`, `comparison`, `expected`, `includeInactive`, `includeSensitive`, `timeoutSeconds` |
| `screenshot` | Optional project-relative `.png` `path`, `timeoutSeconds` |
| `stop-play-mode` | None; only an explicit requested stop |
| `log` | `message` |

For uGUI, `name` or `text` is required and observed `objectPath`/`scope` narrow the candidate set; paths are not standalone selectors. FairyGUI supports only a unique button name, and rejects text/path/scope selectors. Prefer unique matching. Use state assertions for asynchronous results instead of assuming a fixed sleep guarantees success. Password/PIN fields remain masked unless the user authorizes sensitive reading.

## Minimal flow template

Replace the illustrative identifiers and parameters with project evidence, or adapt the live template from `get_script_guidance`.

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityAutorun.Scripting;

[AutoRunScript("business.open-target-from-home", "Open target from home", AutoRunScriptKind.Business,
    Description = "From the declared home UI, click one observed control and assert target state.",
    Preconditions = "Play Mode; startQuery identifies the observed home landmark.",
    PreparePlayMode = false,
    Dependencies = new[] { "click-button", "wait-for-ui-state" })]
public sealed class OpenTargetFromHome : AutoRunScript
{
    public override string CheckPreconditions(AutoRunScriptContext context)
    {
        if (!context.Host.IsPlaying) return "This flow requires Play Mode.";
        if (string.IsNullOrWhiteSpace(context.GetString("startQuery"))
            || string.IsNullOrWhiteSpace(context.GetString("buttonName"))
            || string.IsNullOrWhiteSpace(context.GetString("targetQuery")))
            return "Provide the observed startQuery, buttonName and targetQuery.";
        var state = context.Host.EvaluateUiState(new AutoRunScriptUiStateQuery
        {
            Query = context.GetString("startQuery"), Exact = true, Comparison = "exists"
        });
        return state != null && state.Ok && state.Matched ? null : "Required starting UI is absent.";
    }
    public override IEnumerator Execute(AutoRunScriptContext context)
    {
        yield return context.Call("click-button", new Dictionary<string, string>
        {
            { "name", context.GetString("buttonName") }, { "framework", "ugui" }
        });
        yield return context.Call("wait-for-ui-state", new Dictionary<string, string>
        {
            { "query", context.GetString("targetQuery") }, { "exact", "true" },
            { "comparison", "exists" }, { "timeoutSeconds", "15" }
        });
        context.Log("Target UI assertion passed.");
    }
    public override void OnExit(AutoRunScriptContext context, AutoRunScriptResult result)
    {
        // Release only resources owned by this script; preserve the current game state.
    }
}
```

## Startup flow template

Use a separate flow ID when the request includes starting the game. Confirm the startup scene path and target landmark from project evidence, then add the exact project startup actions needed between preparation and the final assertion. This template does not change scenes or infer a different route if the starting scene is wrong.

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityAutorun.Scripting;
using UnityEngine.SceneManagement;

[AutoRunScript("business.cold-start-to-observed-ui", "Start game and assert observed UI", AutoRunScriptKind.Business,
    Description = "Prepare Play Mode from the observed startup scene, then wait for the declared target landmark.",
    Preconditions = "After preparation, the active scene equals startScenePath; targetQuery identifies the observed target UI.",
    PreparePlayMode = true,
    Dependencies = new[] { "start-play-mode", "wait-for-ui-state" })]
public sealed class ColdStartToObservedUi : AutoRunScript
{
    public override string CheckPreconditions(AutoRunScriptContext context)
    {
        if (!context.Host.IsPlaying) return "Initial Play Mode preparation did not finish.";
        string startScenePath = context.GetString("startScenePath");
        if (string.IsNullOrWhiteSpace(startScenePath) || string.IsNullOrWhiteSpace(context.GetString("targetQuery")))
            return "Provide observed startScenePath and targetQuery.";
        return SceneManager.GetActiveScene().path == startScenePath ? null : "Required post-preparation startup scene is not active.";
    }
    public override IEnumerator Execute(AutoRunScriptContext context)
    {
        yield return context.Call("start-play-mode");
        // Add only the verified project's exact startup/business actions here.
        yield return context.Call("wait-for-ui-state", new Dictionary<string, string>
        {
            { "query", context.GetString("targetQuery") }, { "exact", "true" },
            { "comparison", "exists" }, { "timeoutSeconds", "30" }
        });
        context.Log("The declared target UI was observed.");
    }
}
```

## MCP workflow

Use `create_script` with `path` relative to `Assets/Editor/AutorunScripts/` and `source` containing C#. Use the normal file editor only to repair files created by the current generation task. `refresh_scripts` requests Unity asset refresh; poll `unity_status` for compilation completion and inspect `compilerMessages`. Verify registered metadata and `validationError` through `list_scripts`/`get_script`.

`start_script` takes `scriptId`, string-valued `parameters` or `parametersJson` (choose one), optional `runId`, `ensurePlayMode` (default false), and `timeoutSeconds`. `preparePlayMode` is compiled script metadata, not a `start_script` argument. Effective preparation is caller `ensurePlayMode=true` OR root `preparePlayMode=true`, never dependency inference. Reuse a run ID only for transport retries of the same execution. Every intentional new self-test, including a rerun after compilation or repair, uses a new ID; an old ID returns its retained result instead of executing new code. `get_script_status` with that ID returns `data.scriptRun`, including state, terminal flag, code, message, call stack, logs and artifacts. A failed terminal result still contains the full run evidence. Poll only while nonterminal; call `cancel_script` when the user cancels.
