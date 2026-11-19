using System.Collections.Concurrent;
using System.Security.Cryptography;
using SkiaSharp;
using Svg.Skia;

namespace FlowBlox.Core.Util.Resources
{
    public static class FlowBloxIconUtil
    {
        private static readonly ConcurrentDictionary<string, SKImage> _cache = new();

        /// <summary>
        /// Loads an SVG from a byte array and renders it into an SKImage with the given size.
        /// Uses an internal cache keyed by SHA256(data) + size to avoid redundant rendering.
        /// </summary>
        /// <param name="data">The raw SVG file content as byte array.</param>
        /// <param name="size">Target size in pixels (width = height).</param>
        /// <param name="color">Optional override color. If null, the original SVG colors are used.</param>
        /// <returns>An SKImage rendered at the requested size.</returns>
        /// <exception cref="ArgumentException">Thrown if <paramref name="data"/> is null or empty.</exception>
        public static SKImage CreateFromSVG(byte[] data, int size, SKColor? color = null)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("SVG data is null or empty.", nameof(data));
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size), "Size must be > 0");

            string hashKey = ComputeHashKey(data, size, color);

            if (_cache.TryGetValue(hashKey, out var cached))
            {
                return cached;
            }

            using var mem = new MemoryStream(data);
            var svg = new SKSvg();
            if (svg.Load(mem) == null || svg.Picture == null)
                throw new InvalidOperationException("Failed to load SVG.");

            var bounds = svg.Picture.CullRect;
            float scale = Math.Min(size / bounds.Width, size / bounds.Height);

            var scaleMatrix = SKMatrix.CreateScale(scale, scale);
            var info = new SKImageInfo(size, size);

            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            // center SVG
            float translateX = (size - bounds.Width * scale) / 2f;
            float translateY = (size - bounds.Height * scale) / 2f;
            canvas.Translate(translateX, translateY);

            if (color.HasValue)
            {
                using var paint = new SKPaint
                {
                    ColorFilter = SKColorFilter.CreateBlendMode(color.Value, SKBlendMode.SrcIn),
                    IsAntialias = true
                };
                canvas.DrawPicture(svg.Picture, in scaleMatrix, paint);
            }
            else
            {
                canvas.DrawPicture(svg.Picture, in scaleMatrix);
            }

            canvas.Flush();
            var image = surface.Snapshot();

            _cache.TryAdd(hashKey, image);
            return image;
        }

        private static string ComputeHashKey(byte[] data, int size, SKColor? color)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(data);
            string colorPart = color.HasValue ? color.Value.ToString() : "none";
            return $"{BitConverter.ToString(hash)}_{size}_{colorPart}";
        }
    }
}
