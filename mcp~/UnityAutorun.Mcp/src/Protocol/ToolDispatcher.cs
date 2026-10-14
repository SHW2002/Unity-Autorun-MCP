using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace UnityAutorun.Mcp
{
    public sealed class ToolDispatcher
    {
        private readonly Func<string, JsonObject, Task<JsonNode>> _call;
        private readonly Func<Task<JsonNode>> _status;
        private readonly Func<JsonObject> _endpoint;

        public ToolDispatcher() : this(new BridgeClient()) { }
        private ToolDispatcher(BridgeClient bridge) : this(bridge.CallUnityAsync, bridge.GetStatusAsync, bridge.GetEndpointInfo) { }

        public ToolDispatcher(Func<string, JsonObject, Task<JsonNode>> call,
            Func<Task<JsonNode>> status = null, Func<JsonObject> endpoint = null)
        {
            _call = call ?? throw new ArgumentNullException(nameof(call));
            _status = status;
            _endpoint = endpoint;
        }

        public async Task<JsonNode> DispatchAsync(string name, JsonObject args)
        {
            args = args ?? new JsonObject();
            try
            {
                switch (name)
                {
                    case "get_unity_bridge_port": return _endpoint != null ? _endpoint() : await _call(name, args);
                    case "unity_status": return _status != null ? await _status() : await _call(name, args);
                    case "unity_play": return await _call("play", new JsonObject());
                    case "unity_stop": return await _call("stop", new JsonObject());
                    case "list_buttons": return await _call(name, JsonUtil.Obj(
                        ("framework", Text(args, "framework", "all")), ("query", Text(args, "query")),
                        ("exact", Bool(args, "exact", false)), ("limit", Int(args, "limit", 100)),
                        ("namesOnly", Bool(args, "namesOnly", false))));
                    case "is_ui_view_open": return await _call(name, JsonUtil.Obj(
                        ("targetViewId", Text(args, "targetViewId")), ("query", Text(args, "query"))));
                    case "get_ui_state": return await _call(name, UiStatePayload(args));
                    case "wait_for_ui_state": return await _call(name, UiStatePayload(args, true));
                    case "click_button": return await _call(name, JsonUtil.Obj(
                        ("name", Text(args, "name")), ("text", Text(args, "text")),
                        ("framework", Text(args, "framework", "ugui"))));
                    case "run_sequence": return await _call(name, JsonUtil.Obj(("actions", args["actions"]?.DeepClone() ?? new JsonArray())));
                    case "get_script_guidance": return ScriptGuidance.Get();
                    case "create_script": return ScriptFiles.Create(args);
                    case "list_scripts": return await _call(name, JsonUtil.Obj(
                        ("kind", Text(args, "kind", "all")), ("query", Text(args, "query"))));
                    case "get_script":
                        JsonNode script = await _call(name, JsonUtil.Obj(("scriptId", Required(args, "scriptId")),
                            ("includeSource", Bool(args, "includeSource", true))));
                        return ScriptFiles.IncludeSource(script, Bool(args, "includeSource", true));
                    case "refresh_scripts": return await _call(name, new JsonObject());
                    case "start_script": return await _call(name, ScriptRequests.Start(args));
                    case "get_script_status": return await ScriptRequests.GetStatusAsync(_call, args);
                    case "cancel_script": return await _call(name, JsonUtil.Obj(("runId", Required(args, "runId"))));
                    default: return Error("unknown_tool", "Unknown tool: " + name);
                }
            }
            catch (ArgumentException exception) { return Error("invalid_arguments", exception.Message); }
            catch (JsonException exception) { return Error("invalid_arguments", exception.Message); }
            catch (Exception exception) { return Error("tool_error", exception.Message); }
        }

        public static JsonObject UiStatePayload(JsonObject args, bool assertion = false)
        {
            JsonObject payload = JsonUtil.Obj(("query", Text(args, "query")), ("scope", Text(args, "scope")),
                ("exact", Bool(args, "exact", false)), ("types", args["types"]?.DeepClone() ?? new JsonArray()),
                ("limit", Int(args, "limit", 100)), ("includeInactive", Bool(args, "includeInactive", false)),
                ("includeSensitive", Bool(args, "includeSensitive", false)));
            if (assertion)
            {
                payload["property"] = Text(args, "property", "value");
                payload["comparison"] = Text(args, "comparison", "equals");
                payload["expected"] = Text(args, "expected");
                payload["timeoutMilliseconds"] = Int(args, "timeoutMs", 10000);
                payload["pollMilliseconds"] = Int(args, "pollMs", 100);
            }
            return payload;
        }

        public static string Required(JsonObject args, string key)
        {
            string value = Text(args, key);
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(key + " is required.");
            return value;
        }

        public static string Text(JsonObject args, string key, string fallback = null) { return args[key]?.GetValue<string>() ?? fallback; }
        public static int Int(JsonObject args, string key, int fallback) { return args[key]?.GetValue<int>() ?? fallback; }
        public static bool Bool(JsonObject args, string key, bool fallback) { return args[key]?.GetValue<bool>() ?? fallback; }
        public static JsonObject Error(string code, string message)
        {
            return JsonUtil.Obj(("ok", false), ("code", code), ("message", message), ("data", new JsonObject()));
        }
    }
}
