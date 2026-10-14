---
name: run-script
description: Run and verify an existing Unity Autorun C# automation script by ID, name, or matching business flow. Use when the user wants to execute an already available script. Inspect its declared starting state, dependencies and parameters; this skill does not generate, edit, or repair scripts.
---

# Run Unity Autorun Scripts

Execute an existing compiled script and report its concrete result. Read `get_script_guidance` once and [references/workflow.md](references/workflow.md) for the runtime contract. Live tool schemas and guidance take precedence.

Find the requested script with a targeted `list_scripts` query, then read `get_script` with source when needed to establish its complete flow. Match the declared starting state and actions, not just the final target. If several flows match or required parameters cannot be inferred, ask the smallest necessary clarification. If no existing script fits, report that finding; generating a new script is a separate `gen-script` task.

Check registration/dependency errors, required parameters, and the root script's `preparePlayMode` metadata before running. Verify the starting state with available project evidence or a narrow UI query. For a startup script with `preparePlayMode=true`, currently stopped Play Mode is expected: its business preconditions are evaluated after the runner prepares Play Mode. Do not reject the flow merely because its post-start UI is absent before preparation. If evidence disproves the actual declared start, report the mismatch without claiming an execution or inventing a run ID. If execution is started, the runner checks `CheckPreconditions` and produces the actual `precondition_failed` result when the start is wrong. Do not reset the game, choose a different path, or edit preconditions to make the run pass.

Call `start_script` once with an explicit script ID, string-valued parameters, and timeout. A root `preparePlayMode=true` automatically prepares Play Mode before preconditions/actions; this includes the built-in `start-play-mode`. Caller `ensurePlayMode` defaults to false and may explicitly request startup for an applicable existing flow, but false does not disable a script's true declaration. Do not derive preparation from dependency metadata or toggle Play Mode from an active iterator. `preparePlayMode` is returned metadata, not an input argument. Keep the returned `runId` and use the same ID for identical request retries. Poll `get_script_status` until terminal. Use `cancel_script` if the user cancels. Do not run independent duplicate requests while awaiting a result.

Inspect final assertions, dependency calls, logs, and artifacts. Finishing a sequence of actions does not prove a requested UI result. Use narrow `get_ui_state` or `wait_for_ui_state` queries for extra requested assertions. Review pixels or layout only from actual visual evidence, not structured UIState values.

Success, failure, cancellation, and timeout leave the current game state and Play Mode intact. Stop Play Mode only through an explicit requested action. Never modify any script, dependency, or project code in this workflow. Report the script/run IDs, starting conditions, parameter values relevant to the task, state/result code, observed assertions, artifacts, and unresolved failures.
