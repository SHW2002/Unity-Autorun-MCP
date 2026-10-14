using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed partial class AutoRunBridgeDispatcher
{
    private const float SequenceDefaultStepTimeoutSeconds = 10f;
    private AutoRunSequenceJob _sequenceJob;

    private bool StartSequence(AutoRunBridgeJob job)
    {
        AutoRunBridgeRequest request = job.Request;
        if (_sequenceJob != null || UnityAutorun.Scripting.AutoRunScriptService.HasActiveRun)
        {
            job.Response = AutoRunBridgeResponses.Fail(request.id, "sequence_busy", "Another sequence or script is still running.");
            return true;
        }

        AutoRunBridgePayload payload = request.payload ?? new AutoRunBridgePayload();
        List<AutoRunParam> actions = payload.actions ?? new List<AutoRunParam>();
        if (actions.Count == 0)
        {
            job.Response = AutoRunBridgeResponses.Success(request.id, "Executed 0 actions.", new AutoRunBridgeData());
            return true;
        }

        _sequenceJob = new AutoRunSequenceJob(job, actions);
        return false;
    }

    private void PumpSequence()
    {
        if (_sequenceJob == null)
        {
            return;
        }

        double now = EditorApplication.timeSinceStartup;
        _sequenceJob.Elapsed = (float)(now - _sequenceJob.StartedAt);
        _sequenceJob.StepElapsed = (float)(now - _sequenceJob.StepStartedAt);

        if (_sequenceJob.CurrentIndex >= _sequenceJob.Actions.Count)
        {
            CompleteSequence(AutoRunBridgeResponses.Success(_sequenceJob.Job.Request.id, $"Executed {_sequenceJob.Messages.Count} actions.", new AutoRunBridgeData
            {
                messages = _sequenceJob.Messages,
            }));
            return;
        }

        TryRunCurrentSequenceStep();
    }

    private void TryRunCurrentSequenceStep()
    {
        AutoRunParam action = _sequenceJob.Actions[_sequenceJob.CurrentIndex];
        double now = EditorApplication.timeSinceStartup;
        if (!AutoRunButtonService.HasButton(action))
        {
            CompleteSequenceOnButtonTimeout(action);
            return;
        }

        double waitedSeconds = now - _sequenceJob.StepStartedAt;
        AutoRunButtonResult result = AutoRunButtonService.Click(action);
        _sequenceJob.Messages.Add(
            $"step {_sequenceJob.CurrentIndex + 1}/{_sequenceJob.Actions.Count}: waited {waitedSeconds:0.000}s, {result.message}"
        );
        if (!result.ok)
        {
            var failed = AutoRunBridgeResponses.FromButtonResult(_sequenceJob.Id, result);
            failed.data.messages = _sequenceJob.Messages;
            CompleteSequence(failed);
            return;
        }

        _sequenceJob.CurrentIndex++;
        _sequenceJob.StepElapsed = 0f;
        _sequenceJob.StepStartedAt = EditorApplication.timeSinceStartup;
    }

    private void CompleteSequenceOnButtonTimeout(AutoRunParam action)
    {
        float timeout = SequenceDefaultStepTimeoutSeconds;
        if (_sequenceJob.StepElapsed < timeout)
        {
            return;
        }

        var failed = AutoRunBridgeResponses.FromButtonResult(
            _sequenceJob.Id,
            AutoRunButtonResult.Fail("button_wait_timeout", $"err: button '{action.buttonName}' did not appear within {timeout:0.##}s.")
        );
        failed.data.messages = _sequenceJob.Messages;
        CompleteSequence(failed);
    }

    private void CompleteSequence(AutoRunBridgeResponse response)
    {
        if (_sequenceJob == null)
        {
            return;
        }

        _sequenceJob.Job.Response = response;
        PopulateRuntimeData(response);
        LogResponse(response);
        _sequenceJob.Job.WaitHandle.Set();
        _sequenceJob = null;
    }

    private sealed class AutoRunSequenceJob
    {
        public AutoRunSequenceJob(AutoRunBridgeJob job, List<AutoRunParam> actions)
        {
            Job = job;
            Actions = actions;
            StartedAt = EditorApplication.timeSinceStartup;
            StepStartedAt = StartedAt;
        }

        public AutoRunBridgeJob Job { get; }
        public string Id => Job.Request.id;
        public List<AutoRunParam> Actions { get; }
        public List<string> Messages { get; } = new List<string>();
        public int CurrentIndex { get; set; }
        public float Elapsed { get; set; }
        public float StepElapsed { get; set; }
        public double StartedAt { get; }
        public double StepStartedAt { get; set; }
    }
}
