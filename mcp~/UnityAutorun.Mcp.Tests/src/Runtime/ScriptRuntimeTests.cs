using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityAutorun.Scripting;

namespace UnityAutorun.Mcp.Tests
{
    internal static class ScriptRuntimeTests
    {
        public static IEnumerable<(string Name, Action Test)> Cases()
        {
            yield return ("script success preserves game and releases resources once", Success);
            yield return ("child failure propagates and cleans child before parent", ChildFailure);
            yield return ("child precondition failure prevents execution", Preconditions);
            yield return ("cancel disposes nested iterators and preserves game", Cancellation);
            yield return ("overall timeout terminates and cleans scripts", Timeout);
            yield return ("condition waits use scheduler time and observe outcomes", ConditionWait);
            yield return ("missing and cyclic dependencies fail before constructors", InvalidDependencies);
            yield return ("undeclared child cannot execute", UndeclaredChild);
            yield return ("run IDs prevent duplicate execution and detect conflicts", Idempotency);
            yield return ("zero-duration waits cannot monopolize a scheduler tick", Budget);
            yield return ("cleanup exception becomes failure without repeating OnExit", CleanupFailure);
            yield return ("unsupported Unity yield instruction fails explicitly", UnsupportedYield);
            yield return ("subscript receives only explicitly passed parameters", Parameters);
            yield return ("child preparation metadata never starts Play Mode during a flow", ChildPreparation);
        }

        private static void Success()
        {
            var host = new FakeHost();
            int exits = 0, disposed = 0;
            var script = new Probe(c => Resource(c, () => disposed++), null, (c, r) =>
            { exits++; Assert(r.Succeeded, "success hook result"); });
            var runner = Runner(host, Definition("root", script));
            string id = runner.Start("root");
            runner.Tick(0); runner.Tick(1); runner.Tick(2);
            var status = runner.GetStatus(id);
            Assert(status.Succeeded && exits == 1 && disposed == 1, "exactly once cleanup");
            Assert(status.Artifacts.SequenceEqual(new[] { "proof.png" }), "artifact recorded");
            Assert(host.IsPlaying && host.Stops == 0, "game remains running");
        }

        private static IEnumerator Resource(AutoRunScriptContext context, Action dispose)
        {
            try { context.AddArtifact("proof.png"); yield return null; }
            finally { dispose(); }
        }

        private static void ChildFailure()
        {
            var order = new List<string>(); bool reachedTail = false;
            var parent = new Probe(c => Parent(c, () => reachedTail = true), null,
                (c, r) => { order.Add("parent"); Assert(!r.Succeeded, "parent receives failure"); });
            var child = new Probe(c => Throw(c), null, (c, r) => order.Add("child"));
            var runner = Runner(new FakeHost(), Definition("parent", parent, "child"), Definition("child", child));
            string id = runner.Start("parent"); runner.Tick(0);
            var status = runner.GetStatus(id);
            Assert(status.Code == "goal_not_reached" && status.State == AutoRunScriptRunState.Failed, "child code propagated");
            Assert(!reachedTail && order.SequenceEqual(new[] { "child", "parent" }), "tail skipped, reverse cleanup");
            Assert(status.CallStack.SequenceEqual(new[] { "parent", "child" }), "failure call stack retained");
        }

        private static IEnumerator Parent(AutoRunScriptContext context, Action tail)
        { yield return context.Call("child"); tail(); }
        private static IEnumerator Throw(AutoRunScriptContext context)
        { context.Fail("goal_not_reached", "Expected target is absent."); yield break; }

        private static void Preconditions()
        {
            int executes = 0, exits = 0;
            var child = new Probe(c => Count(() => executes++), c => "This route starts on the main screen.", (c, r) => exits++);
            var runner = Runner(new FakeHost(), Definition("parent", new Probe(c => Parent(c, () => { })), "child"), Definition("child", child));
            string id = runner.Start("parent"); runner.Tick(0);
            Assert(runner.GetStatus(id).Code == "precondition_failed" && executes == 0 && exits == 1, "wrong start has no execution");
        }

        private static void Cancellation()
        {
            int innerDispose = 0, outerDispose = 0, exits = 0;
            var host = new FakeHost();
            var script = new Probe(c => Nested(() => innerDispose++, () => outerDispose++), null, (c, r) => exits++);
            var runner = Runner(host, Definition("root", script));
            string id = runner.Start("root"); runner.Tick(0);
            runner.Cancel(id); runner.Cancel(id); runner.Tick(100);
            Assert(runner.GetStatus(id).State == AutoRunScriptRunState.Canceled, "terminal cancellation");
            Assert(innerDispose == 1 && outerDispose == 1 && exits == 1 && host.Stops == 0, "nested cleanup exactly once");
        }

