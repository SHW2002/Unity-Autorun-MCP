using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;

namespace UnityAutorun.Recording
{
    internal sealed class GameViewRecording : IDisposable
    {
        private static readonly Type GameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        private static readonly FieldInfo GameViewTexture = GameViewType?.GetField("m_RenderTexture",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo MainPlayModeView = typeof(EditorWindow).Assembly
            .GetType("UnityEditor.PlayModeView")?.GetMethod("GetMainPlayModeView",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo WindowHost = typeof(EditorWindow)
            .GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo ActiveWindow = WindowHost?.FieldType
            .GetProperty("actualView", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private readonly string _stagingPath;
        private readonly string _outputPath;
        private readonly int _fps;
        private MediaEncoder _encoder;
        private Texture2D _pixels;
        private RecordingWatermark _watermark;
        private int _sourceWidth;
        private int _sourceHeight;
        private long _lastFrameSlot = -1;

        internal GameViewRecording(string outputPath, int fps)
        {
            _outputPath = outputPath;
            _fps = fps;
            _stagingPath = Path.Combine(Path.GetDirectoryName(outputPath),
                ".unity-autorun-" + Guid.NewGuid().ToString("N") + ".mp4");
        }

        internal int FrameCount { get; private set; }
        internal int Width => _pixels == null ? 0 : _pixels.width;
        internal int Height => _pixels == null ? 0 : _pixels.height;
        internal static bool SupportsGameView => GameViewTexture != null && ActiveWindow != null;

        internal static EditorWindow FindGameView()
        {
            var mainView = MainPlayModeView?.Invoke(null, null) as EditorWindow;
            if (mainView != null && GameViewType.IsInstanceOfType(mainView) && IsVisible(mainView)) return mainView;
            foreach (UnityEngine.Object window in Resources.FindObjectsOfTypeAll(GameViewType))
                if (window is EditorWindow view && IsVisible(view)) return view;
            return null;
        }

        internal static RenderTexture GetFrame(EditorWindow view)
        {
            if (!IsVisible(view)) return null;
            // The Game View color target includes screen-space UI and excludes Unity's Editor chrome.
            var texture = GameViewTexture.GetValue(view) as RenderTexture;
            return texture != null && texture.IsCreated() ? texture : null;
        }

        private static bool IsVisible(EditorWindow view)
        {
            var host = WindowHost.GetValue(view) as UnityEngine.Object;
            return host != null && (ActiveWindow.GetValue(host) as EditorWindow) == view;
        }

        internal void WriteFrame(RenderTexture source, long frameSlot)
        {
            if (_encoder == null) Initialize(source);
            if (frameSlot <= _lastFrameSlot) return;

            RenderTexture target = RenderTexture.GetTemporary(Width, Height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                _pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgbWrite;
                RenderTexture.ReleaseTemporary(target);
            }

            _watermark.BurnInto(_pixels);
            AddFrame(frameSlot);
        }

        internal bool HasSizeChanged(RenderTexture source)
        {
            return FrameCount > 0 && (source.width != _sourceWidth || source.height != _sourceHeight);
        }

        private void Initialize(RenderTexture source)
        {
            _sourceWidth = source.width;
            _sourceHeight = source.height;
            float scale = Mathf.Min(1f, Mathf.Min(1920f / Mathf.Max(_sourceWidth, _sourceHeight),
                1080f / Mathf.Min(_sourceWidth, _sourceHeight)));
            int width = Mathf.FloorToInt(_sourceWidth * scale) & ~1;
            int height = Mathf.FloorToInt(_sourceHeight * scale) & ~1;
            if (width < 64 || height < 64)
                throw new InvalidOperationException("The Game View must be at least 64 × 64 pixels.");
            _pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
            _watermark = new RecordingWatermark(width, height);
            _encoder = new MediaEncoder(_stagingPath, new VideoTrackAttributes
            {
                width = (uint)width,
                height = (uint)height,
                frameRate = new MediaRational(_fps),
                bitRateMode = VideoBitrateMode.High,
                includeAlpha = false
            });
        }

        internal void Complete(long lastFrameSlot)
        {
            if (FrameCount == 0) throw new InvalidOperationException("No Game View frame was recorded.");
            if (lastFrameSlot > _lastFrameSlot) AddFrame(lastFrameSlot);
            CloseEncoder();
            if (!File.Exists(_stagingPath) || new FileInfo(_stagingPath).Length == 0)
                throw new IOException("Unity produced an empty video file.");
            File.Move(_stagingPath, _outputPath);
        }

        private void AddFrame(long frameSlot)
        {
            if (!_encoder.AddFrame(Width, Height, Width * 4, TextureFormat.RGBA32,
                _pixels.GetRawTextureData<byte>(), new MediaTime(frameSlot, (uint)_fps, 1)))
                throw new IOException("Unity's video encoder rejected a Game View frame.");
            _lastFrameSlot = frameSlot;
            FrameCount++;
        }

        private void CloseEncoder()
        {
            MediaEncoder encoder = _encoder;
            _encoder = null;
            encoder?.Dispose();
        }

        public void Dispose()
        {
            try { CloseEncoder(); }
            finally
            {
                if (_pixels != null) UnityEngine.Object.DestroyImmediate(_pixels);
                try { File.Delete(_stagingPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
