using System.Text.Json.Nodes;

namespace UnityAutorun.Mcp
{
    public static class McpTools
    {
        public static JsonArray All()
        {
            var tools = new JsonArray
            {
                Tool("get_unity_bridge_port", "Get the dynamically published Unity bridge endpoint for diagnostics."),
                Tool("unity_status", "Check Unity play and compilation state, including compiler messages."),
                Tool("unity_play", "Request Unity Editor to enter Play Mode."),
                Tool("unity_stop", "Explicitly stop Unity Play Mode. Script completion otherwise preserves the game state."),
                Tool("list_buttons", "List current Unity UI buttons with optional filtering and bounded output.", ("framework", Enum("ugui", "fairygui", "all")), ("query", "string"), ("exact", "boolean"), ("limit", "integer"), ("namesOnly", "boolean")),
                Tool("is_ui_view_open", "Check one target UI view without returning the complete runtime hierarchy.", ("targetViewId", "string"), ("query", "string")),
                Tool("get_ui_state", "Read runtime uGUI and optional TextMeshPro values with paths, visibility and interactability. Password fields are masked unless includeSensitive=true.", ("query", "string"), ("scope", "string"), ("exact", "boolean"), ("types", StringArray()), ("limit", "integer"), ("includeInactive", "boolean"), ("includeSensitive", "boolean")),
                Tool("wait_for_ui_state", "Wait until a filtered UI property satisfies a comparison; prefer observable assertions to fixed sleeps.", ("query", "string"), ("scope", "string"), ("exact", "boolean"), ("types", StringArray()), ("property", "string"), ("comparison", Enum("exists", "notExists", "equals", "notEquals", "contains", "notContains", "startsWith", "endsWith", "greaterThan", "greaterThanOrEqual", "lessThan", "lessThanOrEqual", "isEmpty", "isNotEmpty")), ("expected", "string"), ("timeoutMs", "integer"), ("pollMs", "integer"), ("limit", "integer"), ("includeInactive", "boolean"), ("includeSensitive", "boolean")),
                Tool("click_button", "Click a Unity UI button by name or text.", ("name", "string"), ("text", "string"), ("framework", Enum("ugui", "fairygui"))),
                Tool("run_sequence", "Run a sequence of AutoRun button actions.", ("actions", "array")),
                Tool("get_script_guidance", "Get the current C# script contract, generation template, built-in IDs, source path and strict reuse rules. Read before using gen-script or run-script."),
                Tool("list_scripts", "List compiled business and basic scripts, root preparePlayMode metadata, declared dependencies and validation errors.", ("kind", Enum("business", "basic", "all")), ("query", "string")),
                Tool("get_script", "Get compiled script metadata, including root preparePlayMode, and optionally its source. Inspect preconditions and the complete flow before reusing a script.", ("scriptId", "string"), ("includeSource", "boolean")),
                Tool("create_script", "Create a new host-project C# file under Assets/Editor/AutorunScripts. path is relative to that folder. Existing files are never overwritten. Does not execute the script.", ("path", "string"), ("source", "string")),
                Tool("refresh_scripts", "Refresh Unity assets and report compilation state/messages. Wait for compilation to finish, then list/get the compiled script before running."),
                Tool("start_script", "Start a tracked C# script and return immediately. parameters accepts a string-valued JSON object or its JSON string; parametersJson is an alternative. ensurePlayMode defaults to false; caller true or root script preparePlayMode=true prepares Play Mode before preconditions/actions. Dependency metadata does not request preparation. timeoutSeconds defaults to 120. Reuse runId for retries; conflicting runId is rejected.", ("scriptId", "string"), ("parameters", Parameters()), ("parametersJson", "string"), ("ensurePlayMode", "boolean"), ("timeoutSeconds", "number"), ("runId", "string")),
                Tool("get_script_status", "Get one script run including terminal/state, logs, call stack and artifacts. waitMs optionally polls up to 30000 ms; terminal failure keeps the full result.", ("runId", "string"), ("waitMs", "integer"), ("pollMs", "integer")),
                Tool("cancel_script", "Cancel a tracked script, unwind child scripts and release resources while preserving the game state.", ("runId", "string"))
            };
            Require(tools, "get_script", "scriptId");
            Require(tools, "create_script", "path", "source");
            Require(tools, "start_script", "scriptId");
            Require(tools, "get_script_status", "runId");
            Require(tools, "cancel_script", "runId");
            return tools;
        }

        private static JsonObject Tool(string name, string description, params (string Name, object Schema)[] properties)
        {
            var props = new JsonObject();
            foreach ((string propName, object schema) in properties)
                props[propName] = schema is JsonNode node ? node : JsonUtil.Obj(("type", schema.ToString()));
            return JsonUtil.Obj(("name", name), ("description", description),
                ("inputSchema", JsonUtil.Obj(("type", "object"), ("properties", props), ("additionalProperties", false))));
        }

        private static void Require(JsonArray tools, string name, params string[] fields)
        {
            foreach (JsonNode tool in tools)
                if (tool["name"].GetValue<string>() == name)
                    tool["inputSchema"]["required"] = Strings(fields);
        }

        private static JsonObject Enum(params string[] values)
        {
            return JsonUtil.Obj(("type", "string"), ("enum", Strings(values)));
        }

        private static JsonArray Strings(string[] values)
        {
            var array = new JsonArray();
            foreach (string value in values) array.Add(value);
            return array;
        }

        private static JsonObject Parameters()
        {
            return JsonUtil.Obj(("oneOf", new JsonArray
            {
                JsonUtil.Obj(("type", "object"), ("additionalProperties", JsonUtil.Obj(("type", "string")))),
                JsonUtil.Obj(("type", "string"))
            }));
        }

        private static JsonObject StringArray()
        {
            return JsonUtil.Obj(("type", "array"), ("items", JsonUtil.Obj(("type", "string"))));
        }
    }
}
