using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
        private static readonly AutoRunScriptSourceResolver Sources = new AutoRunScriptSourceResolver();
        private static readonly Dictionary<Type, CatalogEntry> Metadata = new Dictionary<Type, CatalogEntry>();
        private static readonly List<StoredRequest> RecentRequests = new List<StoredRequest>();
        private static readonly Dictionary<string, StoredRequest> LatestRequests = new Dictionary<string, StoredRequest>(StringComparer.Ordinal);
        private static AutoRunScriptRunner _runner;
        private static StoredRequest _activeRequest;
        private static bool _initialized;
        private static bool _refreshing;
        private static bool _catalogDirty;
        private static double _nextCatalogRefreshAt;
        private static long _catalogRevision;
        private static long _runsRevision;
        private static string _catalogSignature;
        private static RunPresentation _runPresentation;
        private static double _lastSessionSaveAt;
        private static string SessionKey { get { return "UnityAutorunTool.Scripts." + Application.dataPath; } }

        static AutoRunScriptService()
        {
            EditorApplication.update += InitializeOnNextUpdate;
            EditorApplication.update += Pump;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.projectChanged += InvalidateCatalog;
        }

        public static event Action CatalogChanged;
        public static event Action RunsChanged;
        public static long CatalogRevision { get { EnsureInitialized(); return _catalogRevision; } }
        public static long RunsRevision { get { EnsureInitialized(); return _runsRevision; } }
        public static bool HasActiveRun { get { EnsureInitialized(); return _activeRequest != null && !_activeRequest.Status.IsTerminal; } }
        public static List<string> CatalogErrors { get { EnsureInitialized(); return new List<string>(Errors); } }
        public static List<AutoRunScriptDefinition> List() { EnsureInitialized(); return new List<AutoRunScriptDefinition>(Definitions); }
        public static List<AutoRunScriptRunStatus> ListRuns()
        {
            EnsureInitialized();
            return RecentRequests.Select(request => request.Status).ToList();
        }
        public static AutoRunScriptRunStatus GetLatestStatus(string scriptId)
        {
            EnsureInitialized();
            StoredRequest request;
            return scriptId != null && LatestRequests.TryGetValue(scriptId, out request) ? request.Status : null;
        }

        private static void EnsureInitialized() { if (!_initialized) Initialize(); }
        private static void InitializeOnNextUpdate()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= InitializeOnNextUpdate;
            Initialize();
        }
        private static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _runner = new AutoRunScriptRunner(new AutoRunScriptDefinition[0], new AutoRunUnityScriptHost(), () => EditorApplication.timeSinceStartup);
            RestoreRequests();
            Refresh();
            if (Requests.Count > 0) NotifyRunsChanged();
        }

        public static void Refresh()
        {
            // Initialize already builds the catalog. Do not scan it a second time for
            // the very first external Refresh call.
            if (!_initialized) { Initialize(); return; }
            if (_refreshing) return;
            _refreshing = true;
            try
            {
                Definitions.Clear();
                Errors.Clear();
                Sources.BeginRefresh();
                foreach (Type type in TypeCache.GetTypesDerivedFrom<AutoRunScript>().OrderBy(value => value.FullName, StringComparer.Ordinal))
                {
                    if (type.IsAbstract) continue;
                    CatalogEntry entry;
                    if (!Metadata.TryGetValue(type, out entry))
                    {
                        entry = ReadMetadata(type);
                        Metadata.Add(type, entry);
                    }
                    if (entry.Definition == null) { Errors.Add(entry.RegistrationError); continue; }
                    AutoRunScriptDefinition definition = entry.Definition;
                    definition.ValidationError = entry.RegistrationError;
                    definition.SourcePath = Sources.Resolve(type);
                    Definitions.Add(definition);
                }
                _runner.SetDefinitions(Definitions);
                var graphErrors = Definitions.Select(definition => string.IsNullOrWhiteSpace(definition.Id)
                    ? definition.ValidationError : _runner.Validate(definition.Id)).ToArray();
                for (int index = 0; index < Definitions.Count; index++)
                {
                    if (graphErrors[index] == null) continue;
                    Definitions[index].ValidationError = graphErrors[index];
                    Errors.Add(Definitions[index].Id + ": " + graphErrors[index]);
                }
                _catalogDirty = false;
                string signature = JsonUtility.ToJson(new CatalogSnapshot { Definitions = Definitions, Errors = Errors });
                if (_catalogSignature != signature)
                {
                    _catalogSignature = signature;
                    _catalogRevision++;
                    Notify(CatalogChanged);
                }
            }
            finally { _refreshing = false; }
        }

        private static CatalogEntry ReadMetadata(Type type)
        {
            var attribute = Attribute.GetCustomAttribute(type, typeof(AutoRunScriptAttribute), false) as AutoRunScriptAttribute;
            if (attribute == null) return new CatalogEntry { RegistrationError = "Script is missing AutoRunScriptAttribute: " + type.FullName };
            var definition = new AutoRunScriptDefinition
            {
                Id = attribute.Id, Name = attribute.Name, Kind = attribute.Kind,
                Description = attribute.Description, Preconditions = attribute.Preconditions,
                PreparePlayMode = attribute.PreparePlayMode,
                Dependencies = attribute.Dependencies == null ? new string[0] : (string[])attribute.Dependencies.Clone(), ClassName = type.FullName
            };
            string error = null;
            if (string.IsNullOrWhiteSpace(definition.Id)) error = "Script ID is required: " + type.FullName;
            else if (string.IsNullOrWhiteSpace(definition.Name)) error = "Script display name is required: " + definition.Id;
            else if (type.ContainsGenericParameters || type.GetConstructor(Type.EmptyTypes) == null)
                error = "Script requires a public parameterless constructor: " + definition.Id;
            else if (definition.Kind == AutoRunScriptKind.Business
                && (string.IsNullOrWhiteSpace(definition.Preconditions)
                    || type.GetMethod(nameof(AutoRunScript.CheckPreconditions)).DeclaringType == typeof(AutoRunScript)))
                error = "Business scripts must declare Preconditions and override CheckPreconditions: " + definition.Id;
            if (error == null)
            {
                try
                {
                    PropertyInfo menuPath = type.GetProperty(nameof(AutoRunScriptDefinition.ConfigurationMenuPath), BindingFlags.Public | BindingFlags.Static);
                    if (menuPath != null)
                    {
                        if (menuPath.PropertyType != typeof(string) || menuPath.GetIndexParameters().Length != 0 || menuPath.GetGetMethod() == null)
                            error = "ConfigurationMenuPath must be a public static string property with a public getter: " + definition.Id;
                        else definition.ConfigurationMenuPath = (string)menuPath.GetValue(null, null);
                    }
                }
                catch (Exception exception)
                {
                    error = "Script configuration menu could not be read: " + definition.Id + " (" + exception.GetBaseException().Message + ")";
                }
            }
            if (error == null) definition.Factory = () => (AutoRunScript)Activator.CreateInstance(type);
            return new CatalogEntry { Definition = definition, RegistrationError = error };
        }

        private static void InvalidateCatalog()
        {
            _catalogDirty = true;
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
            RememberRecent(request);
            _activeRequest = request;
            string validation = _runner.Validate(scriptId);
            // Only the selected root may request initial preparation. A child that
            // requires Play Mode must validate it; never switch modes mid-iterator.
            AutoRunScriptDefinition rootDefinition = Definitions.FirstOrDefault(definition => definition.Id == scriptId);
            bool preparePlayMode = ensurePlayMode || (rootDefinition != null && rootDefinition.PreparePlayMode);
            if (validation != null) FailPending(request, "script_validation_failed", validation);
            else if (preparePlayMode && !EditorApplication.isPlaying)
            {
                request.WaitingForPlayMode = true;
                request.Status.Message = "Waiting for initial entry into Play Mode; no script steps have run.";
                SaveRequests();
                EditorApplication.isPlaying = true;
            }
            else StartReady(request);
            SaveRequests();
            NotifyRunsChanged();
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
            NotifyRunsChanged();
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
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "Editor", "AutorunScripts")) + Path.DirectorySeparatorChar;
            string legacyRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "AutorunScripts", "Editor")) + Path.DirectorySeparatorChar;
            StringComparison pathComparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.IsNullOrEmpty(definition.SourcePath))
            { reason = "Script source could not be identified uniquely."; return false; }
            string sourcePath = Path.GetFullPath(definition.SourcePath);
            if (!sourcePath.StartsWith(root, pathComparison) && !sourcePath.StartsWith(legacyRoot, pathComparison))
            { reason = "Only business scripts under Assets/Editor/AutorunScripts or the legacy Assets/AutorunScripts/Editor can be deleted from this manager."; return false; }
            // A source file can contain several registered script classes; never delete another script accidentally.
            if (Definitions.Any(value => value.Id != scriptId && value.SourcePath == definition.SourcePath))
            { reason = "This source file contains multiple registered scripts; edit it directly."; return false; }
            if (!Sources.IsSingleClassFile(definition.SourcePath))
            { reason = "Source ownership is ambiguous or the file declares multiple classes; edit it directly."; return false; }
            reason = null;
            return true;
        }

        private static void Pump()
        {
            if (!_initialized) return;
            double now = EditorApplication.timeSinceStartup;
            if (_catalogDirty && now >= _nextCatalogRefreshAt && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                _nextCatalogRefreshAt = now + 0.5;
                Refresh();
            }
            StoredRequest request = _activeRequest;
            if (request == null || request.Status.IsTerminal) return;
            if (request.Status.State == AutoRunScriptRunState.Pending)
            {
                request.Status.ElapsedSeconds = Math.Max(0, now - request.Status.StartedAt);
                if (request.Status.ElapsedSeconds >= request.Timeout)
                {
                    FailPending(request, "script_start_timeout", "Play Mode startup exceeded the script timeout.", AutoRunScriptRunState.TimedOut);
                }
                else if (request.WaitingForPlayMode && EditorApplication.isPlaying && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
                    StartReady(request);
            }
            if (request.Status.State == AutoRunScriptRunState.Running)
            {
                if (EditorApplication.isCompiling) _runner.FailAll("script_compilation", "Unity started recompiling during script execution; execution was terminated without restarting steps.");
                else _runner.Tick(now);
                SyncRunnerStatuses();
            }
            if (!_runPresentation.Equals(new RunPresentation(request.Status))) NotifyRunsChanged();
            if (request.Status.IsTerminal || now - _lastSessionSaveAt >= 1) SaveRequests();
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
            if (_activeRequest != null) NotifyRunsChanged();
            SaveRequests();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (!_initialized) return;
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                _runner.FailAll("play_mode_exited", "Play Mode is exiting during script execution; the run was terminated without restarting steps.");
                SyncRunnerStatuses();
                if (_activeRequest != null) NotifyRunsChanged();
                SaveRequests();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                if (_activeRequest != null && _activeRequest.WaitingForPlayMode && !_activeRequest.Status.IsTerminal)
                {
                    FailPending(_activeRequest, "play_mode_start_failed", "Unity returned to Edit Mode before the script could start.");
                    NotifyRunsChanged();
                }
                SaveRequests();
            }
        }

        private static void SyncRunnerStatuses()
        {
            if (_activeRequest == null) return;
            AutoRunScriptRunStatus status = _runner.GetStatus(_activeRequest.RunId);
            if (status != null) _activeRequest.Status = status;
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
                foreach (StoredRequest request in Requests.Values.OrderByDescending(value => value.Status.StartedAt))
                {
                    if (RecentRequests.Count < MaximumHistory) RecentRequests.Add(request);
                    if (request.ScriptId != null && !LatestRequests.ContainsKey(request.ScriptId)) LatestRequests.Add(request.ScriptId, request);
                    if (!request.Status.IsTerminal)
                    {
                        if (_activeRequest == null) _activeRequest = request;
                        else FailPending(request, "script_session_conflict", "Multiple pending runs were found in the session; only the latest initial startup request may resume.");
                    }
                }
            }
            catch (Exception exception) { Debug.LogError("AutoRun script session could not be restored: " + exception.Message); }
        }

        private static void RememberRecent(StoredRequest request)
        {
            int index = 0;
            while (index < RecentRequests.Count && RecentRequests[index].Status.StartedAt > request.Status.StartedAt) index++;
            RecentRequests.Insert(index, request);
            if (RecentRequests.Count > MaximumHistory) RecentRequests.RemoveAt(RecentRequests.Count - 1);
            if (request.ScriptId != null) LatestRequests[request.ScriptId] = request;
        }

        private static void NotifyRunsChanged()
        {
            if (_activeRequest != null && _activeRequest.Status.IsTerminal) _activeRequest = null;
            _runPresentation = _activeRequest == null ? default(RunPresentation) : new RunPresentation(_activeRequest.Status);
            _runsRevision++;
            Notify(RunsChanged);
        }

        private static void Notify(Action handlers)
        {
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        private sealed class CatalogEntry { public AutoRunScriptDefinition Definition; public string RegistrationError; }
        [Serializable] private sealed class CatalogSnapshot
        {
            public List<AutoRunScriptDefinition> Definitions;
            public List<string> Errors;
        }
        private struct RunPresentation : IEquatable<RunPresentation>
        {
            private readonly string _runId;
            private readonly AutoRunScriptRunState _state;
            private readonly string _code;
            private readonly string _message;
            private readonly int _logCount;
            private readonly string _lastLog;
            private readonly int _artifactCount;
            private readonly int _callCount;
            private readonly int _stackDepth;
            private readonly AutoRunScriptRunState _lastCallState;

            public RunPresentation(AutoRunScriptRunStatus status)
            {
                _runId = status.RunId; _state = status.State; _code = status.Code; _message = status.Message;
                _logCount = status.Logs.Count; _lastLog = _logCount == 0 ? null : status.Logs[_logCount - 1];
                _artifactCount = status.Artifacts.Count; _callCount = status.Calls.Count; _stackDepth = status.CallStack.Count;
                _lastCallState = _callCount == 0 ? default(AutoRunScriptRunState) : status.Calls[_callCount - 1].State;
            }
            public bool Equals(RunPresentation other)
            {
                return _runId == other._runId && _state == other._state && _code == other._code && _message == other._message
                    && _logCount == other._logCount && _lastLog == other._lastLog && _artifactCount == other._artifactCount
                    && _callCount == other._callCount && _stackDepth == other._stackDepth && _lastCallState == other._lastCallState;
            }
        }
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
