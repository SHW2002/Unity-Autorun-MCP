using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using UnityAutorun.Mcp;

namespace UnityAutorun.Mcp.Tests
{
    internal static class Program
    {
        private static readonly List<(string Name, Action Test)> Tests = new List<(string Name, Action Test)>
        {
            ("MCP registers script tools and removes map tools", TestToolRegistration),
            ("script creation never replaces existing bytes", TestCreationOnlyScript),
            ("script creation rejects traversal and non-C# paths", TestScriptPathBoundary),
            ("project roots resolve from nested client workspaces", TestProjectRootResolution),
            ("default bridge endpoint path remains compatible", TestDefaultBridgeEndpoint),
            ("bridge profiles isolate publishing lookup and cleanup", TestIsolatedBridgeEndpoint),
            ("invalid bridge profiles fail without touching endpoints", TestInvalidBridgeProfile),
            ("script source reads stay inside generated directory", TestSourceBoundary),
            ("start_script normalizes parameters and preserves retry IDs", TestScriptRequests),
            ("MCP preserves root preparation metadata without a new start argument", TestScriptPreparationMetadata),
            ("status polling preserves terminal failure detail", TestStatusPolling),
            ("MCP framed UTF-8 and JSONL protocol share the contract", TestProtocol),
            ("script guidance describes strict flows and current APIs", TestGuidance),
            ("CLI help exposes script commands without navigation maps", TestCliHelp),
            ("Codex MCP installs with a portable project root", TestCodexMcpInstall),
            ("Claude Code MCP installs at the project root", TestClaudeCodeMcpInstall),
            ("Claude Desktop MCP preserves existing servers", TestClaudeDesktopMcpInstall)
        };

