using System;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace UnityAutorun.Mcp
{
    public static class ScriptRequests
    {
        public static JsonObject Start(JsonObject args)
        {
            string scriptId = ToolDispatcher.Required(args, "scriptId");
            if (args["parameters"] != null && args["parametersJson"] != null)
                throw new ArgumentException("Use parameters or parametersJson, not both.");
            JsonNode parameters = args["parameters"];
            if (parameters is JsonValue) parameters = JsonNode.Parse(parameters.GetValue<string>());
            if (args["parametersJson"] != null) parameters = JsonNode.Parse(args["parametersJson"].GetValue<string>());
            var values = parameters == null && args["parametersJson"] == null && args["parameters"] == null
                ? new JsonObject() : parameters as JsonObject;
            if (values == null) throw new ArgumentException("Script parameters must be a JSON object with string values.");
            foreach (var pair in values)
                if (!(pair.Value is JsonValue value) || !value.TryGetValue<string>(out string ignored))
                    throw new ArgumentException("Script parameter '" + pair.Key + "' must be a string.");

            double timeout = args["timeoutSeconds"]?.GetValue<double>() ?? 120;
            if (double.IsNaN(timeout) || double.IsInfinity(timeout) || timeout <= 0 || timeout > 86400)
                throw new ArgumentException("timeoutSeconds must be greater than 0 and at most 86400.");
            string runId = ToolDispatcher.Text(args, "runId");
            if (string.IsNullOrWhiteSpace(runId)) runId = "script-" + Guid.NewGuid().ToString("N");
            return JsonUtil.Obj(("scriptId", scriptId), ("parametersJson", values.ToJsonString()),
                ("ensurePlayMode", ToolDispatcher.Bool(args, "ensurePlayMode", false)),
                ("timeoutSeconds", timeout), ("runId", runId));
        }

        public static async Task<JsonNode> GetStatusAsync(Func<string, JsonObject, Task<JsonNode>> call, JsonObject args)
        {
            string runId = ToolDispatcher.Required(args, "runId");
            int waitMs = Math.Max(0, Math.Min(30000, ToolDispatcher.Int(args, "waitMs", 0)));
            int pollMs = Math.Max(50, Math.Min(1000, ToolDispatcher.Int(args, "pollMs", 100)));
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                JsonNode response = await call("get_script_status", JsonUtil.Obj(("runId", runId)));
                JsonNode run = response?["data"]?["scriptRun"];
                if (response?["ok"]?.GetValue<bool>() == false || run == null
                    || run["terminal"]?.GetValue<bool>() == true || elapsed.ElapsedMilliseconds >= waitMs)
                    return response;
                int remaining = (int)Math.Max(0, waitMs - elapsed.ElapsedMilliseconds);
                if (remaining == 0) return response;
                await Task.Delay(Math.Min(pollMs, remaining));
            }
        }
    }
}
