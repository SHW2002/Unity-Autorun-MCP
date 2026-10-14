using System;
using System.Collections.Generic;

namespace UnityAutorun.Scripting
{
    public enum AutoRunScriptRunState { Pending, Running, Succeeded, Failed, Canceled, TimedOut }

    [Serializable]
    public sealed class AutoRunScriptDefinition
    {
        public string Id;
        public string Name;
        public AutoRunScriptKind Kind;
        public string Description;
        public string Preconditions;
        public bool PreparePlayMode;
        public string[] Dependencies = new string[0];
        public string SourcePath;
        public string ClassName;
        public string ValidationError;
        [NonSerialized] public Func<AutoRunScript> Factory;
    }

    [Serializable]
    public class AutoRunScriptResult
    {
        public string RunId;
        public string ScriptId;
        public AutoRunScriptRunState State;
        public string Code;
        public string Message;
        public bool Succeeded { get { return State == AutoRunScriptRunState.Succeeded; } }
    }

    [Serializable]
    public sealed class AutoRunScriptRunStatus : AutoRunScriptResult
    {
        public double StartedAt;
        public double CompletedAt;
        public double ElapsedSeconds;
        public List<string> CallStack = new List<string>();
        public List<string> Logs = new List<string>();
        public List<string> Artifacts = new List<string>();
        public List<AutoRunScriptInvocation> Calls = new List<AutoRunScriptInvocation>();
        public bool IsTerminal
        {
            get { return State != AutoRunScriptRunState.Pending && State != AutoRunScriptRunState.Running; }
        }
    }

    [Serializable]
    public sealed class AutoRunScriptInvocation
    {
        public string ScriptId;
        public string ParentScriptId;
        public int Depth;
        public AutoRunScriptRunState State;
        public string Code;
        public string Message;
    }

    public sealed class AutoRunScriptOperationResult
    {
        public bool Ok;
        public bool Matched;
        public string Code;
        public string Message;
        public string ArtifactPath;

        public static AutoRunScriptOperationResult Success(string message, string artifactPath = null)
        {
            return new AutoRunScriptOperationResult { Ok = true, Code = "ok", Message = message, ArtifactPath = artifactPath };
        }
        public static AutoRunScriptOperationResult Failure(string code, string message)
        {
            return new AutoRunScriptOperationResult { Code = code, Message = message };
        }
    }

    public sealed class AutoRunScriptButtonSelector
    {
        public string Name;
        public string Text;
        public string Framework = "ugui";
        public string ObjectPath;
        public string Scope;
        public string MatchPolicy = "unique";
    }

    public sealed class AutoRunScriptUiStateQuery
    {
        public string Query;
        public string Scope;
        public bool Exact;
        public bool IncludeInactive;
        public bool IncludeSensitive;
        public List<string> Types = new List<string>();
        public string Property = "value";
        public string Comparison = "exists";
        public string Expected;
    }

    public interface IAutoRunScriptHost
    {
        bool IsPlaying { get; }
        AutoRunScriptOperationResult ClickButton(AutoRunScriptButtonSelector selector);
        AutoRunScriptOperationResult EvaluateUiState(AutoRunScriptUiStateQuery query);
        AutoRunScriptOperationResult CaptureScreenshot(string relativePath);
        AutoRunScriptOperationResult StopPlayMode();
    }
}
