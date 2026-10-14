using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

public sealed partial class AutoRunBridgeDispatcher
{
    private readonly Queue<AutoRunBridgeJob> _jobs = new();
    private readonly object _lock = new();

    public AutoRunBridgeResponse ExecuteStatus(string id)
    {
        return AutoRunBridgeResponses.Success(id, "AutoRun bridge is available.", new AutoRunBridgeData
        {
            isPlaying = EditorApplication.isPlaying,
            unityVersion = Application.unityVersion,
            bridgeVersion = AutoRunBridgeServer.Version,
        });
    }

    public AutoRunBridgeResponse Enqueue(string requestJson)
    {
        AutoRunBridgeRequest request;
        try
        {
            request = JsonUtility.FromJson<AutoRunBridgeRequest>(requestJson);
        }
        catch (Exception ex)
        {
            return AutoRunBridgeResponses.Fail(null, "invalid_json", ex.Message);
        }

        if (request == null || string.IsNullOrEmpty(request.command))
        {
            return AutoRunBridgeResponses.Fail(null, "invalid_request", "Request command is required.");
        }

        var job = new AutoRunBridgeJob(request);
        lock (_lock)
        {
            _jobs.Enqueue(job);
        }

        if (!job.WaitHandle.WaitOne(GetRequestTimeout(request)))
        {
            return AutoRunBridgeResponses.Fail(request.id, "timeout", "Unity bridge request timed out.");
        }

        return job.Response;
    }

    public void Pump()
    {
        if (_uiStateWaitJob != null)
        {
            PumpUiStateWait();
        }

        if (_sequenceJob != null)
        {
            PumpSequence();
        }

        AutoRunBridgeJob job = null;
        lock (_lock)
        {
            if (_jobs.Count > 0)
            {
                job = _jobs.Dequeue();
            }
        }

        if (job == null)
        {
            return;
        }

        bool shouldComplete = true;
        try
        {
            job.Response = Execute(job.Request, job, out shouldComplete);
        }
        catch (Exception ex)
        {
            job.Response = AutoRunBridgeResponses.Fail(job.Request.id, "execution_error", ex.Message);
        }
        finally
        {
            if (shouldComplete)
            {
                PopulateRuntimeData(job.Response);
                LogResponse(job.Response);
                job.WaitHandle.Set();
            }
        }
    }

    private AutoRunBridgeResponse Execute(AutoRunBridgeRequest request, AutoRunBridgeJob job, out bool shouldComplete)
    {
        shouldComplete = true;
        switch (request.command)
        {
            case "status":
                return ExecuteStatus(request.id);
            case "play":
                EditorApplication.isPlaying = true;
                return AutoRunBridgeResponses.Success(request.id, "Play mode requested.");
            case "stop":
                EditorApplication.isPlaying = false;
                return AutoRunBridgeResponses.Success(request.id, "Stop requested.");
            case "list_buttons":
                return ListButtons(request);
            case "list_open_views":
                return ListOpenViews(request);
            case "is_ui_view_open":
                return IsUiViewOpen(request);
            case "get_ui_state":
                return GetUiState(request);
            case "wait_for_ui_state":
                shouldComplete = StartUiStateWait(job);
                return job.Response;
            case "click_button":
                return ClickButton(request);
            case "run_sequence":
                shouldComplete = StartSequence(job);
                return job.Response;
            case "list_scripts":
                return ListScripts(request);
            case "get_script":
                return GetScript(request);
            case "refresh_scripts":
                AutoRunCompilationMonitor.RequestRefresh();
                return AutoRunBridgeResponses.Success(request.id, "Script refresh requested; poll unity_status and list_scripts to confirm compilation.");
            case "start_script":
                return StartScript(request);
            case "get_script_status":
                return GetScriptStatus(request);
            case "cancel_script":
                return CancelScript(request);
            default:
                return AutoRunBridgeResponses.Fail(request.id, "unknown_command", $"Unknown command: {request.command}");
        }
    }

