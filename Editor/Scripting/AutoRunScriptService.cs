using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UnityAutorun.Scripting
{
    [InitializeOnLoad]
    public static class AutoRunScriptService
    {
        private const int MaximumHistory = 50;
        private static readonly List<AutoRunScriptDefinition> Definitions = new List<AutoRunScriptDefinition>();
        private static readonly Dictionary<string, StoredRequest> Requests = new Dictionary<string, StoredRequest>(StringComparer.Ordinal);
        private static readonly List<string> Errors = new List<string>();
        private static AutoRunScriptRunner _runner;
        private static bool _initialized;
        private static double _lastSessionSaveAt;
        private static string SessionKey { get { return "UnityAutorunTool.Scripts." + Application.dataPath; } }

        static AutoRunScriptService()
        {
            EditorApplication.delayCall += Initialize;
            EditorApplication.update += Pump;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        public static bool HasActiveRun { get { EnsureInitialized(); return Requests.Values.Any(request => !request.Status.IsTerminal); } }
        public static List<string> CatalogErrors { get { EnsureInitialized(); return new List<string>(Errors); } }
        public static List<AutoRunScriptDefinition> List() { EnsureInitialized(); return new List<AutoRunScriptDefinition>(Definitions); }
        public static List<AutoRunScriptRunStatus> ListRuns()
        {
            EnsureInitialized();
            return Requests.Values.Select(request => request.Status).OrderByDescending(status => status.StartedAt).Take(MaximumHistory).ToList();
        }

        private static void EnsureInitialized() { if (!_initialized) Initialize(); }
        private static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _runner = new AutoRunScriptRunner(new AutoRunScriptDefinition[0], new AutoRunUnityScriptHost(), () => EditorApplication.timeSinceStartup);
            RestoreRequests();
            Refresh();
        }

        public static void Refresh()
        {
            EnsureInitialized();
            Definitions.Clear();
            Errors.Clear();
            List<SourceEntry> sources = ReadSources();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<AutoRunScript>().OrderBy(value => value.FullName, StringComparer.Ordinal))
            {
                if (type.IsAbstract) continue;
                var attribute = Attribute.GetCustomAttribute(type, typeof(AutoRunScriptAttribute), false) as AutoRunScriptAttribute;
                if (attribute == null) { Errors.Add("Script is missing AutoRunScriptAttribute: " + type.FullName); continue; }
                var definition = new AutoRunScriptDefinition
                {
                    Id = attribute.Id, Name = attribute.Name, Kind = attribute.Kind,
                    Description = attribute.Description, Preconditions = attribute.Preconditions,
                    Dependencies = attribute.Dependencies == null ? new string[0] : (string[])attribute.Dependencies.Clone(),
                    ClassName = type.FullName, SourcePath = FindSource(type, sources)
                };
                if (string.IsNullOrWhiteSpace(definition.Id)) definition.ValidationError = "Script ID is required: " + type.FullName;
                else if (string.IsNullOrWhiteSpace(definition.Name)) definition.ValidationError = "Script display name is required: " + definition.Id;
                else if (type.ContainsGenericParameters || type.GetConstructor(Type.EmptyTypes) == null)
                    definition.ValidationError = "Script requires a public parameterless constructor: " + definition.Id;
                else if (definition.Kind == AutoRunScriptKind.Business
                    && (string.IsNullOrWhiteSpace(definition.Preconditions)
                        || type.GetMethod(nameof(AutoRunScript.CheckPreconditions)).DeclaringType == typeof(AutoRunScript)))
                    definition.ValidationError = "Business scripts must declare Preconditions and override CheckPreconditions: " + definition.Id;
                if (definition.ValidationError == null) definition.Factory = () => (AutoRunScript)Activator.CreateInstance(type);
                Definitions.Add(definition);
            }
            _runner.SetDefinitions(Definitions);
            // Evaluate against original registration errors, then publish all graph errors together.
            var graphErrors = Definitions.Select(definition => string.IsNullOrWhiteSpace(definition.Id)
                ? definition.ValidationError : _runner.Validate(definition.Id)).ToArray();
            for (int index = 0; index < Definitions.Count; index++)
            {
                if (graphErrors[index] == null) continue;
                Definitions[index].ValidationError = graphErrors[index];
                Errors.Add(Definitions[index].Id + ": " + graphErrors[index]);
            }
        }

        public static AutoRunScriptRunStatus Start(string scriptId, Dictionary<string, string> parameters = null,
            bool ensurePlayMode = false, double timeoutSeconds = 120, string runId = null)
        {
            EnsureInitialized();
            runId = string.IsNullOrWhiteSpace(runId) ? Guid.NewGuid().ToString("N") : runId;
            StoredRequest existing;
            if (Requests.TryGetValue(runId, out existing))
            {
                if (existing.ScriptId != scriptId || existing.EnsurePlayMode != ensurePlayMode || existing.Timeout != timeoutSeconds
                    || !ParametersEqual(existing.Parameters, parameters))
                    throw new AutoRunScriptException("run_id_conflict", "This run ID belongs to a different script request; no script was restarted.");
                return existing.Status;
            }
            if (double.IsNaN(timeoutSeconds) || double.IsInfinity(timeoutSeconds) || timeoutSeconds <= 0)
                throw new AutoRunScriptException("invalid_timeout", "Script timeout must be a positive finite number.");
            if (HasActiveRun) throw new AutoRunScriptException("script_busy", "Another script is pending or running.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new AutoRunScriptException("unity_compiling", "Wait for Unity compilation and asset refresh before starting scripts.");
            if (AutoRunCompilationMonitor.Messages.Count > 0)
                throw new AutoRunScriptException("script_compilation_failed", "Resolve the project's compiler errors before executing scripts; the last loaded assembly may be stale.");
            var request = new StoredRequest
            {
                ScriptId = scriptId, RunId = runId, EnsurePlayMode = ensurePlayMode, Timeout = timeoutSeconds,
                Parameters = (parameters ?? new Dictionary<string, string>()).Select(pair => new ParameterEntry { Key = pair.Key, Value = pair.Value }).ToList(),
                Status = new AutoRunScriptRunStatus { RunId = runId, ScriptId = scriptId, State = AutoRunScriptRunState.Pending,
                    Code = "pending", Message = "Waiting to start.", StartedAt = EditorApplication.timeSinceStartup }
            };
            Requests.Add(runId, request);
            string validation = _runner.Validate(scriptId);
            if (validation != null) FailPending(request, "script_validation_failed", validation);
            else if (ensurePlayMode && !EditorApplication.isPlaying)
            {
                request.WaitingForPlayMode = true;
                request.Status.Message = "Waiting for initial entry into Play Mode; no script steps have run.";
                SaveRequests();
                EditorApplication.isPlaying = true;
            }
            else StartReady(request);
            SaveRequests();
            return request.Status;
        }

        public static AutoRunScriptRunStatus GetStatus(string runId)
        {
            EnsureInitialized();
            StoredRequest request;
            return runId != null && Requests.TryGetValue(runId, out request) ? request.Status : null;
        }

        public static AutoRunScriptRunStatus Cancel(string runId)
        {
            EnsureInitialized();
            StoredRequest request;
            if (runId == null || !Requests.TryGetValue(runId, out request)) return null;
            if (request.Status.IsTerminal) return request.Status;
            if (request.Status.State == AutoRunScriptRunState.Pending)
            {
                request.WaitingForPlayMode = false;
                request.Status.State = AutoRunScriptRunState.Canceled;
                request.Status.Code = "script_canceled";
                request.Status.Message = "Pending script canceled; game state is preserved.";
                CompleteTime(request.Status);
            }
            else request.Status = _runner.Cancel(runId);
            SaveRequests();
            return request.Status;
        }

        public static bool CanDelete(string scriptId, out string reason)
        {
            EnsureInitialized();
            AutoRunScriptDefinition definition = Definitions.FirstOrDefault(value => value.Id == scriptId);
            if (definition == null) { reason = "Script not found."; return false; }
            if (definition.Kind == AutoRunScriptKind.Basic) { reason = "Basic scripts cannot be deleted from this manager."; return false; }
            string[] callers = Definitions.Where(value => (value.Dependencies ?? new string[0]).Contains(scriptId)).Select(value => value.Name).ToArray();
            if (callers.Length > 0) { reason = "Referenced by: " + string.Join(", ", callers); return false; }
            if (HasActiveRun) { reason = "Wait for the active script to finish before deleting script source."; return false; }
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "AutorunScripts", "Editor")) + Path.DirectorySeparatorChar;
            StringComparison pathComparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.IsNullOrEmpty(definition.SourcePath) || !Path.GetFullPath(definition.SourcePath).StartsWith(root, pathComparison))
            { reason = "Only business scripts under Assets/AutorunScripts/Editor can be deleted from this manager."; return false; }
            // A source file can contain several registered script classes; never delete another script accidentally.
            if (Definitions.Any(value => value.Id != scriptId && value.SourcePath == definition.SourcePath))
            { reason = "This source file contains multiple registered scripts; edit it directly."; return false; }
            reason = null;
            return true;
        }

        private static void Pump()
        {
            if (!_initialized) return;
            double now = EditorApplication.timeSinceStartup;
            bool pendingChanged = false;
            foreach (StoredRequest request in Requests.Values.ToArray())
            {
                if (request.Status.IsTerminal || request.Status.State != AutoRunScriptRunState.Pending) continue;
                request.Status.ElapsedSeconds = Math.Max(0, now - request.Status.StartedAt);
                if (request.Status.ElapsedSeconds >= request.Timeout)
                {
                    FailPending(request, "script_start_timeout", "Play Mode startup exceeded the script timeout.", AutoRunScriptRunState.TimedOut);
                    pendingChanged = true;
                }
                else if (request.WaitingForPlayMode && EditorApplication.isPlaying && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
                    StartReady(request);
            }
            if (_runner.HasActiveRuns)
            {
                if (EditorApplication.isCompiling) _runner.FailAll("script_compilation", "Unity started recompiling during script execution; execution was terminated without restarting steps.");
                else _runner.Tick(now);
                SyncRunnerStatuses();
                if (!_runner.HasActiveRuns || now - _lastSessionSaveAt >= 1) SaveRequests();
            }
            else if (pendingChanged) SaveRequests();
        }

        private static void StartReady(StoredRequest request)
        {
            request.WaitingForPlayMode = false;
            double remaining = request.Timeout - Math.Max(0, EditorApplication.timeSinceStartup - request.Status.StartedAt);
            if (remaining <= 0) { FailPending(request, "script_start_timeout", "Script startup exceeded its timeout.", AutoRunScriptRunState.TimedOut); return; }
            double originalStartedAt = request.Status.StartedAt;
            try
            {
                _runner.Start(request.ScriptId, request.Parameters.ToDictionary(entry => entry.Key, entry => entry.Value), request.Timeout, request.RunId);
            }
            catch (Exception exception)
            {
                var scriptException = exception as AutoRunScriptException;
                FailPending(request, scriptException?.Code ?? "script_start_failed", exception.Message);
                return;
            }
            request.Status = _runner.GetStatus(request.RunId);
            // The timeout and reported duration both include preparation before the first script instruction.
            request.Status.StartedAt = originalStartedAt;
            request.Status.ElapsedSeconds = Math.Max(0, (request.Status.IsTerminal ? request.Status.CompletedAt : EditorApplication.timeSinceStartup) - originalStartedAt);
        }

        private static void BeforeAssemblyReload()
        {
            if (!_initialized) return;
            _runner.FailAll("script_domain_reload", "Unity reloaded assemblies during script execution; the run was terminated and side-effecting steps will not be replayed.");
            SyncRunnerStatuses();
            SaveRequests();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (!_initialized) return;
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                _runner.FailAll("play_mode_exited", "Play Mode is exiting during script execution; the run was terminated without restarting steps.");
                SyncRunnerStatuses();
                SaveRequests();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                foreach (StoredRequest request in Requests.Values)
                    if (request.WaitingForPlayMode && !request.Status.IsTerminal)
                        FailPending(request, "play_mode_start_failed", "Unity returned to Edit Mode before the script could start.");
                SaveRequests();
            }
        }

        private static void SyncRunnerStatuses()
        {
            foreach (StoredRequest request in Requests.Values)
            {
                AutoRunScriptRunStatus status = _runner.GetStatus(request.RunId);
                if (status != null) request.Status = status;
            }
        }

        private static void FailPending(StoredRequest request, string code, string message, AutoRunScriptRunState state = AutoRunScriptRunState.Failed)
        {
            request.WaitingForPlayMode = false;
            request.Status.State = state;
            request.Status.Code = code;
            request.Status.Message = message;
            request.Status.Logs.Add(message);
            CompleteTime(request.Status);
        }
        private static void CompleteTime(AutoRunScriptRunStatus status)
        {
            status.CompletedAt = EditorApplication.timeSinceStartup;
            status.ElapsedSeconds = Math.Max(0, status.CompletedAt - status.StartedAt);
        }
        private static bool ParametersEqual(List<ParameterEntry> left, Dictionary<string, string> right)
        {
            right = right ?? new Dictionary<string, string>();
            return left.Count == right.Count && left.All(entry => right.ContainsKey(entry.Key) && right[entry.Key] == entry.Value);
        }
        private static void SaveRequests()
        {
            SessionState.SetString(SessionKey, JsonUtility.ToJson(new StoredRequests { Requests = Requests.Values.ToList() }));
            _lastSessionSaveAt = EditorApplication.timeSinceStartup;
        }
        private static void RestoreRequests()
        {
            string json = SessionState.GetString(SessionKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                StoredRequests saved = JsonUtility.FromJson<StoredRequests>(json);
                foreach (StoredRequest request in saved.Requests ?? new List<StoredRequest>())
                {
                    if (request == null || request.Status == null || string.IsNullOrEmpty(request.RunId)) continue;
                    Requests[request.RunId] = request;
                    // Only an initial Play Mode entry with zero executed steps is recoverable.
                    if (!request.Status.IsTerminal && !(request.Status.State == AutoRunScriptRunState.Pending && request.WaitingForPlayMode))
                        FailPending(request, "script_domain_reload", "Script execution cannot resume after an assembly reload; steps were not replayed.");
                }
            }
            catch (Exception exception) { Debug.LogError("AutoRun script session could not be restored: " + exception.Message); }
        }

        private static List<SourceEntry> ReadSources()
        {
            var sources = new List<SourceEntry>();
            foreach (string guid in AssetDatabase.FindAssets("t:MonoScript"))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
                if (script == null) continue;
                string path = assetPath;
                if (assetPath.StartsWith("Packages/", StringComparison.Ordinal))
                {
                    UnityEditor.PackageManager.PackageInfo package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
                    if (package != null) path = Path.Combine(package.resolvedPath, assetPath.Substring(package.assetPath.Length).TrimStart('/', '\\'));
                }
                if (!Path.IsPathRooted(path)) path = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), path);
                sources.Add(new SourceEntry { Type = script.GetClass(), Path = Path.GetFullPath(path), Text = script.text });
            }
            return sources;
        }
        private static string FindSource(Type type, List<SourceEntry> sources)
        {
            SourceEntry direct = sources.FirstOrDefault(source => source.Type == type);
            if (direct != null) return direct.Path;
            var pattern = new Regex(@"\bclass\s+" + Regex.Escape(type.Name) + @"\b");
            SourceEntry[] matches = sources.Where(source => pattern.IsMatch(source.Text ?? string.Empty)
                && (string.IsNullOrEmpty(type.Namespace) || Regex.IsMatch(source.Text ?? string.Empty, @"\bnamespace\s+" + Regex.Escape(type.Namespace) + @"\b"))).ToArray();
            return matches.Length == 1 ? matches[0].Path : null;
        }

        private sealed class SourceEntry { public Type Type; public string Path; public string Text; }
        [Serializable] private sealed class ParameterEntry { public string Key; public string Value; }
        [Serializable] private sealed class StoredRequest
        {
            public string ScriptId;
            public string RunId;
            public bool EnsurePlayMode;
            public double Timeout;
            public bool WaitingForPlayMode;
            public List<ParameterEntry> Parameters = new List<ParameterEntry>();
            public AutoRunScriptRunStatus Status;
        }
        [Serializable] private sealed class StoredRequests { public List<StoredRequest> Requests = new List<StoredRequest>(); }
    }
}
