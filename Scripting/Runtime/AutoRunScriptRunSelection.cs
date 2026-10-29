using System;
using System.Collections.Generic;

namespace UnityAutorun.Scripting
{
    public static class AutoRunScriptRunSelection
    {
        public static AutoRunScriptRunStatus Select(IReadOnlyList<AutoRunScriptRunStatus> runs, string scriptId, string selectedRunId)
        {
            if (runs == null || string.IsNullOrEmpty(scriptId)) return null;
            AutoRunScriptRunStatus latest = null;
            AutoRunScriptRunStatus active = null;
            AutoRunScriptRunStatus selected = null;
            foreach (AutoRunScriptRunStatus run in runs)
            {
                if (run == null || !string.Equals(run.ScriptId, scriptId, StringComparison.Ordinal)) continue;
                if (!run.IsTerminal && active == null) active = run;
                if (latest == null) latest = run;
                if (string.Equals(run.RunId, selectedRunId, StringComparison.Ordinal)) selected = run;
            }
            return selected ?? active ?? latest;
        }
    }
}