        private static int Main()
        {
            Tests.AddRange(ScriptRuntimeTests.Cases());
            Tests.AddRange(ScriptCatalogTests.Cases());
            Tests.AddRange(AgentInstructionsInstallTests.Cases());
            Tests.AddRange(SkillInstallTests.Cases());
            Tests.AddRange(BridgeLifecycleTests.Cases());
            int failed = 0;
            foreach ((string name, Action test) in Tests)
            {
                try { test(); Console.WriteLine("PASS " + name); }
                catch (Exception exception) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + exception); }
            }
            Console.WriteLine((Tests.Count - failed) + "/" + Tests.Count + " tests passed.");
            return failed == 0 ? 0 : 1;
        }

        private static void TestToolRegistration()
        {
            JsonArray tools = McpTools.All();
            string[] names = tools.Select(tool => tool["name"].GetValue<string>()).ToArray();
            foreach (string name in new[] { "get_script_guidance", "list_scripts", "get_script", "create_script", "refresh_scripts", "start_script", "get_script_status", "cancel_script", "unity_status", "unity_play", "unity_stop", "get_ui_state", "wait_for_ui_state" })
                AssertTrue(names.Contains(name), "Missing tool: " + name);
            AssertEqual(names.Length, names.Distinct().Count(), "Duplicate tools were registered.");
            AssertFalse(names.Any(name => name.Contains("nav") || name.Contains("route")), "Legacy navigation tools remain registered.");
            JsonNode creation = tools.First(tool => Text(tool.AsObject(), "name") == "create_script")["inputSchema"];
            AssertEqual("path,source", string.Join(",", creation["required"].AsArray().Select(item => item.GetValue<string>())), "Creation schema omits required fields.");
            AssertFalse(creation["additionalProperties"].GetValue<bool>(), "Creation schema accepts undeclared options.");
        }

        private static void TestCreationOnlyScript()
        {
            WithUnityProject(root =>
            {
                const string source = "// 中文业务脚本\npublic class NewScript {}\n";
                JsonObject created = ScriptFiles.Create(JsonUtil.Obj(("path", "Business/NewScript.cs"), ("source", source)));
                AssertTrue(Boolean(created, "ok"), "Initial script was not created.");
                string path = Path.Combine(root, ProjectPaths.GeneratedScriptsRelativePath, "Business", "NewScript.cs");
                AssertEqual(source, File.ReadAllText(path), "Source bytes changed during creation.");
                AssertEqual("Assets/Editor/AutorunScripts/Business/NewScript.cs", Text(created["data"].AsObject(), "sourcePath"), "Returned path is not project-relative.");
                AssertTrue(Boolean(created["data"].AsObject(), "requiresRefresh"), "Creation did not report pending compilation.");
                AssertFalse(Directory.Exists(Path.Combine(root, ProjectPaths.LegacyGeneratedScriptsRelativePath)), "Creation still populated the legacy script directory.");
                byte[] original = File.ReadAllBytes(path);
                JsonObject rejected = ScriptFiles.Create(JsonUtil.Obj(("path", "Business/NewScript.cs"), ("source", "replacement")));
                AssertFalse(Boolean(rejected, "ok"), "Creation overwrote an existing script.");
                AssertEqual("script_exists", Text(rejected, "code"), "Unexpected duplicate error.");
                AssertTrue(original.SequenceEqual(File.ReadAllBytes(path)), "Rejected creation modified the file.");
            });
        }

        private static void TestScriptPathBoundary()
        {
            WithUnityProject(root =>
            {
                foreach (string path in new[] { "../Escape.cs", "Business/../../Escape.cs", "..\\Escape.cs", "Assets/../../../Escape.cs", "Other.cs/Bad.cs", "test.json", "test.cs.meta", "test.cs:stream", "test.cs.", "C:\\outside.cs", "/outside.cs", "Business//Flow.cs", "./Flow.cs", "Business /Flow.cs" })
                {
                    // A directory with a .cs suffix is allowed but cannot escape the root.
                    if (path == "Other.cs/Bad.cs") continue;
                    JsonObject result = ScriptFiles.Create(JsonUtil.Obj(("path", path), ("source", "unsafe")));
                    AssertFalse(Boolean(result, "ok"), "Unsafe path was accepted: " + path);
                    AssertEqual("invalid_script_path", Text(result, "code"), "Unexpected path error: " + path);
                }
                AssertFalse(File.Exists(Path.Combine(root, "Escape.cs")), "Path traversal wrote outside script directory.");
                AssertFalse(Directory.Exists(Path.Combine(root, ProjectPaths.GeneratedScriptsRelativePath)), "Invalid path requests created script directories.");
            });
        }

        private static void TestProjectRootResolution()
        {
            WithUnityProject(root =>
            {
                string originalDirectory = Environment.CurrentDirectory;
                string originalConfiguredRoot = Environment.GetEnvironmentVariable("UNITY_AUTORUN_PROJECT_ROOT");
                try
                {
                    string workspace = Path.Combine(root, "Tools", "Client", "Nested");
                    Directory.CreateDirectory(workspace);
                    Environment.CurrentDirectory = workspace;
                    Environment.SetEnvironmentVariable("UNITY_AUTORUN_PROJECT_ROOT", ".");
                    AssertEqual(Path.GetFullPath(root), ProjectPaths.ResolveProjectRootDirectory(), "Relative root was not resolved against ancestor workspaces.");
                }
                finally
                {
                    Environment.CurrentDirectory = originalDirectory;
                    Environment.SetEnvironmentVariable("UNITY_AUTORUN_PROJECT_ROOT", originalConfiguredRoot);
                }
            });
        }

        private static void TestSourceBoundary()
        {
            WithUnityProject(root =>
            {
                const string source = "// correct source";
                ScriptFiles.Create(JsonUtil.Obj(("path", "Flow.cs"), ("source", source)));
                Func<string, JsonNode> response = path => JsonUtil.Obj(("ok", true), ("data", JsonUtil.Obj(("script", JsonUtil.Obj(("id", "flow"), ("sourcePath", path))))));
                JsonNode loaded = ScriptFiles.IncludeSource(response("Assets/Editor/AutorunScripts/Flow.cs"), true);
                AssertEqual(source, Text(loaded["data"]["script"].AsObject(), "source"), "Generated source was not included.");
                ScriptFiles.IncludeSource(loaded, false);
                AssertFalse(loaded["data"]["script"].AsObject().ContainsKey("source"), "includeSource=false still returned source.");
                string legacyPath = Path.Combine(root, ProjectPaths.LegacyGeneratedScriptsRelativePath, "LegacyFlow.cs");
                Directory.CreateDirectory(Path.GetDirectoryName(legacyPath));
                File.WriteAllText(legacyPath, "// preserved legacy source");
                JsonNode legacy = ScriptFiles.IncludeSource(response("Assets/AutorunScripts/Editor/LegacyFlow.cs"), true);
                AssertEqual("// preserved legacy source", Text(legacy["data"]["script"].AsObject(), "source"), "Legacy source could no longer be read.");
                JsonNode absoluteLegacy = ScriptFiles.IncludeSource(response(legacyPath), true);
                AssertEqual("// preserved legacy source", Text(absoluteLegacy["data"]["script"].AsObject(), "source"), "Absolute legacy source path was rejected.");
                JsonNode traversal = ScriptFiles.IncludeSource(response("Assets/Editor/AutorunScripts/../AutorunScripts/Flow.cs"), true);
                AssertFalse(traversal["data"]["script"].AsObject().ContainsKey("source"), "Traversing source path was read.");
                AssertTrue(traversal["data"]["script"].AsObject().ContainsKey("sourceError"), "Traversing source path was not diagnosed.");
                string outside = Path.Combine(root, "ProjectSettings", "secret.cs");
                File.WriteAllText(outside, "must-not-read");
                JsonNode rejected = ScriptFiles.IncludeSource(response(outside), true);
                AssertFalse(rejected["data"]["script"].AsObject().ContainsKey("source"), "Source reading escaped the script directory.");
                AssertTrue(rejected["data"]["script"].AsObject().ContainsKey("sourceError"), "Rejected source path was not diagnosed.");
                string prefixSibling = Path.Combine(root, "Assets", "Editor", "AutorunScriptsOutside", "secret.cs");
                Directory.CreateDirectory(Path.GetDirectoryName(prefixSibling));
                File.WriteAllText(prefixSibling, "must-not-read-prefix-sibling");
                JsonNode sibling = ScriptFiles.IncludeSource(response(prefixSibling), true);
                AssertFalse(sibling["data"]["script"].AsObject().ContainsKey("source"), "Source read accepted a sibling with a matching directory prefix.");
            });
        }

        private static void TestDefaultBridgeEndpoint()
        {
            WithUnityProject(root => WithBridgeProfile(null, () =>
            {
                string expected = Path.Combine(root, "Library", "UnityAutorunTool", "bridge-endpoint.json");
                BridgeEndpointProvider.PublishForCurrentProcess("127.0.0.1", 17331);
                BridgeEndpoint endpoint = new BridgeEndpointProvider().GetRequired();
                AssertEqual(expected, endpoint.StateFilePath, "Default endpoint path changed.");
                AssertEqual(17331, endpoint.Port, "Default endpoint could not be resolved.");
                WithBridgeProfile("", () => AssertEqual(expected, new BridgeEndpointProvider().GetRequired().StateFilePath,
                    "Empty profile did not preserve the default endpoint path."));
                BridgeEndpointProvider.ClearForCurrentProcess();
                AssertFalse(File.Exists(expected), "Default endpoint was not cleaned up.");
            }));
        }

        private static void TestIsolatedBridgeEndpoint()
        {
            WithUnityProject(root => WithBridgeProfile(null, () =>
            {
                string defaultPath = Path.Combine(root, "Library", "UnityAutorunTool", "bridge-endpoint.json");
                BridgeEndpointProvider.PublishForCurrentProcess("127.0.0.1", 17331);
                byte[] original = File.ReadAllBytes(defaultPath);
                string isolatedPath = Path.Combine(root, "Library", "UnityAutorunTool", "profiles", "scriptable-navigation-case", "bridge-endpoint.json");
                WithBridgeProfile("scriptable-navigation-case", () =>
                {
                    BridgeEndpointProvider.PublishForCurrentProcess("127.0.0.1", 17332);
                    BridgeEndpoint endpoint = new BridgeEndpointProvider().GetRequired();
                    AssertEqual(isolatedPath, endpoint.StateFilePath, "Profile endpoint path is incorrect.");
                    AssertEqual(17332, endpoint.Port, "Profile lookup returned a different bridge.");
                    AssertTrue(original.SequenceEqual(File.ReadAllBytes(defaultPath)), "Profile publish changed the default endpoint.");
                    JsonObject info = new BridgeEndpointProvider().GetInfo();
                    AssertEqual(isolatedPath, Text(info["data"].AsObject(), "stateFilePath"), "Diagnostics do not identify the selected endpoint file.");
                    BridgeEndpointProvider.ClearForCurrentProcess();
                    AssertFalse(File.Exists(isolatedPath), "Profile cleanup did not remove its own endpoint.");
                    AssertTrue(original.SequenceEqual(File.ReadAllBytes(defaultPath)), "Profile cleanup changed the default endpoint.");
                });
                AssertEqual(17331, new BridgeEndpointProvider().GetRequired().Port, "Default lookup did not retain the existing bridge.");
                WithBridgeProfile("unpublished-profile", () => AssertFalse(Boolean(new BridgeEndpointProvider().GetInfo(), "ok"),
                    "Missing profile silently fell back to the default bridge."));
                AssertTrue(original.SequenceEqual(File.ReadAllBytes(defaultPath)), "Failed profile lookup changed the default endpoint.");
                BridgeEndpointProvider.ClearForCurrentProcess();
            }));
        }

        private static void TestInvalidBridgeProfile()
        {
            WithUnityProject(root => WithBridgeProfile(null, () =>
            {
                string defaultPath = Path.Combine(root, "Library", "UnityAutorunTool", "bridge-endpoint.json");
                BridgeEndpointProvider.PublishForCurrentProcess("127.0.0.1", 17331);
                byte[] original = File.ReadAllBytes(defaultPath);
                foreach (string profile in new[] { "../default", "nested/profile", "nested\\profile", ".", "..", " ", "a b", "a.", ":", "中文", new string('a', 65) })
                {
                    WithBridgeProfile(profile, () =>
                    {
                        bool rejected = false;
                        try { BridgeEndpointProvider.PublishForCurrentProcess("127.0.0.1", 17332); }
                        catch (ArgumentException) { rejected = true; }
                        AssertTrue(rejected, "Invalid bridge profile was accepted: " + profile);
                        AssertFalse(Boolean(new BridgeEndpointProvider().GetInfo(), "ok"), "Invalid profile resolved an endpoint: " + profile);
                        BridgeEndpointProvider.ClearForCurrentProcess();
                        AssertTrue(original.SequenceEqual(File.ReadAllBytes(defaultPath)), "Invalid profile changed the default endpoint: " + profile);
                    });
                }
                AssertFalse(Directory.Exists(Path.Combine(root, "Library", "UnityAutorunTool", "profiles")), "Invalid profiles created state directories.");
                BridgeEndpointProvider.ClearForCurrentProcess();
            }));
        }

        private static void TestScriptRequests()
        {
            int calls = 0;
            string receivedCommand = null;
            JsonObject received = null;
            var dispatcher = new ToolDispatcher((command, payload) =>
            {
                calls++;
                receivedCommand = command;
                received = payload;
                return Task.FromResult<JsonNode>(JsonUtil.Obj(("ok", true)));
            });
            JsonNode result = dispatcher.DispatchAsync("start_script", JsonUtil.Obj(("scriptId", "business.flow"),
                ("parameters", JsonUtil.Obj(("name", "中文按钮"), ("count", "3"))), ("runId", "retry-id"),
                ("ensurePlayMode", true), ("timeoutSeconds", 45.5))).GetAwaiter().GetResult();
            AssertTrue(Boolean(result.AsObject(), "ok"), "Valid start was rejected.");
            AssertEqual("start_script", receivedCommand, "Wrong bridge command.");
            AssertEqual("retry-id", Text(received, "runId"), "Explicit retry ID changed.");
            AssertEqual("中文按钮", Text(ParseObject(Text(received, "parametersJson")), "name"), "Unicode parameter changed.");
            AssertTrue(Boolean(received, "ensurePlayMode"), "Explicit Play Mode option was lost.");
            AssertEqual(45.5, received["timeoutSeconds"].GetValue<double>(), "Timeout changed.");
            AssertFalse(received.ContainsKey("parameters"), "Bridge receives the wrong parameter representation.");
            JsonObject defaults = ScriptRequests.Start(JsonUtil.Obj(("scriptId", "business.flow"), ("parameters", "{\"count\":\"4\"}")));
            AssertFalse(Boolean(defaults, "ensurePlayMode"), "Play Mode was implicitly requested.");
            AssertTrue(!string.IsNullOrEmpty(Text(defaults, "runId")), "Generated retry ID is missing.");
            AssertEqual("4", Text(ParseObject(Text(defaults, "parametersJson")), "count"), "JSON string parameters were not normalized.");
            int beforeRejected = calls;
            foreach (JsonObject invalid in new[]
            {
                JsonUtil.Obj(("scriptId", "flow"), ("parameters", JsonUtil.Obj(("numeric", 3)))),
                JsonUtil.Obj(("scriptId", "flow"), ("parametersJson", "null")),
                JsonUtil.Obj(("scriptId", "flow"), ("parametersJson", "[]")),
                JsonUtil.Obj(("scriptId", "flow"), ("parametersJson", "bad-json")),
                JsonUtil.Obj(("scriptId", "flow"), ("parametersJson", "{}"), ("parameters", new JsonObject())),
                JsonUtil.Obj(("scriptId", "flow"), ("timeoutSeconds", 0)),
                new JsonObject()
            })
            {
                JsonNode failed = dispatcher.DispatchAsync("start_script", invalid).GetAwaiter().GetResult();
                AssertFalse(Boolean(failed.AsObject(), "ok"), "Invalid script start was accepted.");
            }
            AssertEqual(beforeRejected, calls, "Invalid start reached the bridge.");
        }

        private static void TestStatusPolling()
        {
            int calls = 0;
            var dispatcher = new ToolDispatcher((command, payload) =>
            {
                AssertEqual("get_script_status", command, "Polling used the wrong command.");
                AssertEqual(1, payload.Count, "Polling options were sent to Unity.");
                AssertEqual("run-test", Text(payload, "runId"), "Polling changed the run ID.");
                calls++;
                bool terminal = calls > 1;
                return Task.FromResult<JsonNode>(JsonUtil.Obj(("ok", !terminal), ("code", terminal ? "precondition_failed" : "ok"),
                    ("data", JsonUtil.Obj(("scriptRun", JsonUtil.Obj(("runId", "run-test"), ("terminal", terminal),
                        ("state", terminal ? "failed" : "running"), ("logs", new JsonArray { "child completed", "wrong start" }),
                        ("artifacts", new JsonArray { "artifact.png" })))))));
            });
            JsonNode result = dispatcher.DispatchAsync("get_script_status", JsonUtil.Obj(("runId", "run-test"), ("waitMs", 500), ("pollMs", 50))).GetAwaiter().GetResult();
            AssertEqual(2, calls, "Status did not poll until terminal.");
            AssertFalse(Boolean(result.AsObject(), "ok"), "Terminal failure was turned into success.");
            AssertEqual("failed", Text(result["data"]["scriptRun"].AsObject(), "state"), "Terminal result was lost.");
            AssertEqual(2, result["data"]["scriptRun"]["logs"].AsArray().Count, "Failure logs were lost.");
            AssertEqual(1, result["data"]["scriptRun"]["artifacts"].AsArray().Count, "Failure artifacts were lost.");
        }

        private static void TestScriptPreparationMetadata()
        {
            JsonObject metadata = JsonUtil.Obj(("id", "business.cold-start"), ("preparePlayMode", true),
                ("dependencies", new JsonArray { "start-play-mode" }));
            JsonObject receivedStart = null;
            var dispatcher = new ToolDispatcher((command, payload) =>
            {
                if (command == "get_script")
                    return Task.FromResult<JsonNode>(JsonUtil.Obj(("ok", true), ("data", JsonUtil.Obj(("script", metadata.DeepClone())))));
                if (command == "list_scripts")
                    return Task.FromResult<JsonNode>(JsonUtil.Obj(("ok", true), ("data", JsonUtil.Obj(("scripts", new JsonArray
                    {
                        metadata.DeepClone(), JsonUtil.Obj(("id", "business.already-running"), ("preparePlayMode", false))
                    })))));
                receivedStart = payload;
                return Task.FromResult<JsonNode>(JsonUtil.Obj(("ok", true)));
            });
            JsonNode script = dispatcher.DispatchAsync("get_script", JsonUtil.Obj(("scriptId", "business.cold-start"),
                ("includeSource", false))).GetAwaiter().GetResult();
            AssertTrue(Boolean(script["data"]["script"].AsObject(), "preparePlayMode"), "get_script dropped compiled root preparation metadata.");
            JsonNode list = dispatcher.DispatchAsync("list_scripts", new JsonObject()).GetAwaiter().GetResult();
            AssertTrue(Boolean(list["data"]["scripts"][0].AsObject(), "preparePlayMode"), "list_scripts dropped true root metadata.");
            AssertFalse(Boolean(list["data"]["scripts"][1].AsObject(), "preparePlayMode"), "list_scripts changed false root metadata.");
            dispatcher.DispatchAsync("start_script", JsonUtil.Obj(("scriptId", "business.cold-start"))).GetAwaiter().GetResult();
            AssertFalse(Boolean(receivedStart, "ensurePlayMode"), "MCP replaced the original caller preparation option.");
            AssertFalse(receivedStart.ContainsKey("preparePlayMode"), "Compiled metadata was incorrectly added as a start request argument.");
            JsonObject schema = McpTools.All().First(tool => Text(tool.AsObject(), "name") == "start_script")["inputSchema"].AsObject();
            AssertFalse(schema["properties"].AsObject().ContainsKey("preparePlayMode"), "Start schema exposes compiled metadata as a caller setting.");
        }

        private static void TestProtocol()
        {
            string initialize = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"}\n";
            string call = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"get_script\",\"arguments\":{\"scriptId\":\"业务.目标\",\"includeSource\":false}}}";
            string list = "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/list\"}\n";
            string input = initialize + "Content-Length: " + Encoding.UTF8.GetByteCount(call) + "\r\n\r\n" + call + "\n" + "broken-json\n" + list;
            var dispatcher = new ToolDispatcher((command, payload) =>
            {
                AssertEqual("业务.目标", Text(payload, "scriptId"), "Framed UTF-8 script ID changed.");
                return Task.FromResult<JsonNode>(JsonUtil.Obj(("ok", false), ("code", "script_not_found"), ("data", new JsonObject())));
            });
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(input)))
            using (var output = new StringWriter())
            {
                new McpServer(dispatcher).RunAsync(stream, output).GetAwaiter().GetResult();
                JsonObject[] messages = output.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(ParseObject).ToArray();
                AssertEqual(4, messages.Length, "Framing or malformed-message recovery lost a response.");
                AssertEqual(McpVersion.Current, Text(messages[0]["result"]["serverInfo"].AsObject(), "version"), "MCP version is not sourced from the assembly.");
                AssertTrue(Boolean(messages[1]["result"].AsObject(), "isError"), "Tool failure was not marked as an MCP error.");
                AssertEqual("script_not_found", Text(ParseObject(messages[1]["result"]["content"][0]["text"].GetValue<string>()), "code"), "Tool failure detail was lost.");
                AssertEqual(-32700, messages[2]["error"]["code"].GetValue<int>(), "Malformed JSON used the wrong error code.");
                AssertTrue(messages[3]["result"]["tools"].AsArray().Count > 0, "Protocol did not recover after malformed JSON.");
            }
        }

        private static void TestGuidance()
        {
            WithUnityProject(root =>
            {
                JsonObject data = ScriptGuidance.Get()["data"].AsObject();
                AssertEqual(ProjectPaths.GeneratedScriptsRelativePath, Text(data, "scriptDirectory"), "Guidance uses the wrong host directory.");
                AssertEqual(ProjectPaths.LegacyGeneratedScriptsRelativePath, Text(data, "legacyScriptDirectory"), "Guidance omits the compatible legacy source directory.");
                AssertEqual("gen-script,edit-script,run-script", string.Join(",", data["skills"].AsArray().Select(value => value.GetValue<string>())), "Guidance has obsolete skill names.");
                string contract = data.ToJsonString();
                AssertTrue(contract.Contains("CheckPreconditions") && contract.Contains("Never edit reused scripts"), "Strict business-flow contract is missing.");
                AssertTrue(ScriptGuidance.Template.Contains("AutoRunScriptKind.Business") && ScriptGuidance.Template.Contains("context.Call"), "Template omits runtime composition.");
                AssertFalse(contract.Contains("nav_map") || contract.Contains("start_ui_navigation"), "Guidance still requires old navigation tools.");
            });
        }

        private static void TestCliHelp()
        {
            TextWriter original = Console.Out;
            using (var output = new StringWriter())
            {
                try
                {
                    Console.SetOut(output);
                    AssertEqual(0, CliApp.RunAsync(new[] { "help" }).GetAwaiter().GetResult(), "CLI help failed.");
                    string help = output.ToString();
                    AssertTrue(help.Contains("start-script") && help.Contains("create-script"), "Script commands are missing from CLI help.");
                    AssertFalse(help.Contains("nav-map") || help.Contains("run-route"), "Legacy map CLI commands remain.");
                }
                finally { Console.SetOut(original); }
            }
        }

        private static void TestCodexMcpInstall()
        {
            WithTemporaryDirectory(root =>
            {
                string repositoryRoot = Path.Combine(root, "Repository");
                string codexFolder = Path.Combine(repositoryRoot, ".codex");
                string unityProjectRoot = Path.Combine(repositoryRoot, "Unity");
                Directory.CreateDirectory(codexFolder);
                Directory.CreateDirectory(unityProjectRoot);
                string configPath = Path.Combine(codexFolder, "config.toml");
                File.WriteAllText(configPath, "model = \"existing\"\n");
                string publishedDll = CreatePublishedDll(root);

                WithMcpInstallConfig(publishedDll, () =>
                {
                    bool installed = McpInstallService.Install(
                        codexFolder,
                        out string message);
                    AssertTrue(installed, "Codex MCP installation failed: " + message);
                    string installedConfig = File.ReadAllText(configPath);
                    AssertTrue(
                        installedConfig.Contains("model = \"existing\""),
                        "Codex installation removed existing config.");
                    AssertTrue(
                        installedConfig.Contains("UNITY_AUTORUN_PROJECT_ROOT = \"Unity\""),
                        "Codex installation did not use a portable Unity project root.");
                    AssertFalse(
                        installedConfig.Contains("cwd ="),
                        "Codex installation hard-coded an MCP working directory.");
                    AssertFalse(
                        installedConfig.Contains("UNITY_AUTORUN_TOOL_ROOT"),
                        "Codex installation wrote a redundant absolute tool root.");

                    string firstInstall = installedConfig;
                    installed = McpInstallService.Install(codexFolder, out message);
                    AssertTrue(installed, "Repeated Codex MCP installation failed: " + message);
                    AssertEqual(
                        firstInstall,
                        File.ReadAllText(configPath),
                        "Repeated Codex installation changed an already-current config.");
                }, unityProjectRoot);
            });
        }

        private static void TestClaudeCodeMcpInstall()
        {
            WithTemporaryDirectory(root =>
            {
                string projectRoot = Path.Combine(root, "Project");
                string claudeFolder = Path.Combine(projectRoot, ".claude");
                Directory.CreateDirectory(claudeFolder);
                string publishedDll = CreatePublishedDll(root);
                string projectConfig = Path.Combine(projectRoot, ".mcp.json");
                File.WriteAllText(
                    projectConfig,
                    "{\n  \"mcpServers\": {\n    \"existing\": {\"command\":\"existing\"}\n  }\n}\n");
                string legacyWrongConfig = Path.Combine(claudeFolder, ".mcp.json");
                File.WriteAllText(legacyWrongConfig, "legacy-location-must-not-change");

                string unityProjectRoot = Path.Combine(projectRoot, "Unity");
                Directory.CreateDirectory(unityProjectRoot);
                WithMcpInstallConfig(publishedDll, () =>
                {
                    bool installed = McpInstallService.Install(
                        claudeFolder,
                        out string message);
                    AssertTrue(installed, "Claude Code MCP installation failed: " + message);
                    AssertTrue(
                        message.Contains("Claude Code"),
                        "Claude Code install message did not identify the client.");
                    AssertTrue(
                        message.Contains("legacy config"),
                        "Claude Code install message did not report the legacy config.");
                    AssertClaudeServerAndExistingEntry(projectConfig);
                    JsonObject installedServer = ParseObject(
                        File.ReadAllText(projectConfig))["mcpServers"]?[McpInstallConfig.ServerName]?.AsObject();
                    AssertEqual(
                        "Unity",
                        Text(installedServer?["env"]?.AsObject(), "UNITY_AUTORUN_PROJECT_ROOT"),
                        "Claude Code installation did not use a portable Unity project root.");
                    AssertFalse(
                        installedServer?["env"]?.AsObject().ContainsKey("UNITY_AUTORUN_TOOL_ROOT") == true,
                        "Claude Code installation wrote a redundant absolute tool root.");
                    AssertEqual(
                        "legacy-location-must-not-change",
                        File.ReadAllText(legacyWrongConfig),
                        "Installer rewrote the legacy .claude/.mcp.json location.");

                    string firstInstall = File.ReadAllText(projectConfig);
                    installed = McpInstallService.Install(claudeFolder, out message);
                    AssertTrue(installed, "Repeated Claude Code MCP installation failed: " + message);
                    AssertEqual(
                        firstInstall,
                        File.ReadAllText(projectConfig),
                        "Repeated Claude Code installation changed an already-current config.");
                }, unityProjectRoot);
            });
        }

        private static void TestClaudeDesktopMcpInstall()
        {
            WithTemporaryDirectory(root =>
            {
                string configFolder = Path.Combine(root, "Claude");
                Directory.CreateDirectory(configFolder);
                string desktopConfig = Path.Combine(
                    configFolder,
                    "claude_desktop_config.json");
                File.WriteAllText(
                    desktopConfig,
                    "{\n  \"mcpServers\": {\n    \"existing\": {\"command\":\"existing\"}\n  }\n}\n");
                string publishedDll = CreatePublishedDll(root);

                WithMcpInstallConfig(publishedDll, () =>
                {
                    bool installed = McpInstallService.Install(
                        configFolder,
                        out string message);
                    AssertTrue(installed, "Claude Desktop MCP installation failed: " + message);
                    AssertTrue(
                        message.Contains("Claude Desktop"),
                        "Claude Desktop install message did not identify the client.");
                    AssertClaudeServerAndExistingEntry(desktopConfig);
                    AssertFalse(
                        File.Exists(Path.Combine(configFolder, ".mcp.json")),
                        "Claude Desktop installation created a Claude Code config file.");
                });
            });
        }

        private static string CreatePublishedDll(string root)
        {
            string publishedDll = Path.Combine(
                root,
                "publish",
                "UnityAutorun.Mcp.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(publishedDll));
            File.WriteAllText(publishedDll, "test");
            return publishedDll;
        }

        private static void WithMcpInstallConfig(
            string publishedDll,
            Action action,
            string projectRoot = null)
        {
            McpInstallConfig previous = McpInstallConfig.TestInstance;
            try
            {
                McpInstallConfig.TestInstance = new McpInstallConfig
                {
                    PublishedDllPath = publishedDll,
                    ProjectRoot = projectRoot ?? "",
                    ClaudeServerJson = "{\"command\":\"dotnet\",\"args\":[\"UnityAutorun.Mcp.dll\",\"mcp\"],\"env\":{\"UNITY_AUTORUN_TOOL_ROOT\":\"tool\"}}"
                };
                action();
            }
            finally
            {
                McpInstallConfig.TestInstance = previous;
            }
        }

        private static void AssertClaudeServerAndExistingEntry(string configPath)
        {
            JsonObject document = ParseObject(File.ReadAllText(configPath));
            JsonObject servers = document["mcpServers"]?.AsObject();
            AssertTrue(servers != null, "MCP config has no mcpServers object.");
            AssertTrue(
                servers.ContainsKey("existing"),
                "MCP installation removed an existing server.");
            JsonObject installed = servers[McpInstallConfig.ServerName]?.AsObject();
            AssertTrue(installed != null, "MCP config has no unity-autorun server.");
            AssertEqual(
                "dotnet",
                Text(installed, "command"),
                "MCP server command was not installed correctly.");
        }

        private static void WithUnityProject(Action<string> action)
        {
            WithTemporaryDirectory(root =>
            {
                Directory.CreateDirectory(Path.Combine(root, "Assets"));
                Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
                string previous = Environment.GetEnvironmentVariable("UNITY_AUTORUN_PROJECT_ROOT");
                try
                {
                    Environment.SetEnvironmentVariable("UNITY_AUTORUN_PROJECT_ROOT", root);
                    action(root);
                }
                finally { Environment.SetEnvironmentVariable("UNITY_AUTORUN_PROJECT_ROOT", previous); }
            });
        }
        private static void WithBridgeProfile(string profile, Action action)
        {
            string previous = Environment.GetEnvironmentVariable(BridgeEndpointProvider.ProfileEnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(BridgeEndpointProvider.ProfileEnvironmentVariable, profile);
                action();
            }
            finally { Environment.SetEnvironmentVariable(BridgeEndpointProvider.ProfileEnvironmentVariable, previous); }
        }
        private static void WithTemporaryDirectory(Action<string> action)
        {
            string root = Path.Combine(Path.GetTempPath(), "unity-autorun-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try { action(root); }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        private static JsonObject ParseObject(string json) { return JsonNode.Parse(json).AsObject(); }
        private static bool Boolean(JsonObject value, string key) { return value?[key]?.GetValue<bool>() == true; }
        private static string Text(JsonObject value, string key) { return value?[key]?.GetValue<string>(); }
        private static void AssertTrue(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void AssertFalse(bool condition, string message) { AssertTrue(!condition, message); }
        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(message + " Expected=" + expected + ", actual=" + actual + ".");
        }
    }
}
