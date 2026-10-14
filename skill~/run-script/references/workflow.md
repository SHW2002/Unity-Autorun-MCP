# Existing Script Runtime Contract

This reference is self-contained within the installed Skill. Use the connected MCP's `get_script_guidance` and schemas for version-specific details.

## Discover

New business sources use `Assets/Editor/AutorunScripts/`. Existing sources under `Assets/AutorunScripts/Editor/` remain compatible for discovery, source inspection and execution. Use the registered `sourcePath` and script ID without relocating files.

`list_scripts` supports kind `business`, `basic`, or `all`, plus `query`. `get_script` accepts `scriptId` and `includeSource`; use `includeSource=true` when establishing the complete matching flow. Check its exact starting conditions, `preparePlayMode` flag, parameter reads, declared dependencies, final assertions and `validationError`. Business scripts must declare Preconditions and override CheckPreconditions. Built-ins include `start-play-mode`, `click-button`, `wait`, `wait-for-ui-state`, `screenshot`, `stop-play-mode`, and `log`; do not substitute them for an existing business flow without the user's requested scope supporting that action.

## Run

`start_script` accepts `scriptId`, `parameters` (an object with string values, or its JSON string) or `parametersJson`, optional `runId`, `ensurePlayMode` (default false), and `timeoutSeconds` (default 120). Choose either parameters field, not both. Numbers and booleans are represented as strings in the parameter dictionary.

`preparePlayMode` is compiled metadata returned by `list_scripts`/`get_script`, not a start argument. When the root flag is true, the runner automatically enters Play Mode before preconditions or actions even if caller `ensurePlayMode` is omitted or false. Caller true also requests initial preparation; caller false cannot veto root true. Dependency flags are never consulted to infer a startup flow. Root `start-play-mode` declares preparation and requires no parameters; as a child it only verifies Play Mode has already been prepared, failing rather than switching Play Mode mid-run. A cold-start business flow declares root preparation and calls this base script. A flow whose root flag is false continues to require its declared running-game start unless the caller explicitly requests applicable startup.

Example arguments:

```json
{
  "scriptId": "business.observed-flow",
  "parameters": { "itemId": "42" },
  "ensurePlayMode": false,
  "timeoutSeconds": 120,
  "runId": "one-unique-id-for-this-run"
}
```

`get_script_status` accepts `runId` and an optional bounded `waitMs`/`pollMs`. Inspect `data.scriptRun`: `state` is pending, running, succeeded, failed, canceled, or timedout; `terminal=true` ends polling. Results include code/message, call stack, logs, invocation outcomes and artifact paths. Failure keeps this evidence even when the outer result has `ok=false`. Unknown IDs and conflicting retries are errors. Use a new ID for an intentional new run; reusing an old ID returns its retained result. For a root preparation flow, stopped Play Mode and absent post-start UI do not alone disprove the declared post-preparation start. If a preliminary query proves the actual starting scene/state is wrong, report that observation without inventing a failed execution; a runner-generated precondition failure exists only after an actual `start_script` request.

`cancel_script` accepts `runId`. Cancellation unwinds child scripts and owned resources; current scene/UI and Play Mode are preserved. `unity_stop` and `stop-play-mode` are explicit actions, never automatic cleanup. The runner handles authorized initial Play Mode preparation before the business iterator starts. Do not independently toggle Play Mode, edit code, or trigger recompilation during a run: arbitrary C# iterators cannot survive a Domain Reload. Report a reload failure rather than restarting and duplicating completed actions.

## Verify

Use the script's final assertions or narrow `get_ui_state`/`wait_for_ui_state` queries for user-requested evidence. UIState supports uGUI and optional TextMeshPro; it does not prove pixel appearance or arbitrary non-UI business state. Use actual project assertions or screenshot review when required by the request. Sensitive UI values remain masked unless the user explicitly authorizes reading them.

Do not modify code when compilation, dependencies, preconditions, or assertions fail. Preserve the result and describe the smallest missing requirement. If no complete matching script exists, ask whether the user wants a new self-test script rather than creating it silently. Generating a new flow belongs to `gen-script`; maintaining a user-specified existing business script while preserving its contract belongs to `edit-script`, each after the corresponding authorization.
