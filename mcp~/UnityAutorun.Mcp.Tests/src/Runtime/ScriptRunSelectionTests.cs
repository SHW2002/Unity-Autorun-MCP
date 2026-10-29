using System;
using System.Collections.Generic;
using UnityAutorun.Scripting;

namespace UnityAutorun.Mcp.Tests
{
    internal static class ScriptRunSelectionTests
    {
        public static IEnumerable<(string Name, Action Test)> Cases()
        {
            yield return ("switching scripts never shows another script's failed result", SwitchScripts);
            yield return ("run history selects only results belonging to the current script", SelectHistory);
            yield return ("active runs take priority only within their own script", ActiveRuns);
        }

        private static void SwitchScripts()
        {
            var old = Run("old", "a", AutoRunScriptRunState.Failed);
            var runs = new List<AutoRunScriptRunStatus> { old };
            Assert(AutoRunScriptRunSelection.Select(runs, "b", "old") == null, "A stale error leaked into a different script.");
            Assert(AutoRunScriptRunSelection.Select(runs, "a", "old") == old, "Original failure history was lost.");
            Assert(AutoRunScriptRunSelection.Select(runs, "", "old") == null, "No selection showed a previous failure.");
        }

        private static void SelectHistory()
        {
            var newest = Run("new", "b", AutoRunScriptRunState.Succeeded);
            var older = Run("older", "b", AutoRunScriptRunState.Failed);
            var unrelated = Run("unrelated", "a", AutoRunScriptRunState.Failed);
            var runs = new List<AutoRunScriptRunStatus> { unrelated, newest, older };
            Assert(AutoRunScriptRunSelection.Select(runs, "b", "older") == older, "Explicit history selection was lost.");
            Assert(AutoRunScriptRunSelection.Select(runs, "b", null) == newest, "History selection displaced the latest summary.");
            Assert(AutoRunScriptRunSelection.Select(runs, "b", "unrelated") == newest, "Another script's result displaced current history.");
        }

        private static void ActiveRuns()
        {
            var activeOther = Run("other", "a", AutoRunScriptRunState.Running);
            var finished = Run("finished", "b", AutoRunScriptRunState.Succeeded);
            var active = Run("active", "b", AutoRunScriptRunState.Pending);
            var runs = new List<AutoRunScriptRunStatus> { activeOther, finished };
            Assert(AutoRunScriptRunSelection.Select(runs, "b", "") == finished, "Another active script displaced the selected script's result.");
            runs.Add(active);
            Assert(AutoRunScriptRunSelection.Select(runs, "b", null) == active, "Current active run was hidden behind history.");
            Assert(AutoRunScriptRunSelection.Select(runs, "b", "finished") == finished, "Active summary prevented inspecting history details.");
        }

        private static AutoRunScriptRunStatus Run(string runId, string scriptId, AutoRunScriptRunState state)
        {
            return new AutoRunScriptRunStatus { RunId = runId, ScriptId = scriptId, State = state };
        }

        private static void Assert(bool value, string message)
        {
            if (!value) throw new Exception(message);
        }
    }
}
