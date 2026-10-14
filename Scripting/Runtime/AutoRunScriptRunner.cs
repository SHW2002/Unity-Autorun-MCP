using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace UnityAutorun.Scripting
{
    /// <summary>Main-thread, cooperative scheduler. Scripts must yield regularly; arbitrary C# cannot be preempted.</summary>
    public sealed class AutoRunScriptRunner
    {
        private const int MaximumDepth = 64;
        private const int MaximumLogs = 2000;
        private readonly Dictionary<string, AutoRunScriptDefinition> _definitions = new Dictionary<string, AutoRunScriptDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, Run> _runs = new Dictionary<string, Run>(StringComparer.Ordinal);
        private readonly IAutoRunScriptHost _host;
        private readonly Func<double> _clock;
        private double _now;

        public AutoRunScriptRunner(IEnumerable<AutoRunScriptDefinition> definitions, IAutoRunScriptHost host, Func<double> clock = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _clock = clock ?? (() => _now);
            SetDefinitions(definitions);
        }

        public int MaximumStepsPerTick { get; set; } = 64;
        public bool HasActiveRuns { get { return _runs.Values.Any(run => !run.Status.IsTerminal); } }
        public IEnumerable<string> ActiveRunIds { get { return _runs.Where(pair => !pair.Value.Status.IsTerminal).Select(pair => pair.Key).ToArray(); } }

        public void SetDefinitions(IEnumerable<AutoRunScriptDefinition> definitions)
        {
            _definitions.Clear();
            foreach (AutoRunScriptDefinition definition in definitions ?? new AutoRunScriptDefinition[0])
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.Id)) continue;
                AutoRunScriptDefinition existing;
                if (_definitions.TryGetValue(definition.Id, out existing))
                {
                    existing.ValidationError = definition.ValidationError = "Duplicate script ID: " + definition.Id;
                    continue;
                }
                _definitions.Add(definition.Id, definition);
            }
        }

        public string Validate(string scriptId)
        {
            return ValidateGraph(scriptId, new List<string>(), new HashSet<string>(StringComparer.Ordinal));
        }

        private string ValidateGraph(string id, List<string> visiting, HashSet<string> visited)
        {
            AutoRunScriptDefinition definition;
            if (string.IsNullOrWhiteSpace(id) || !_definitions.TryGetValue(id, out definition)) return "Missing script dependency: " + id;
            if (!string.IsNullOrWhiteSpace(definition.ValidationError)) return definition.ValidationError;
            if (definition.Factory == null) return "Script has no usable parameterless constructor: " + id;
            if (visiting.Contains(id)) return "Cyclic script dependency: " + string.Join(" -> ", visiting.Concat(new[] { id }).ToArray());
            if (visited.Contains(id)) return null;
            visiting.Add(id);
            foreach (string dependency in definition.Dependencies ?? new string[0])
            {
                string error = ValidateGraph(dependency, visiting, visited);
                if (error != null) return error;
            }
            visiting.RemoveAt(visiting.Count - 1);
            visited.Add(id);
            return null;
        }

        public string Start(string scriptId, Dictionary<string, string> parameters = null, double timeoutSeconds = 120, string runId = null)
        {
            if (double.IsNaN(timeoutSeconds) || double.IsInfinity(timeoutSeconds) || timeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            runId = string.IsNullOrWhiteSpace(runId) ? Guid.NewGuid().ToString("N") : runId;
            Run existing;
            if (_runs.TryGetValue(runId, out existing))
            {
                if (existing.Status.ScriptId != scriptId || existing.Timeout != timeoutSeconds || !EqualParameters(existing.Parameters, parameters))
                    throw new AutoRunScriptException("run_id_conflict", "This run ID belongs to a different script request.");
                return runId;
            }
            var run = new Run
            {
                Definitions = new Dictionary<string, AutoRunScriptDefinition>(_definitions, StringComparer.Ordinal),
                Parameters = parameters == null ? new Dictionary<string, string>() : new Dictionary<string, string>(parameters),
                Timeout = timeoutSeconds,
                Status = new AutoRunScriptRunStatus { RunId = runId, ScriptId = scriptId, State = AutoRunScriptRunState.Running,
                    Code = "running", Message = "Script is running.", StartedAt = _clock() }
            };
            _runs.Add(runId, run);
            string validationError = Validate(scriptId);
            if (validationError != null)
            {
                Finish(run, AutoRunScriptRunState.Failed, "script_validation_failed", validationError);
                return runId;
            }
            try { PushScript(run, scriptId, run.Parameters); }
            catch (Exception exception) { FailFromException(run, exception); }
            return runId;
        }

        public AutoRunScriptRunStatus GetStatus(string runId)
        {
            Run run;
            return runId != null && _runs.TryGetValue(runId, out run) ? run.Status : null;
        }

        public AutoRunScriptRunStatus Cancel(string runId)
        {
            Run run;
            if (runId == null || !_runs.TryGetValue(runId, out run)) return null;
            if (!run.Status.IsTerminal) Finish(run, AutoRunScriptRunState.Canceled, "script_canceled", "Script canceled; game state is preserved.");
            return run.Status;
        }

        public void FailAll(string code, string message)
        {
            foreach (Run run in _runs.Values.ToArray())
                if (!run.Status.IsTerminal) Finish(run, AutoRunScriptRunState.Failed, code, message);
        }

        public void Tick(double now)
        {
            _now = now;
            foreach (Run run in _runs.Values.ToArray())
            {
                if (run.Status.IsTerminal) continue;
                run.Status.ElapsedSeconds = Math.Max(0, now - run.Status.StartedAt);
                if (run.Status.ElapsedSeconds >= run.Timeout)
                {
                    Finish(run, AutoRunScriptRunState.TimedOut, "script_timeout", "Script exceeded its overall timeout; game state is preserved.");
                    continue;
                }
                try { Advance(run); }
                catch (Exception exception) { FailFromException(run, exception); }
            }
        }

        private void Advance(Run run)
        {
            int budget = Math.Max(1, Math.Min(4096, MaximumStepsPerTick));
            while (budget-- > 0 && run.Frames.Count > 0 && !run.Status.IsTerminal)
            {
                Frame frame = run.Frames[run.Frames.Count - 1];
                if (!frame.Initialized)
                {
                    frame.Initialized = true;
                    string error = frame.Script.CheckPreconditions(frame.Context);
                    if (error != null) throw new AutoRunScriptException("precondition_failed", string.IsNullOrWhiteSpace(error) ? "Script preconditions were not met." : error);
                    IEnumerator enumerator = frame.Script.Execute(frame.Context);
                    if (enumerator != null) frame.Enumerators.Push(enumerator);
                }
                if (frame.Wait != null)
                {
                    AutoRunScriptWait wait = frame.Wait;
                    if (wait.Condition != null)
                    {
                        if (wait.Condition()) frame.Wait = null;
                        else if (_now >= wait.Deadline) throw new AutoRunScriptException("condition_timeout", wait.Message ?? "Script condition did not become true before its timeout.");
                        else return;
                    }
                    else if (_now >= wait.Deadline) frame.Wait = null;
                    else return;
                }
                if (frame.Enumerators.Count == 0)
                {
                    ExitFrame(run, frame, AutoRunScriptRunState.Succeeded, "ok", "Script completed.");
                    run.Frames.RemoveAt(run.Frames.Count - 1);
                    UpdateStack(run);
                    if (run.Frames.Count == 0) Finish(run, AutoRunScriptRunState.Succeeded, "ok", "Script completed; game state is preserved.");
                    continue;
                }
                IEnumerator current = frame.Enumerators.Peek();
                if (!current.MoveNext())
                {
                    frame.Enumerators.Pop();
                    Dispose(current);
                    continue;
                }
                object yielded = current.Current;
                if (yielded == null) return;
                var nested = yielded as IEnumerator;
                if (nested != null)
                {
                    if (frame.Enumerators.Count >= MaximumDepth) throw new AutoRunScriptException("iterator_depth_exceeded", "Nested iterator depth exceeded.");
                    if (frame.Enumerators.Contains(nested)) throw new AutoRunScriptException("iterator_cycle", "An iterator cannot yield itself or an active ancestor iterator.");
                    frame.Enumerators.Push(nested);
                    continue;
                }
                var waitInstruction = yielded as AutoRunScriptWait;
                if (waitInstruction != null)
                {
                    waitInstruction.Deadline = _now + waitInstruction.Seconds;
                    frame.Wait = waitInstruction;
                    continue;
                }
                var call = yielded as AutoRunScriptCall;
                if (call != null)
                {
                    if (!(frame.Definition.Dependencies ?? new string[0]).Contains(call.ScriptId))
                        throw new AutoRunScriptException("undeclared_dependency", "Script '" + frame.Definition.Id + "' did not declare dependency '" + call.ScriptId + "'.");
                    PushScript(run, call.ScriptId, call.Parameters);
                    continue;
                }
                throw new AutoRunScriptException("unsupported_yield", "Unsupported yield type: " + yielded.GetType().FullName + ". Use null, IEnumerator, context.Wait, context.WaitUntil, or context.Call.");
            }
        }

        private void PushScript(Run run, string scriptId, Dictionary<string, string> parameters)
        {
            if (run.Frames.Count >= MaximumDepth) throw new AutoRunScriptException("call_depth_exceeded", "Script call depth exceeded.");
            if (run.Frames.Any(activeFrame => activeFrame.Definition.Id == scriptId)) throw new AutoRunScriptException("script_cycle", "Recursive script call: " + scriptId);
            AutoRunScriptDefinition definition;
            if (!run.Definitions.TryGetValue(scriptId ?? string.Empty, out definition)) throw new AutoRunScriptException("script_not_found", "Script not found: " + scriptId);
            if (run.Status.Calls.Count >= 10000) throw new AutoRunScriptException("script_call_limit", "Script exceeded the maximum number of recorded calls.");
            var invocation = new AutoRunScriptInvocation { ScriptId = scriptId, Depth = run.Frames.Count,
                ParentScriptId = run.Frames.Count == 0 ? null : run.Frames[run.Frames.Count - 1].Definition.Id, State = AutoRunScriptRunState.Running };
            AutoRunScript script = definition.Factory();
            if (script == null) throw new AutoRunScriptException("script_factory_failed", "Script factory returned null: " + scriptId);
            var frame = new Frame { Definition = definition, Script = script, Invocation = invocation };
            frame.Context = new AutoRunScriptContext(run.Status.RunId, scriptId, parameters, _host,
                message => AppendLog(run, message), path => { if (!run.Status.Artifacts.Contains(path)) run.Status.Artifacts.Add(path); });
            run.Frames.Add(frame);
            run.Status.Calls.Add(invocation);
            UpdateStack(run);
            frame.Context.Log("Started.");
        }

        private void ExitFrame(Run run, Frame frame, AutoRunScriptRunState state, string code, string message)
        {
            frame.Invocation.State = state;
            frame.Invocation.Code = code;
            frame.Invocation.Message = message;
            Exception firstError = null;
            while (frame.Enumerators.Count > 0)
            {
                IEnumerator enumerator = frame.Enumerators.Pop();
                try { Dispose(enumerator); }
                catch (Exception exception) { firstError = firstError ?? exception; AppendLog(run, "Iterator cleanup failed: " + exception.Message); }
            }
            if (!frame.Exited)
            {
                frame.Exited = true;
                try { frame.Script.OnExit(frame.Context, new AutoRunScriptResult { RunId = run.Status.RunId, ScriptId = frame.Definition.Id, State = state, Code = code, Message = message }); }
                catch (Exception exception) { firstError = firstError ?? exception; AppendLog(run, "OnExit failed for '" + frame.Definition.Id + "': " + exception.Message); }
            }
            if (firstError != null && state == AutoRunScriptRunState.Succeeded)
                throw new AutoRunScriptException("script_cleanup_error", firstError.Message);
        }

        private void Finish(Run run, AutoRunScriptRunState state, string code, string message)
        {
            if (run.Status.IsTerminal) return;
            UpdateStack(run);
            run.Status.State = state;
            run.Status.Code = code;
            run.Status.Message = message;
            run.Status.CompletedAt = _clock();
            run.Status.ElapsedSeconds = Math.Max(0, run.Status.CompletedAt - run.Status.StartedAt);
            for (int index = run.Frames.Count - 1; index >= 0; index--)
                ExitFrame(run, run.Frames[index], state, code, message);
            run.Frames.Clear();
            AppendLog(run, message);
        }

        private void FailFromException(Run run, Exception exception)
        {
            var scriptException = exception as AutoRunScriptException;
            Finish(run, AutoRunScriptRunState.Failed, scriptException == null ? "script_exception" : scriptException.Code,
                exception.GetType().Name + ": " + exception.Message);
        }

        private static void UpdateStack(Run run) { run.Status.CallStack = run.Frames.Select(frame => frame.Definition.Id).ToList(); }
        private static void Dispose(IEnumerator enumerator) { var disposable = enumerator as IDisposable; if (disposable != null) disposable.Dispose(); }
        private static void AppendLog(Run run, string message)
        {
            if (run.Status.Logs.Count >= MaximumLogs) run.Status.Logs.RemoveAt(0);
            run.Status.Logs.Add(message ?? string.Empty);
        }
        private static bool EqualParameters(Dictionary<string, string> left, Dictionary<string, string> right)
        {
            if ((left == null ? 0 : left.Count) != (right == null ? 0 : right.Count)) return false;
            if (left == null || left.Count == 0) return true;
            return left.All(pair => right.ContainsKey(pair.Key) && right[pair.Key] == pair.Value);
        }

        private sealed class Run
        {
            public AutoRunScriptRunStatus Status;
            public Dictionary<string, AutoRunScriptDefinition> Definitions;
            public Dictionary<string, string> Parameters;
            public double Timeout;
            public readonly List<Frame> Frames = new List<Frame>();
        }
        private sealed class Frame
        {
            public AutoRunScriptDefinition Definition;
            public AutoRunScript Script;
            public AutoRunScriptContext Context;
            public AutoRunScriptInvocation Invocation;
            public readonly Stack<IEnumerator> Enumerators = new Stack<IEnumerator>();
            public AutoRunScriptWait Wait;
            public bool Initialized;
            public bool Exited;
        }
    }
}
