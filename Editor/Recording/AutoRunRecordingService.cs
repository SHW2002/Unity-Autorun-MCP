using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityAutorun.Recording
{
    [Serializable]
    public sealed class AutoRunRecordingStatus
    {
        public string recordingId;
        public string state;
        public string code;
        public string message;
        public string endReason;
        public string outputPath;
        public bool terminal;
        public int fps;
        public int maxSeconds;
        public int frameCount;
        public int width;
        public int height;
        public double elapsedSeconds;
    }

    [InitializeOnLoad]
    internal static class AutoRunRecordingService
    {
        internal const int DefaultFps = 30;
        internal const int DefaultMaxSeconds = 60;
        private const double FrameTimeout = 5;
        private const string HistoryKey = "UnityAutorunTool.Recording.History";

        [Serializable]
        private sealed class RecordingHistory
        {
            public List<AutoRunRecordingStatus> items = new List<AutoRunRecordingStatus>();
        }

        private static readonly RecordingHistory History =
            JsonUtility.FromJson<RecordingHistory>(SessionState.GetString(HistoryKey, ""))
            ?? new RecordingHistory();

        private static EditorWindow _view;
        private static GameViewRecording _writer;
        private static AutoRunRecordingStatus _active;
        private static double _requestedAt;
        private static double _startedAt;
        private static double _lastSourceAt;
        private static long _lastCapturedSlot;

        static AutoRunRecordingService()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode) Finish("play_mode_exited");
            };
            AssemblyReloadEvents.beforeAssemblyReload += () => Finish("assembly_reload");
            EditorApplication.quitting += () => Finish("editor_quit");
        }

        internal static AutoRunRecordingStatus Active => _active;
        internal static AutoRunRecordingStatus Latest => _active ??
            (History.items.Count == 0 ? null : History.items[0]);

        internal static AutoRunRecordingStatus Start(string recordingId, string path, int fps, int maxSeconds)
        {
            if (!Guid.TryParse(recordingId, out Guid id))
                return Error("invalid_recording_id", "recordingId must be a UUID.");
            recordingId = id.ToString("N");
            if (fps < 1 || fps > 60 || maxSeconds < 1 || maxSeconds > 600)
                return Error("invalid_recording_options", "Use 1–60 fps and a limit of 1–600 seconds.");

            string destination;
            try { destination = ResolveOutputPath(path, recordingId); }
            catch (Exception exception) { return Error("invalid_recording_path", exception.Message); }
            AutoRunRecordingStatus previous = Find(recordingId);
            if (previous != null)
            {
                // A retry may arrive after the file is written; check the original request before touching disk.
                if (previous.fps == fps && previous.maxSeconds == maxSeconds
                    && string.Equals(previous.outputPath, destination, PathComparison))
                    return previous;
                return Error("recording_id_conflict", "This recording ID was used with different settings.");
            }

            if (_active != null) return Error("recording_busy", "A Game View recording is already active.");
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isPaused)
                return Error("recording_play_mode_required", "Start recording in unpaused Play Mode with compilation finished.");
            if (!GameViewRecording.SupportsGameView || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                return Error("recording_unavailable", "This Unity Editor does not expose a graphics-enabled Game View recorder.");
            EditorWindow view = GameViewRecording.FindGameView();
            if (view == null) return Error("game_view_unavailable", "Open and show Unity's Game View before recording.");

            try
            {
                CheckOutputDirectory(destination);
                if (File.Exists(destination)) return Error("recording_file_exists", "Choose a new output file; recordings never overwrite files.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                _writer = new GameViewRecording(destination, fps);
            }
            catch (Exception exception) { return Error("recording_start_failed", exception.Message); }

            _view = view;
            _requestedAt = _lastSourceAt = EditorApplication.timeSinceStartup;
            _lastCapturedSlot = -1;
            _active = new AutoRunRecordingStatus
            {
                recordingId = recordingId, state = "waiting_for_frame", code = "ok",
                message = "Waiting for the Game View to render.", fps = fps, maxSeconds = maxSeconds,
                outputPath = destination
            };
            EditorApplication.update += Update;
            view.Repaint();
            AutoRunWindow.RepaintAllWindows();
            return _active;
        }

        internal static AutoRunRecordingStatus GetStatus(string recordingId)
        {
            return Guid.TryParse(recordingId, out Guid id)
                ? Find(id.ToString("N")) ?? Error("recording_not_found", "No recording exists with this ID in this Editor session.")
                : Error("invalid_recording_id", "recordingId must be a UUID.");
        }

        internal static AutoRunRecordingStatus Stop(string recordingId)
        {
            AutoRunRecordingStatus status = GetStatus(recordingId);
            if (status == _active) Finish("stopped");
            return status;
        }

        private static void Update()
        {
            if (EditorApplication.isCompiling) { Finish("compiling"); return; }
            if (_view == null) { Finish("game_view_closed"); return; }

            double now = EditorApplication.timeSinceStartup;
            if (_writer.FrameCount > 0 && now - _startedAt >= _active.maxSeconds)
            {
                Finish("duration_limit");
                return;
            }

            try
            {
                long slot = _writer.FrameCount == 0 ? 0 : FrameSlot(now - _startedAt);
                if (slot <= _lastCapturedSlot) return;
                RenderTexture frame = GameViewRecording.GetFrame(_view);
                if (frame == null)
                {
                    if (now - _lastSourceAt >= FrameTimeout) Finish("game_view_not_rendering");
                    return;
                }
                if (_writer.HasSizeChanged(frame)) { Finish("game_view_resized"); return; }

                _lastSourceAt = now;
                _writer.WriteFrame(frame, slot);
                _lastCapturedSlot = slot;
                _active.frameCount = _writer.FrameCount;
                if (_writer.FrameCount == 1)
                {
                    _startedAt = now;
                    _active.state = "recording";
                    _active.message = "Recording the Game View.";
                    _active.width = _writer.Width;
                    _active.height = _writer.Height;
                    AutoRunWindow.RepaintAllWindows();
                }
                _active.elapsedSeconds = now - _startedAt;
            }
            catch (Exception exception) { Finish("capture_failed", exception.Message); }
        }

        private static void Finish(string reason, string error = null)
        {
            if (_active == null) return;
            EditorApplication.update -= Update;
            AutoRunRecordingStatus status = _active;
            status.terminal = true;
            status.endReason = reason;
            double elapsed = EditorApplication.timeSinceStartup - (_writer.FrameCount > 0 ? _startedAt : _requestedAt);
            status.elapsedSeconds = Math.Min(status.maxSeconds, elapsed);
            try
            {
                if (error != null) throw new IOException(error);
                if (_writer.FrameCount == 0)
                    throw new IOException("No Game View frame was recorded. Keep the Game View visible in Play Mode.");
                _writer.Complete(Math.Max(0, FrameSlot(status.elapsedSeconds) - 1));
                status.state = "completed";
                status.message = "Game View video saved.";
                status.frameCount = _writer.FrameCount;
            }
            catch (Exception exception)
            {
                status.state = "failed";
                status.code = "recording_failed";
                status.message = exception.Message;
            }
            finally
            {
                try { _writer.Dispose(); }
                catch (Exception exception) { Debug.LogWarning("Could not release the video encoder: " + exception.Message); }
                _writer = null;
                _view = null;
                _active = null;
                History.items.Insert(0, status);
                if (History.items.Count > 20) History.items.RemoveAt(20);
                SessionState.SetString(HistoryKey, JsonUtility.ToJson(History));
                AutoRunWindow.RepaintAllWindows();
            }
        }

        private static long FrameSlot(double elapsed) => (long)Math.Floor(elapsed * _active.fps);

        private static AutoRunRecordingStatus Find(string id)
        {
            if (_active?.recordingId == id) return _active;
            return History.items.Find(item => item.recordingId == id);
        }

        private static AutoRunRecordingStatus Error(string code, string message)
        {
            return new AutoRunRecordingStatus { terminal = true, state = "failed", code = code, message = message };
        }

        private static StringComparison PathComparison => Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static string ResolveOutputPath(string relativePath, string id)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                relativePath = "Temp/UnityAutorunTool/Recordings/" + id + ".mp4";
            string root = ProjectRoot;
            string path = Path.GetFullPath(Path.Combine(root, relativePath));
            if (Path.IsPathRooted(relativePath) || relativePath.Contains(":")
                || !path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison)
                || !string.Equals(Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Recording path must be a project-relative .mp4 file.");
            return path;
        }

        private static void CheckOutputDirectory(string path)
        {
            string root = ProjectRoot;
            for (string current = Path.GetDirectoryName(path); !string.Equals(current, root, PathComparison);
                current = Path.GetDirectoryName(current))
            {
                if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Recording output directories cannot be symbolic links.");
            }
        }
    }
}
