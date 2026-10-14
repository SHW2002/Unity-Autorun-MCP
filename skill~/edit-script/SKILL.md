---
name: edit-script
description: Repair or optimize a user-specified existing Unity Autorun C# business script, preserving its ID and starting-state, flow, parameter and result contracts. Compile, self-test and check affected callers. Use for maintaining an existing business script; new flows belong to gen-script and execution without edits belongs to run-script.
---

# Edit Unity Autorun Scripts

Maintain the existing business script that the user has authorized you to edit. Read `get_script_guidance` once and [references/edit-contract.md](references/edit-contract.md) for source, execution and compilation details; live tool schemas and guidance take precedence. This installed Skill contains its own reference and does not require another Skill's files.

## Locate and preserve the contract

Use `list_scripts` and `get_script(includeSource=true)` to uniquely identify the requested script, its `sourcePath`, implementation and declared dependencies. Sources normally live under `Assets/Editor/AutorunScripts/`; existing sources under `Assets/AutorunScripts/Editor/` remain supported in place. For a compilation or registration failure, inspect the actual source and attribute instead of assuming the last loaded assembly represents the current file. Resolve duplicate IDs, ambiguous source paths or missing source before writing.

If the requested script is absent, report that result and ask whether the user wants a new self-test script. Do not silently create one; an existing explicit request to create a new script already supplies that authorization. If the request is execution or routine self-testing without an authorized repair, find a complete matching script and run it through `run-script` instead of entering this editing workflow.

Record the existing ID, post-preparation starting state, `PreparePlayMode`, business actions, parameter names/types/defaults and observable result assertions. Keep these contracts unchanged when repairing their implementation. A changed start, business route, preparation behavior, parameter contract or result defines a new flow: route it to `gen-script` with a separate ID/file and obtain creation authorization when it is not already part of the request. Never weaken preconditions or assertions to hide a failure.

Read candidate callers' source and dependency metadata to determine which directly or transitively call the target and whether they rely on behavior affected by the repair. Limit edits to the user-specified business source file(s). Protect dependencies, callers, built-in scripts and unrelated project code; do not repair them opportunistically or relocate existing files. A target source file containing other script classes does not authorize changing those classes. Use verified host APIs and evidence from actual source, prefabs and runtime state; keep host business behavior out of the generic tool core.

## Edit, compile and verify

1. Read `unity_status`; first require `data.scriptRunStateAvailable=true`, then confirm `data.hasActiveScriptRun=false` and `data.isCompiling=false` before changing source or triggering asset refresh. When run state is unavailable, wait for the next status; if it remains unavailable, report the missing evidence and leave source unchanged. If a run is pending/running, poll status until it ends; use `get_script_status` for its known `runId` when available, and do not cancel without authorization. If an older connection omits `scriptRunStateAvailable` or the active-run field, confirm the current runner state from known run/UI evidence or report the missing evidence before editing. Recheck immediately before each edit/refresh; recompilation during a run loses arbitrary C# iterators and must never trigger automatic replay.
2. Inspect the failing implementation and relevant host APIs, then make the smallest repair that preserves the recorded behavior contract. Declare every `context.Call` dependency; changes to internal dependency composition are allowed only when they retain the same complete business behavior. Do not change a reused dependency to make the target pass. Release only resources owned by the edited script in its exit paths.
3. Call `refresh_scripts`; poll `unity_status` until compilation and asset refresh have finished, inspect `compilerMessages`, then confirm the edited script and affected callers have no registration/dependency `validationError`. A written source or an old loaded assembly is not compilation success. If unrelated project errors prevent compilation, report them without editing unrelated code.
4. Self-test the edited script with explicit string-valued parameters and timeout using `start_script`. Use a new `runId` for each intentional test after edits/compilation; reuse it only for transport retries of that same request. Poll `get_script_status` until `terminal=true` and inspect the full result, declared assertions, dependency calls, logs and artifacts. Observe the required starting state; a startup script checks its declared state after runner preparation. Do not reset the game, stop Play Mode or choose another path to arrange a rerun without authorization.
5. Regress the affected callers with their own starts and parameters and new run IDs, sequentially after the preceding run finishes. Check their observable outcomes, not merely registration. If a caller's start or required parameters are unavailable, identify that caller as unverified and explain the missing evidence; do not claim full regression coverage.
6. Diagnose failures and make at most three repair rounds, changing only the originally specified business source file(s), recompiling and retesting each time. Do not expand the editing scope, replace the script with a new flow, relax its assertions or automatically restore a starting state. Exhausted repair rounds or an unavailable start leave an explicitly incomplete result.

## Exit and report

Success, failure, timeout and cancellation preserve the current game state and Play Mode. `OnExit` cleans only owned resources, subscriptions or temporary objects; it must tolerate partial initialization. A requested stop is a separate explicit action.

Report the changed script ID/source and implementation, preserved start/flow/parameters/results, affected callers, actual compilation outcome, each self-test/regression `runId` and concrete assertion evidence. Distinguish verified runs, failed runs and callers that could not be tested. Missing evidence, compilation failure or an exhausted repair budget is not success.
