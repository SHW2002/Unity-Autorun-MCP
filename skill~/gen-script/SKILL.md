---
name: gen-script
description: Generate and self-test reusable Unity Autorun C# business scripts for a specific UI or project automation flow. Use when the user authorizes a new automation script or a new flow; compile through Unity, self-test and repair only code created by this task. Running an existing script belongs to run-script; maintaining an existing business script belongs to edit-script.
---

# Generate Unity Autorun Scripts

Create one concrete, named business flow in the host project's `Assets/Editor/AutorunScripts/` and prove its requested result. The Unity Editor compiles and runs the C# code; the MCP server provides commands and contracts rather than deciding business navigation. Existing scripts under the older `Assets/AutorunScripts/Editor/` directory remain available for discovery and reuse; do not move or rewrite them as part of generation.

Read `get_script_guidance` once and [references/script-contract.md](references/script-contract.md) before authoring. The live contract and tool schemas take precedence over the bundled example.

For routine operation or self-testing, first inspect `list_scripts` and `get_script(includeSource=true)` for an existing script whose complete start, flow, parameters and assertions match. Run a usable match through `run-script`. If no match exists, report that finding and ask whether the user wants a new self-test script; do not silently generate one. An explicit request to create a new script already authorizes generation. Repairing a user-specified existing business script while preserving its contract belongs to `edit-script`; a changed start or business contract receives a new ID/file here.

## Define and inspect the flow

Identify the starting scene/UI, required parameters, exact actions, and observable final assertions from the request and project evidence. Ask for the missing starting state only if the available sources and runtime state cannot resolve it. Inspect actual project code, prefabs, and narrow `get_ui_state` or `list_buttons` queries; never invent project APIs or selectors.

Before asserting a host state flag, inspect its getter and assignments to establish its meaning. An operation-in-progress flag does not prove authentication, completion, or resource availability. Preconditions must express the requested flow; do not add unrelated restrictions based on a flag's name.

For a host configuration window, follow the configuration integration and apply-path checks in the script contract. A working menu or a self-test with manually supplied parameters does not prove that the saved GUI values reach the run.

For startup navigation, inspect delayed automatic popup and loading queues in host sources and runtime evidence. When the user has authorized dismissal, verify each known popup's close control and how dismissal advances the queue, then close them in that order. Reuse an independently runnable business dismissal script when its complete flow matches. An unknown popup fails with concrete evidence. Assert completion only after the verified queues/loading have finished and the target remains stable and unobstructed; a briefly clickable home screen is insufficient.

Use `list_scripts` and `get_script(includeSource=true)` to review candidate dependencies. Reuse a script only when its complete behavior, starting requirements, and outputs exactly match the required segment. A shared target name or partial overlap does not establish equivalence. Never modify a reused business script or built-in script to fit the new task. Different flows or starting states to the same target receive separate IDs and source files.

## Generate, compile, and self-test

1. Create a public concrete `AutoRunScript` with an `AutoRunScript` attribute, descriptive ID/name, `Preconditions`, and declared dependency IDs. For a requested startup flow, set `PreparePlayMode=true` on the root business script and declare/call `start-play-mode`; its runner preparation happens before business preconditions and actions. Keep the default false for flows that require an already running game. Dependency metadata never enables preparation by itself. Override `CheckPreconditions` against the exact post-preparation starting state. Name the file and ID after this exact flow.
2. Inspect the host assembly layout as described in the contract, then use `create_script` to create a new `.cs` file beneath `Assets/Editor/AutorunScripts/`. It refuses replacement. Reuse built-ins and exact business segments through `context.Call`; use verified project APIs for behavior the generic UI operations cannot express.
3. Call `refresh_scripts`, then check `unity_status` until Unity compilation finishes. Resolve compiler messages and registration/dependency errors, and confirm the new script through `get_script`. A written file is not a compiled or validated script.
4. Self-test with `start_script` using explicit parameters and timeout. Read `get_script`'s `data.script.preparePlayMode` to confirm the compiled startup declaration: a root value of true automatically prepares Play Mode even when the caller's `ensurePlayMode` is omitted or false. The caller option still defaults to false and can explicitly request startup for an applicable existing flow; it cannot disable the script's root declaration. Business preconditions check the actual UI/scene after preparation. A cold-start flow must carry `PreparePlayMode=true` in its code instead of relying on a special caller option. Keep one `runId` for request retries and poll `get_script_status` until `terminal=true`.
5. Check the requested final assertions and the full run result, including dependency calls, logs, and artifact paths. A terminal success needs an observable assertion of the user’s goal; finishing clicks alone is insufficient. Capture screenshots only when requested or helpful for the requested review.
6. Diagnose failures and make at most three repair rounds, editing only source files created by this generation task. Recompile before each rerun and use a new `runId` for each new self-test; an old run ID returns its retained result and does not execute newly compiled code. Keep an ID only for transport retries of that same run. Never edit reused scripts, widen starting conditions to conceal a wrong start, or change assertions merely to pass. If a rerun needs a starting state that no longer exists, report it instead of silently resetting the game or choosing another flow.

## Exit and report

Success, failure, timeout, and cancellation preserve the current game state and Play Mode. `OnExit` releases only resources, subscriptions, or temporary objects owned by the script. Do not reset scenes, navigate back, or stop Play Mode automatically; use an explicit requested stop action when applicable.

Report the script ID/source, declared start, parameters, reused dependencies, compilation outcome, self-test result, concrete assertion evidence, and any unresolved failure. A compiler failure, wrong starting state, or exhausted repair budget is an unfinished result, not success.