        private static IEnumerator Nested(Action inner, Action outer)
        { try { yield return Infinite(inner); } finally { outer(); } }
        private static IEnumerator Infinite(Action dispose)
        { try { while (true) yield return null; } finally { dispose(); } }

        private static void Timeout()
        {
            int exits = 0;
            var runner = Runner(new FakeHost(), Definition("root", new Probe(c => Infinite(() => { }), null, (c, r) => exits++)));
            string id = runner.Start("root", timeoutSeconds: 2); runner.Tick(0); runner.Tick(2); runner.Tick(3);
            Assert(runner.GetStatus(id).State == AutoRunScriptRunState.TimedOut && exits == 1, "timeout closes script once");
        }

        private static void ConditionWait()
        {
            bool ready = false, reached = false;
            var runner = Runner(new FakeHost(), Definition("root", new Probe(c => Wait(c, () => ready, () => reached = true))));
            string id = runner.Start("root"); runner.Tick(0); runner.Tick(1);
            Assert(!reached && !runner.GetStatus(id).IsTerminal, "condition not guessed");
            ready = true; runner.Tick(1.5);
            Assert(reached && runner.GetStatus(id).Succeeded, "observed condition proceeds");
            var second = Runner(new FakeHost(), Definition("root", new Probe(c => Wait(c, () => false, () => { }))));
            id = second.Start("root"); second.Tick(0); second.Tick(3);
            Assert(second.GetStatus(id).Code == "condition_timeout", "bounded condition wait");
        }

        private static IEnumerator Wait(AutoRunScriptContext context, Func<bool> condition, Action next)
        { yield return context.WaitUntil(condition, 2, "Target did not appear."); next(); }

        private static void InvalidDependencies()
        {
            int constructors = 0;
            Func<AutoRunScript> create = () => { constructors++; return new Probe(c => Count(() => { })); };
            var root = new AutoRunScriptDefinition { Id = "root", Dependencies = new[] { "missing" }, Factory = create };
            var runner = Runner(new FakeHost(), root);
            string id = runner.Start("root");
            Assert(runner.GetStatus(id).Code == "script_validation_failed" && constructors == 0, "missing dependency prevents execution");
            root.Dependencies = new[] { "child" };
            var child = new AutoRunScriptDefinition { Id = "child", Dependencies = new[] { "root" }, Factory = create };
            runner = Runner(new FakeHost(), root, child); id = runner.Start("root");
            Assert(runner.GetStatus(id).IsTerminal && constructors == 0, "cycle rejected before side effects");
        }

        private static void UndeclaredChild()
        {
            int childStarts = 0;
            var runner = Runner(new FakeHost(), Definition("parent", new Probe(c => Parent(c, () => { }))),
                Definition("child", new Probe(c => Count(() => childStarts++))));
            string id = runner.Start("parent"); runner.Tick(0);
            Assert(runner.GetStatus(id).Code == "undeclared_dependency" && childStarts == 0, "hidden calls cannot bypass dependency catalog");
        }

        private static void Idempotency()
        {
            int starts = 0;
            var runner = Runner(new FakeHost(), Definition("root", new Probe(c => Count(() => starts++))));
            var parameters = new Dictionary<string, string> { ["name"] = "a" };
            runner.Start("root", parameters, runId: "same"); runner.Start("root", parameters, runId: "same"); runner.Tick(0);
            runner.Start("root", parameters, runId: "same");
            Assert(starts == 1, "retry never repeats a completed run");
            bool refused = false;
            try { runner.Start("root", new Dictionary<string, string> { ["name"] = "b" }, runId: "same"); }
            catch (AutoRunScriptException e) { refused = e.Code == "run_id_conflict"; }
            Assert(refused, "changed request cannot reuse run ID");
        }

        private static void Budget()
        {
            int steps = 0;
            var runner = Runner(new FakeHost(), Definition("root", new Probe(c => Busy(c, () => steps++))));
            runner.MaximumStepsPerTick = 4;
            string id = runner.Start("root"); runner.Tick(0);
            Assert(steps > 0 && steps <= 4 && !runner.GetStatus(id).IsTerminal, "tick budget enforced");
            runner.Cancel(id);
        }
        private static IEnumerator Busy(AutoRunScriptContext context, Action step)
        { while (true) { step(); yield return context.Wait(0); } }

