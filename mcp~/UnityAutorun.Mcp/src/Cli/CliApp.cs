using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace UnityAutorun.Mcp
{
    public static class CliApp
    {
        public static async Task<int> RunAsync(string[] args)
        {
            if (args.Length == 0 || args[0] == "help" || args[0] == "--help" || args[0] == "-h")
            {
                PrintUsage();
                return 0;
            }
            string tool;
            var payload = new JsonObject();
            switch (args[0])
            {
                case "bridge-port": tool = "get_unity_bridge_port"; break;
                case "status": tool = "unity_status"; break;
                case "play": tool = "unity_play"; break;
                case "stop": tool = "unity_stop"; break;
                case "list-buttons":
                    tool = "list_buttons";
                    payload = JsonUtil.Obj(("framework", Read(args, "--framework", "all")), ("query", Read(args, "--query")),
                        ("exact", ReadBool(args, "--exact", false)), ("limit", ReadInt(args, "--limit", 100)),
                        ("namesOnly", ReadBool(args, "--names-only", false)));
                    break;
                case "is-view-open":
                    tool = "is_ui_view_open";
                    payload = JsonUtil.Obj(("targetViewId", Read(args, "--target")), ("query", Read(args, "--query")));
                    break;
                case "ui-state": tool = "get_ui_state"; payload = UiStateArguments(args); break;
                case "wait-ui-state":
                    tool = "wait_for_ui_state";
                    payload = UiStateArguments(args);
                    payload["property"] = Read(args, "--property", "value");
                    payload["comparison"] = Read(args, "--comparison", "equals");
                    payload["expected"] = Read(args, "--expected");
                    payload["timeoutMs"] = ReadInt(args, "--timeout-ms", 10000);
                    payload["pollMs"] = ReadInt(args, "--poll-ms", 100);
                    break;
                case "click":
                    tool = "click_button";
                    payload = JsonUtil.Obj(("name", Read(args, "--name")), ("text", Read(args, "--text")),
                        ("framework", Read(args, "--framework", "ugui")));
                    break;
                case "run-sequence":
                    tool = "run_sequence";
                    payload["actions"] = JsonNode.Parse(ReadJson(args, "--json", "--json-file", "[]"));
                    break;
                case "script-guidance": tool = "get_script_guidance"; break;
                case "list-scripts":
                    tool = "list_scripts";
                    payload = JsonUtil.Obj(("kind", Read(args, "--kind", "all")), ("query", Read(args, "--query")));
                    break;
                case "get-script":
                    tool = "get_script";
                    payload = JsonUtil.Obj(("scriptId", Read(args, "--script")), ("includeSource", ReadBool(args, "--include-source", true)));
                    break;
                case "create-script":
                    tool = "create_script";
                    payload = JsonUtil.Obj(("path", Read(args, "--path")),
                        ("source", File.ReadAllText(Read(args, "--source-file") ?? throw new ArgumentException("--source-file is required."))));
                    break;
                case "refresh-scripts": tool = "refresh_scripts"; break;
                case "start-script":
                    tool = "start_script";
                    payload = JsonUtil.Obj(("scriptId", Read(args, "--script")),
                        ("parametersJson", ReadJson(args, "--parameters-json", "--parameters-file", "{}")),
                        ("ensurePlayMode", ReadBool(args, "--ensure-play-mode", false)),
                        ("timeoutSeconds", ReadNumber(args, "--timeout-seconds", 120)), ("runId", Read(args, "--run")));
                    break;
                case "script-status":
                    tool = "get_script_status";
                    payload = JsonUtil.Obj(("runId", Read(args, "--run")), ("waitMs", ReadInt(args, "--wait-ms", 0)),
                        ("pollMs", ReadInt(args, "--poll-ms", 100)));
                    break;
                case "cancel-script": tool = "cancel_script"; payload["runId"] = Read(args, "--run"); break;
                default:
                    Console.Error.WriteLine("Unknown command: " + args[0]);
                    PrintUsage();
                    return 2;
            }
            JsonNode result = await new ToolDispatcher().DispatchAsync(tool, payload);
            Console.WriteLine(JsonUtil.Pretty(result));
            return result?["ok"]?.GetValue<bool>() == false ? 1 : 0;
        }

        private static JsonObject UiStateArguments(string[] args)
        {
            return JsonUtil.Obj(("query", Read(args, "--query")), ("scope", Read(args, "--scope")),
                ("exact", ReadBool(args, "--exact", false)), ("types", ReadMany(args, "--type")),
                ("limit", ReadInt(args, "--limit", 100)), ("includeInactive", ReadBool(args, "--include-inactive", false)),
                ("includeSensitive", ReadBool(args, "--include-sensitive", false)));
        }
        private static string Read(string[] args, string option, string fallback = null)
        {
            int index = Array.IndexOf(args, option);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }
        private static int ReadInt(string[] args, string option, int fallback)
        {
            string text = Read(args, option);
            return text == null ? fallback : int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        }
        private static double ReadNumber(string[] args, string option, double fallback)
        {
            string text = Read(args, option);
            return text == null ? fallback : double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        }
        private static bool ReadBool(string[] args, string option, bool fallback)
        {
            int index = Array.IndexOf(args, option);
            if (index < 0) return fallback;
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal)) return true;
            return bool.Parse(args[index + 1]);
        }
        private static JsonArray ReadMany(string[] args, string option)
        {
            var result = new JsonArray();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == option && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) result.Add(args[++i]);
            return result;
        }
        private static string ReadJson(string[] args, string inlineOption, string fileOption, string fallback)
        {
            string file = Read(args, fileOption);
            return file == null ? Read(args, inlineOption, fallback) : File.ReadAllText(file);
        }
        private static void PrintUsage()
        {
            Console.WriteLine("Unity AutoRun MCP\n\n"
                + "Usage: dotnet run --project mcp~/UnityAutorun.Mcp -- <command>\n"
                + "  mcp | mock-bridge | bridge-port | status | play | stop\n"
                + "  list-buttons [--framework ugui|fairygui|all] [--query Name] [--exact] [--limit 100] [--names-only]\n"
                + "  is-view-open --target TargetView [--query Name]\n"
                + "  ui-state [--query NameOrValue] [--scope ViewPath] [--type Toggle] [--limit 100]\n"
                + "  wait-ui-state --query Path [--property value] [--comparison equals] [--expected Value] [--timeout-ms 10000]\n"
                + "  click [--name Name] [--text Text] [--framework ugui|fairygui]\n"
                + "  run-sequence --json-file actions.json\n"
                + "  script-guidance\n"
                + "  list-scripts [--kind business|basic|all] [--query Text]\n"
                + "  get-script --script ScriptId [--include-source true|false]\n"
                + "  create-script --path Business/Flow.cs --source-file source.cs\n"
                + "  refresh-scripts\n"
                + "  start-script --script ScriptId [--parameters-json JSON|--parameters-file File] [--ensure-play-mode] [--timeout-seconds 120] [--run RunId]\n"
                + "  script-status --run RunId [--wait-ms 30000] [--poll-ms 100]\n"
                + "  cancel-script --run RunId");
        }
    }
}