    private static AutoRunBridgeResponse ListButtons(AutoRunBridgeRequest request)
    {
        AutoRunBridgePayload payload = request.payload ?? new AutoRunBridgePayload();
        string framework = string.IsNullOrEmpty(payload.framework) ? "all" : payload.framework;
        List<AutoRunButtonInfo> allButtons = AutoRunButtonService.ListButtons(framework);
        IEnumerable<AutoRunButtonInfo> matches = allButtons;
        if (!string.IsNullOrWhiteSpace(payload.query))
        {
            matches = payload.exact
                ? matches.Where(button => string.Equals(button.name, payload.query, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(button.text, payload.query, StringComparison.OrdinalIgnoreCase))
                : matches.Where(button => ContainsIgnoreCase(button.name, payload.query)
                    || ContainsIgnoreCase(button.text, payload.query));
        }

        List<AutoRunButtonInfo> matchedButtons = matches.ToList();
        int limit = Math.Max(1, Math.Min(200, payload.limit <= 0 ? 100 : payload.limit));
        List<AutoRunButtonInfo> returnedButtons = matchedButtons.Take(limit).ToList();
        var data = new AutoRunBridgeData
        {
            framework = framework,
            buttonCount = matchedButtons.Count,
            truncated = matchedButtons.Count > returnedButtons.Count,
        };
        if (payload.namesOnly)
        {
            data.buttonNames = returnedButtons.Select(button => button.name).Distinct().ToList();
        }
        else
        {
            data.buttons = returnedButtons;
        }

        return AutoRunBridgeResponses.Success(request.id, $"Found {matchedButtons.Count} matching buttons.", data);
    }

    private static AutoRunBridgeResponse ClickButton(AutoRunBridgeRequest request)
    {
        AutoRunParam param = ToParam(request.payload);
        AutoRunButtonResult result = AutoRunButtonService.Click(param);
        return AutoRunBridgeResponses.FromButtonResult(request.id, result);
    }

    private static AutoRunBridgeResponse ListOpenViews(AutoRunBridgeRequest request)
    {
        AutoRunBridgePayload payload = request.payload ?? new AutoRunBridgePayload();
        int total;
        List<string> openViews = AutoRunViewService.ListOpenViewNames(
            payload.query,
            payload.exact,
            payload.limit,
            out total
        );
        return AutoRunBridgeResponses.Success(request.id, $"Found {total} matching active view candidates.", new AutoRunBridgeData
        {
            openViews = openViews,
            openViewCount = total,
            truncated = total > openViews.Count,
        });
    }

    private static AutoRunBridgeResponse IsUiViewOpen(AutoRunBridgeRequest request)
    {
        AutoRunBridgePayload payload = request.payload ?? new AutoRunBridgePayload();
        string query = string.IsNullOrEmpty(payload.targetViewId) ? payload.query : payload.targetViewId;
        if (string.IsNullOrWhiteSpace(query))
        {
            return AutoRunBridgeResponses.Fail(request.id, "view_required", "targetViewId or query is required.");
        }

        int total;
        List<string> matches = AutoRunViewService.ListOpenViewMatches(query, 10, out total);
        return AutoRunBridgeResponses.Success(request.id, total > 0 ? "UI view is open." : "UI view is not open.", new AutoRunBridgeData
        {
            viewOpen = total > 0,
            openViewCount = total,
            openViews = matches,
            truncated = total > matches.Count,
        });
    }

    private static bool ContainsIgnoreCase(string value, string query)
    {
        return !string.IsNullOrEmpty(value)
            && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static AutoRunParam ToParam(AutoRunBridgePayload payload)
    {
        return new AutoRunParam
        {
            buttonName = payload?.name ?? AutoRunParam.DEFAULT_NAME,
            buttonText = string.IsNullOrEmpty(payload?.text) ? AutoRunParam.DEFAULT_TEXT : payload.text,
            isFairyGUI = (payload?.framework ?? "ugui").ToLowerInvariant() == "fairygui",
        };
    }

    private static void LogResponse(AutoRunBridgeResponse response)
    {
        if (response == null)
        {
            return;
        }

        AutoRunWindow.AppendBridgeConsoleText($"MCP {response.code}: {response.message}",
            response.ok ? AutoRunLogLevel.Info : AutoRunLogLevel.Error);
        if (response.data?.messages == null)
        {
            return;
        }

        foreach (string message in response.data.messages)
        {
            AutoRunWindow.AppendBridgeConsoleText(message, AutoRunLogLevel.Debug);
        }
    }

    private static void PopulateRuntimeData(AutoRunBridgeResponse response)
    {
        if (response == null)
        {
            return;
        }

        response.data = response.data ?? new AutoRunBridgeData();
        response.data.isPlaying = EditorApplication.isPlaying;
        response.data.unityVersion = Application.unityVersion;
        response.data.bridgeVersion = AutoRunBridgeServer.Version;
        response.data.isCompiling = AutoRunCompilationMonitor.IsCompiling;
        response.data.compilerMessages = AutoRunCompilationMonitor.Messages;
    }

    private static TimeSpan GetRequestTimeout(AutoRunBridgeRequest request)
    {
        const double defaultSeconds = 30;
        const double maximumSeconds = 300;
        if (request?.command == "run_sequence")
        {
            int count = request.payload?.actions?.Count ?? 0;
            return TimeSpan.FromSeconds(Math.Max(defaultSeconds, Math.Min(maximumSeconds, count * 10 + 5)));
        }

        if (request?.command == "wait_for_ui_state")
        {
            int requestedMilliseconds = request.payload?.timeoutMilliseconds ?? 10000;
            double seconds = Math.Max(0, requestedMilliseconds) / 1000d + 5;
            return TimeSpan.FromSeconds(
                Math.Max(
                    defaultSeconds,
                    Math.Min(maximumSeconds, seconds)));
        }

        return TimeSpan.FromSeconds(defaultSeconds);
    }

    private sealed class AutoRunBridgeJob
    {
        public AutoRunBridgeJob(AutoRunBridgeRequest request)
        {
            Request = request;
        }

        public AutoRunBridgeRequest Request { get; }
        public AutoRunBridgeResponse Response { get; set; }
        public ManualResetEvent WaitHandle { get; } = new ManualResetEvent(false);
    }
}
