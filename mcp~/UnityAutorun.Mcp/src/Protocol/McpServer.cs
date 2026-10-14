using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace UnityAutorun.Mcp
{
    public sealed class McpServer
    {
        private readonly ToolDispatcher _dispatcher;
        private readonly List<byte> _buffer = new List<byte>();
        private TextWriter _output;

        public McpServer(ToolDispatcher dispatcher = null) { _dispatcher = dispatcher ?? new ToolDispatcher(); }
        public Task RunAsync() { return RunAsync(Console.OpenStandardInput(), Console.Out); }

        public async Task RunAsync(Stream input, TextWriter output)
        {
            _output = output;
            var chunk = new byte[4096];
            int read;
            while ((read = await input.ReadAsync(chunk, 0, chunk.Length)) > 0)
            {
                for (int i = 0; i < read; i++) _buffer.Add(chunk[i]);
                await ReadMessagesAsync();
            }
            if (_buffer.Count > 0)
            {
                string trailing = Encoding.UTF8.GetString(_buffer.ToArray()).Trim();
                _buffer.Clear();
                if (trailing.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    WriteError(null, -32700, "Incomplete Content-Length frame.");
                else if (trailing.Length > 0) await HandleMessageAsync(trailing);
            }
        }

        private async Task ReadMessagesAsync()
        {
            while (_buffer.Count > 0)
            {
                if (_buffer[0] == (byte)'C' || _buffer[0] == (byte)'c')
                {
                    int headerEnd = Find(new byte[] { 13, 10, 13, 10 });
                    if (headerEnd < 0) return;
                    string header = Encoding.ASCII.GetString(_buffer.GetRange(0, headerEnd).ToArray());
                    string lengthLine = null;
                    foreach (string line in header.Split(new[] { "\r\n" }, StringSplitOptions.None))
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) lengthLine = line;
                    if (lengthLine == null || !int.TryParse(lengthLine.Substring(15).Trim(), out int length) || length < 0)
                    {
                        _buffer.Clear();
                        WriteError(null, -32700, "Invalid Content-Length header.");
                        return;
                    }
                    int bodyStart = headerEnd + 4;
                    if (_buffer.Count - bodyStart < length) return;
                    string body = Encoding.UTF8.GetString(_buffer.GetRange(bodyStart, length).ToArray());
                    _buffer.RemoveRange(0, bodyStart + length);
                    await HandleMessageAsync(body);
                }
                else
                {
                    int newline = _buffer.IndexOf(10);
                    if (newline < 0) return;
                    string line = Encoding.UTF8.GetString(_buffer.GetRange(0, newline).ToArray()).Trim();
                    _buffer.RemoveRange(0, newline + 1);
                    if (line.Length > 0) await HandleMessageAsync(line);
                }
            }
        }

        private int Find(byte[] pattern)
        {
            for (int index = 0; index <= _buffer.Count - pattern.Length; index++)
            {
                int item = 0;
                while (item < pattern.Length && _buffer[index + item] == pattern[item]) item++;
                if (item == pattern.Length) return index;
            }
            return -1;
        }

        private async Task HandleMessageAsync(string line)
        {
            JsonObject request;
            try { request = JsonNode.Parse(line)?.AsObject(); }
            catch (Exception exception) { WriteError(null, -32700, exception.Message); return; }
            if (request == null) { WriteError(null, -32600, "Invalid JSON-RPC request."); return; }
            try
            {
                string method = request["method"]?.GetValue<string>();
                JsonNode id = request["id"]?.DeepClone();
                if (method != null && method.StartsWith("notifications/", StringComparison.Ordinal)) return;
                switch (method)
                {
                    case "initialize":
                        WriteResult(id, JsonUtil.Obj(("protocolVersion", "2024-11-05"),
                            ("capabilities", JsonUtil.Obj(("tools", new JsonObject()))),
                            ("serverInfo", JsonUtil.Obj(("name", "unity-autorun"), ("version", McpVersion.Current))),
                            ("instructions", "Read get_script_guidance before using gen-script or run-script. Generate actual Unity-compiled C# scripts, inspect existing scripts and reuse only exactly matching flows without modifying them. Use start_script then get_script_status until terminal. Completion, failure and cancellation preserve game state; stopping Play Mode is explicit. Bridge endpoints are discovered automatically.")));
                        break;
                    case "ping": WriteResult(id, new JsonObject()); break;
                    case "tools/list": WriteResult(id, JsonUtil.Obj(("tools", McpTools.All()))); break;
                    case "tools/call":
                        JsonObject parameters = request["params"]?.AsObject() ?? new JsonObject();
                        JsonNode result = await _dispatcher.DispatchAsync(parameters["name"]?.GetValue<string>() ?? "",
                            parameters["arguments"]?.AsObject() ?? new JsonObject());
                        WriteResult(id, JsonUtil.Obj(("isError", result?["ok"]?.GetValue<bool>() == false),
                            ("content", new JsonArray { JsonUtil.Obj(("type", "text"), ("text", JsonUtil.Pretty(result))) })));
                        break;
                    default: WriteError(id, -32601, "Method not found: " + method); break;
                }
            }
            catch (Exception exception) { WriteError(request["id"]?.DeepClone(), -32000, exception.Message); }
        }

        private void WriteResult(JsonNode id, JsonNode result)
        {
            _output.WriteLine(JsonUtil.Obj(("jsonrpc", "2.0"), ("id", id), ("result", result)).ToJsonString());
            _output.Flush();
        }
        private void WriteError(JsonNode id, int code, string message)
        {
            _output.WriteLine(JsonUtil.Obj(("jsonrpc", "2.0"), ("id", id),
                ("error", JsonUtil.Obj(("code", code), ("message", message)))).ToJsonString());
            _output.Flush();
        }
    }
}
