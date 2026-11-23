using System;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace UnityAutorun.Recording
{
    internal sealed class RecordingWatermark
    {
        private const string SignatureAssetGuid = "9da098fb49b2417fb84528d6bc987c33";
        private const string RepositoryLinkAssetGuid = "11b5234fdc8b4155b6163ff8e0419a87";
        private readonly int _footerHeight;
        private readonly Overlay _signature;
        private readonly Overlay _repositoryLink;

        internal RecordingWatermark(int frameWidth, int frameHeight)
        {
            int shortSide = Mathf.Min(frameWidth, frameHeight);
            int margin = Mathf.Max(2, Mathf.RoundToInt(shortSide * 0.033f));
            _footerHeight = Mathf.Max(6, Mathf.RoundToInt(shortSide * 0.039f));
            _signature = new Overlay(SignatureAssetGuid, frameWidth * 0.65f, shortSide * 0.126f);
            _signature.Left = frameWidth - margin - _signature.Width;
            _signature.Bottom = _footerHeight + margin;
            _repositoryLink = new Overlay(RepositoryLinkAssetGuid, frameWidth * 0.92f, _footerHeight * 0.62f);
            _repositoryLink.Left = (frameWidth - _repositoryLink.Width) / 2;
            _repositoryLink.Bottom = (_footerHeight - _repositoryLink.Height) / 2;
        }

        internal void BurnInto(Texture2D frame)
        {
            // Unity's raw texture data starts at the bottom-left of the image.
            NativeArray<Color32> pixels = frame.GetRawTextureData<Color32>();
            int footerPixelCount = frame.width * _footerHeight;
            var black = new Color32(0, 0, 0, 255);
            for (int i = 0; i < footerPixelCount; i++)
                pixels[i] = black;

            _signature.Composite(pixels, frame.width);
            _repositoryLink.Composite(pixels, frame.width);
        }

        private sealed class Overlay
        {
            private readonly Color32[] _pixels;

            internal Overlay(string assetGuid, float maxWidth, float maxHeight)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(assetGuid));
                if (texture == null || !texture.isReadable)
                    throw new InvalidOperationException("A Unity Autorun recording watermark texture is missing or unreadable: " + assetGuid);

                float scale = Mathf.Min(maxWidth / texture.width, maxHeight / texture.height);
                Width = Mathf.Max(1, Mathf.RoundToInt(texture.width * scale));
                Height = Mathf.Max(1, Mathf.RoundToInt(texture.height * scale));
                _pixels = new Color32[Width * Height];
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                        _pixels[y * Width + x] = texture.GetPixelBilinear((x + 0.5f) / Width, (y + 0.5f) / Height);
            }

            internal int Width { get; }
            internal int Height { get; }
            internal int Left { get; set; }
            internal int Bottom { get; set; }

            internal void Composite(NativeArray<Color32> frame, int frameWidth)
            {
                for (int y = 0; y < Height; y++)
                {
                    int rowStart = (y + Bottom) * frameWidth + Left;
                    for (int x = 0; x < Width; x++)
                    {
                        Color32 overlay = _pixels[y * Width + x];
                        if (overlay.a == 0) continue;
                        Color32 pixel = frame[rowStart + x];
                        frame[rowStart + x] = new Color32(
                            Blend(pixel.r, overlay.r, overlay.a),
                            Blend(pixel.g, overlay.g, overlay.a),
                            Blend(pixel.b, overlay.b, overlay.a), 255);
                    }
                }
            }

            private static byte Blend(byte background, byte foreground, int alpha)
            {
                return (byte)((background * (255 - alpha) + foreground * alpha + 127) / 255);
            }
        }
    }
}