        private static void CleanupFailure()
        {
            int exits = 0;
            var runner = Runner(new FakeHost(), Definition("root", new Probe(c => Count(() => { }), null,
                (c, r) => { exits++; throw new InvalidOperationException("cleanup failed"); })));
            string id = runner.Start("root"); runner.Tick(0); runner.Tick(1); runner.Cancel(id);
            Assert(runner.GetStatus(id).Code == "script_cleanup_error" && exits == 1, "cleanup error is observable, hook not repeated");
        }

        private static void UnsupportedYield()
        {
            var runner = Runner(new FakeHost(), Definition("root", new Probe(c => Unknown())));
            string id = runner.Start("root"); runner.Tick(0);
            Assert(runner.GetStatus(id).Code == "unsupported_yield", "unsupported wait is not silently treated as success");
        }
        private static IEnumerator Unknown() { yield return new object(); }

        private static void Parameters()
        {
            string observed = null;
            var runner = Runner(new FakeHost(), Definition("parent", new Probe(c => WithParameters(c)), "child"),
                Definition("child", new Probe(c => Count(() => observed = c.GetString("parentOnly", "absent")))));
            string id = runner.Start("parent", new Dictionary<string, string> { ["parentOnly"] = "secret" }); runner.Tick(0);
            Assert(runner.GetStatus(id).Succeeded && observed == "absent", "parameters do not leak implicitly to children");
        }
        private static IEnumerator WithParameters(AutoRunScriptContext context)
        { yield return context.Call("child", new Dictionary<string, string> { ["childOnly"] = "yes" }); }

        private static void ChildPreparation()
        {
            var host = new FakeHost { Playing = false };
            bool parentAction = false, childAction = false, tail = false;
            var parent = Definition("parent", new Probe(c => PreparationParent(c, () => parentAction = true, () => tail = true)), "child");
            var child = Definition("child", new Probe(c => Count(() => childAction = true),
                c => c.Host.IsPlaying ? null : "Initial Play Mode preparation was not declared by the root."));
            child.PreparePlayMode = true;
            var runner = Runner(host, parent, child);
            string id = runner.Start("parent");
            runner.Tick(0);
            Assert(parentAction && !childAction && !tail, "parent actions are not replayed and unprepared child actions never run");
            Assert(runner.GetStatus(id).Code == "precondition_failed" && !host.IsPlaying && host.Stops == 0,
                "child metadata does not alter Play Mode or silently continue a different route");
        }

        private static IEnumerator PreparationParent(AutoRunScriptContext context, Action before, Action after)
        {
            before();
            yield return context.Call("child");
            after();
        }
        private static IEnumerator Count(Action action) { action(); yield break; }

        private static AutoRunScriptRunner Runner(FakeHost host, params AutoRunScriptDefinition[] definitions)
        { return new AutoRunScriptRunner(definitions, host); }
        private static AutoRunScriptDefinition Definition(string id, AutoRunScript script, params string[] dependencies)
        { return new AutoRunScriptDefinition { Id = id, Name = id, Kind = AutoRunScriptKind.Basic, Dependencies = dependencies, Factory = () => script }; }
        private static void Assert(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private sealed class Probe : AutoRunScript
        {
            private readonly Func<AutoRunScriptContext, IEnumerator> _execute;
            private readonly Func<AutoRunScriptContext, string> _check;
            private readonly Action<AutoRunScriptContext, AutoRunScriptResult> _exit;
            public Probe(Func<AutoRunScriptContext, IEnumerator> execute, Func<AutoRunScriptContext, string> check = null,
                Action<AutoRunScriptContext, AutoRunScriptResult> exit = null)
            { _execute = execute; _check = check; _exit = exit; }
            public override IEnumerator Execute(AutoRunScriptContext context) { return _execute(context); }
            public override string CheckPreconditions(AutoRunScriptContext context) { return _check == null ? null : _check(context); }
            public override void OnExit(AutoRunScriptContext context, AutoRunScriptResult result) { if (_exit != null) _exit(context, result); }
        }
        private sealed class FakeHost : IAutoRunScriptHost
        {
            public bool Playing = true;
            public bool IsPlaying { get { return Playing; } }
            public int Stops;
            public AutoRunScriptOperationResult ClickButton(AutoRunScriptButtonSelector selector) { return AutoRunScriptOperationResult.Success("clicked"); }
            public AutoRunScriptOperationResult EvaluateUiState(AutoRunScriptUiStateQuery query) { return AutoRunScriptOperationResult.Success("matched"); }
            public AutoRunScriptOperationResult CaptureScreenshot(string path) { return AutoRunScriptOperationResult.Success("captured", path); }
            public AutoRunScriptOperationResult StopPlayMode() { Stops++; return AutoRunScriptOperationResult.Success("stopped"); }
        }
    }
}
