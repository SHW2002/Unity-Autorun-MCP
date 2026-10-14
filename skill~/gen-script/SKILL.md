---
name: gen-script
description: Generate and self-test reusable Unity Autorun C# business scripts for a specific UI or project automation flow. Use when the user requests a new automation script or a new flow; compile through Unity, run the requested self-test, and repair only code created by this task. Running an existing script belongs to run-script.
---

# Generate Unity Autorun Scripts

Create one concrete, named business flow in the host project's `Assets/AutorunScripts/Editor/` and prove its requested result. The Unity Editor compiles and runs the C# code; the MCP server provides commands and contracts rather than deciding business navigation.

Read `get_script_guidance` once and [references/script-contract.md](references/script-contract.md) before authoring. The live contract and tool schemas take precedence over the bundled example.

## Define and inspect the flow

Identify the starting scene/UI, required parameters, exact actions, and observable final assertions from the request and project evidence. Ask for the missing starting state only if the available sources and runtime state cannot resolve it. Inspect actual project code, prefabs, and narrow `get_ui_state` or `list_buttons` queries; never invent project APIs or selectors.

Use `list_scripts` and `get_script(includeSource=true)` to review candidate dependencies. Reuse a script only when its complete behavior, starting requirements, and outputs exactly match the required segment. A shared target name or partial overlap does not establish equivalence. Never modify a reused business script or built-in script to fit the new task. Different flows or starting states to the same target receive separate IDs and source files.

## Generate, compile, and self-test

1. Create a public concrete `AutoRunScript` with an `AutoRunScript` attribute, descriptive ID/name, `Preconditions`, and declared dependency IDs. Override `CheckPreconditions` to check the actual starting state before performing any action. Name the file and ID after this exact flow.
2. Inspect the host assembly layout as described in the contract, then use `create_script` to create a new `.cs` file beneath `Assets/AutorunScripts/Editor/`. It refuses replacement. Reuse built-ins and exact business segments through `context.Call`; use verified project APIs for behavior the generic UI operations cannot express.
3. Call `refresh_scripts`, then check `unity_status` until Unity compilation finishes. Resolve compiler messages and registration/dependency errors, and confirm the new script through `get_script`. A written file is not a compiled or validated script.
4. Self-test with `start_script` using explicit parameters, timeout, and `ensurePlayMode`. The latter defaults to false: enable it only when the user requests starting the game. Play Mode startup is the runner's preparation stage; check the business preconditions against the actual UI/scene after that preparation. For a flow requested from stopped Unity, document `ensurePlayMode=true` and the specific post-start scene/UI. Keep one `runId` for request retries and poll `get_script_status` until `terminal=true`.
5. Check the requested final assertions and the full run result, including dependency calls, logs, and artifact paths. A terminal success needs an observable assertion of the user’s goal; finishing clicks alone is insufficient. Capture screenshots only when requested or helpful for the requested review.
6. Diagnose failures and make at most three repair rounds, editing only source files created by this generation task. Recompile before each rerun and use a new `runId` for each new self-test; an old run ID returns its retained result and does not execute newly compiled code. Keep an ID only for transport retries of that same run. Never edit reused scripts, widen starting conditions to conceal a wrong start, or change assertions merely to pass. If a rerun needs a starting state that no longer exists, report it instead of silently resetting the game or choosing another flow.

## Exit and report

Success, failure, timeout, and cancellation preserve the current game state and Play Mode. `OnExit` releases only resources, subscriptions, or temporary objects owned by the script. Do not reset scenes, navigate back, or stop Play Mode automatically; use an explicit requested stop action when applicable.

Report the script ID/source, declared start, parameters, reused dependencies, compilation outcome, self-test result, concrete assertion evidence, and any unresolved failure. A compiler failure, wrong starting state, or exhausted repair budget is an unfinished result, not success.
