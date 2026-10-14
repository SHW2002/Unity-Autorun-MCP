# Existing Script Runtime Contract

This reference is self-contained within the installed Skill. Use the connected MCP's `get_script_guidance` and schemas for version-specific details.

## Discover

`list_scripts` supports kind `business`, `basic`, or `all`, plus `query`. `get_script` accepts `scriptId` and `includeSource`. Check its exact starting conditions, parameter reads, declared dependencies, final assertions and `validationError`. Business scripts must declare Preconditions and override CheckPreconditions. Built-ins include `click-button`, `wait`, `wait-for-ui-state`, `screenshot`, `stop-play-mode`, and `log`; do not substitute them for an existing business flow without the user's requested scope supporting that action.

## Run

`start_script` accepts `scriptId`, `parameters` (an object with string values, or its JSON string) or `parametersJson`, optional `runId`, `ensurePlayMode` (default false), and `timeoutSeconds` (default 120). Choose either parameters field, not both. Numbers and booleans are represented as strings in the parameter dictionary.

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

`get_script_status` accepts `runId` and an optional bounded `waitMs`/`pollMs`. Inspect `data.scriptRun`: `state` is pending, running, succeeded, failed, canceled, or timedout; `terminal=true` ends polling. Results include code/message, call stack, logs, invocation outcomes and artifact paths. Failure keeps this evidence even when the outer result has `ok=false`. Unknown IDs and conflicting retries are errors. Use a new ID for an intentional new run; reusing an old ID returns its retained result. If a preliminary query already shows a wrong starting state, report that observation without inventing a failed execution; a runner-generated precondition failure exists only after an actual `start_script` request.

`cancel_script` accepts `runId`. Cancellation unwinds child scripts and owned resources; current scene/UI and Play Mode are preserved. `unity_stop` and `stop-play-mode` are explicit actions, never automatic cleanup. The runner handles authorized initial Play Mode preparation before the business iterator starts. Do not independently toggle Play Mode, edit code, or trigger recompilation during a run: arbitrary C# iterators cannot survive a Domain Reload. Report a reload failure rather than restarting and duplicating completed actions.

## Verify

Use the script's final assertions or narrow `get_ui_state`/`wait_for_ui_state` queries for user-requested evidence. UIState supports uGUI and optional TextMeshPro; it does not prove pixel appearance or arbitrary non-UI business state. Use actual project assertions or screenshot review when required by the request. Sensitive UI values remain masked unless the user explicitly authorizes reading them.

Do not modify code when compilation, dependencies, preconditions, or assertions fail. Preserve the result and describe the smallest missing requirement. Generating and self-testing a new flow belongs to `gen-script`.
