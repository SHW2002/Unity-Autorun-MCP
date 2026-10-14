using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityAutorun.Scripting;

public sealed partial class AutoRunBridgeDispatcher
{
    private static AutoRunBridgeResponse ListScripts(AutoRunBridgeRequest request)
    {
        var payload = request.payload ?? new AutoRunBridgePayload();
        var scripts = AutoRunScriptService.List().Where(item =>
            (string.IsNullOrWhiteSpace(payload.kind) || payload.kind == "all"
                || string.Equals(item.Kind.ToString(), payload.kind, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(payload.query) || ContainsIgnoreCase(item.Id, payload.query)
                || ContainsIgnoreCase(item.Name, payload.query) || ContainsIgnoreCase(item.Description, payload.query)));
        return AutoRunBridgeResponses.Success(request.id, "Registered scripts.", new AutoRunBridgeData
        {
            scripts = scripts.Select(ToScriptInfo).ToList(),
            catalogErrors = new List<string>(AutoRunScriptService.CatalogErrors),
        });
    }

    private static AutoRunBridgeResponse GetScript(AutoRunBridgeRequest request)
    {
        var definition = AutoRunScriptService.List().FirstOrDefault(item => item.Id == request.payload?.scriptId);
        if (definition == null) return AutoRunBridgeResponses.Fail(request.id, "script_not_found", "The requested script is not registered.");
        var info = ToScriptInfo(definition);
        if (request.payload.includeSource && !string.IsNullOrEmpty(info.sourcePath) && File.Exists(info.sourcePath))
            info.source = File.ReadAllText(info.sourcePath);
        return AutoRunBridgeResponses.Success(request.id, "Script metadata.", new AutoRunBridgeData { script = info });
    }

    private AutoRunBridgeResponse StartScript(AutoRunBridgeRequest request)
    {
        var payload = request.payload ?? new AutoRunBridgePayload();
        bool knownRun = !string.IsNullOrWhiteSpace(payload.runId) && AutoRunScriptService.GetStatus(payload.runId) != null;
        if (!knownRun && _sequenceJob != null) return AutoRunBridgeResponses.Fail(request.id, "sequence_busy", "A button sequence is still running.");
        if (!knownRun && AutoRunCompilationMonitor.IsCompiling)
            return AutoRunBridgeResponses.Fail(request.id, "scripts_compiling", "Wait for Unity compilation before starting a script.");
        try
        {
            var status = AutoRunScriptService.Start(payload.scriptId, ParseScriptParameters(payload.parametersJson),
                payload.ensurePlayMode, payload.timeoutSeconds, payload.runId);
            return ScriptResponse(request.id, status);
        }
        catch (AutoRunScriptException ex) { return AutoRunBridgeResponses.Fail(request.id, ex.Code, ex.Message); }
        catch (Exception ex) { return AutoRunBridgeResponses.Fail(request.id, "invalid_script_request", ex.Message); }
    }

    private static AutoRunBridgeResponse GetScriptStatus(AutoRunBridgeRequest request)
    {
        return ScriptResponse(request.id, AutoRunScriptService.GetStatus(request.payload?.runId));
    }

    private static AutoRunBridgeResponse CancelScript(AutoRunBridgeRequest request)
    {
        string runId = request.payload?.runId;
        if (string.IsNullOrWhiteSpace(runId)) return AutoRunBridgeResponses.Fail(request.id, "run_id_required", "runId is required.");
        if (AutoRunScriptService.GetStatus(runId) == null)
            return AutoRunBridgeResponses.Fail(request.id, "script_run_not_found", "No run exists with this runId.");
        AutoRunScriptService.Cancel(runId);
        return ScriptResponse(request.id, AutoRunScriptService.GetStatus(runId));
    }

    private static AutoRunBridgeResponse ScriptResponse(string requestId, AutoRunScriptRunStatus status)
    {
        if (status == null) return AutoRunBridgeResponses.Fail(requestId, "script_run_not_found", "No run exists with this runId.");
        var data = new AutoRunBridgeData
        {
            terminal = status.IsTerminal,
            elapsedMilliseconds = (long)(status.ElapsedSeconds * 1000),
            scriptRun = new AutoRunBridgeScriptRun
            {
                runId = status.RunId, scriptId = status.ScriptId, state = status.State.ToString().ToLowerInvariant(),
                code = status.Code, message = status.Message, startedAt = status.StartedAt,
                completedAt = status.CompletedAt, terminal = status.IsTerminal,
                elapsedMilliseconds = (long)(status.ElapsedSeconds * 1000),
                callStack = new List<string>(status.CallStack), logs = new List<string>(status.Logs),
                artifacts = new List<string>(status.Artifacts),
                calls = status.Calls.Select(call => new AutoRunBridgeScriptCall
                {
                    scriptId = call.ScriptId, parentScriptId = call.ParentScriptId, depth = call.Depth,
                    state = call.State.ToString().ToLowerInvariant(), code = call.Code, message = call.Message,
                }).ToList(),
            },
        };
        if (!status.IsTerminal || status.Succeeded)
            return AutoRunBridgeResponses.Success(requestId, status.Message ?? "Script is running.", data);
        var failure = AutoRunBridgeResponses.Fail(requestId, status.Code, status.Message);
        failure.data = data;
        return failure;
    }

    private static AutoRunBridgeScriptInfo ToScriptInfo(AutoRunScriptDefinition definition)
    {
        return new AutoRunBridgeScriptInfo
        {
            id = definition.Id, name = definition.Name, kind = definition.Kind.ToString().ToLowerInvariant(),
            description = definition.Description, preconditions = definition.Preconditions,
            dependencies = definition.Dependencies, sourcePath = definition.SourcePath,
            validationError = definition.ValidationError,
        };
    }

    public static Dictionary<string, string> ParseScriptParameters(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
        var values = AutoRunJsonParser.Parse(json) as Dictionary<string, object>;
        if (values == null) throw new ArgumentException("parametersJson must be a JSON object containing string values.");
        var parameters = new Dictionary<string, string>();
        foreach (var pair in values)
        {
            if (!(pair.Value is string)) throw new ArgumentException("Script parameter '" + pair.Key + "' must be a string.");
            parameters.Add(pair.Key, (string)pair.Value);
        }
        return parameters;
    }
}
