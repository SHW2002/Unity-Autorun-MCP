using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace UnityAutorun.Mcp
{
    public static class ScriptFiles
    {
        public static JsonObject Create(JsonObject args)
        {
            string path;
            try { path = ProjectPaths.ResolveGeneratedScriptPath(ToolDispatcher.Required(args, "path")); }
            catch (ArgumentException exception) { return ToolDispatcher.Error("invalid_script_path", exception.Message); }
            string source = ToolDispatcher.Required(args, "source");
            if (File.Exists(path) || Directory.Exists(path))
                return ToolDispatcher.Error("script_exists", "Existing scripts cannot be overwritten. Create a different script and preserve reused scripts.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(source);
            }
            catch (IOException) when (File.Exists(path))
            {
                return ToolDispatcher.Error("script_exists", "The script already exists and was not overwritten.");
            }
            string relative = Path.GetRelativePath(ProjectPaths.ResolveProjectRootDirectory(), path).Replace('\\', '/');
            return JsonUtil.Obj(("ok", true), ("code", "script_created"),
                ("message", "C# source created. Call refresh_scripts and inspect compilation and registered metadata before executing."),
                ("data", JsonUtil.Obj(("sourcePath", relative), ("requiresRefresh", true))));
        }

        public static JsonNode IncludeSource(JsonNode response, bool includeSource)
        {
            JsonObject script = response?["data"]?["script"] as JsonObject;
            if (script == null) return response;
            if (!includeSource) { script.Remove("source"); return response; }
            if (script["source"] != null) return response;
            string sourcePath = script["sourcePath"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(sourcePath)) return response;
            try
            {
                string path = ProjectPaths.ResolveGeneratedSourcePath(sourcePath);
                if (File.Exists(path)) script["source"] = File.ReadAllText(path);
            }
            catch (Exception exception) { script["sourceError"] = exception.Message; }
            return response;
        }
    }
}
