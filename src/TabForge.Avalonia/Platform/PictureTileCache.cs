using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using SkiaSharp;

namespace TabForge.Avalonia.Platform;

// Owns: one retained picture's raster, kept as GPU textures in device-pixel tiles at the exact scale and sub-pixel offset it is shown at, so a frame
//     that redraws part of it copies tiles instead of rasterising thousands of lines and glyphs again. Textures live in GPU memory, not the process heap.
// Does not own: the picture (RetainedPicture's operation), the canvas, when a frame draws.
// Threads: render thread only (Draw), except Release, which only queues the textures; they are freed on the render thread, where the GPU context is current.
internal sealed class PictureTileCache
{
    /// <summary>A tile's side in device pixels.</summary>
    private const int TileSize = 512;

    /// <summary>The most tiles one picture keeps (about 1 MB of GPU memory each); more than a 4K screen's worth, so a full view never thrashes.</summary>
    private const int MaxTiles = 64;

    private readonly RetiredTextures _retired;

    // Sub-pixel text as the window's own layer draws it; a surface without a pixel geometry draws text in grey.
    private static readonly SKSurfaceProperties LcdText = new(SKPixelGeometry.RgbHorizontal);

    private readonly Dictionary<(int X, int Y), SKImage> _tiles = new();
    private GRContext? _gr;
    private float _scale, _offsetX, _offsetY;
    private bool _keyed;

    /// <param name="retired">The owning view's bin: a later draw of that view frees the textures this cache drops.</param>
    public PictureTileCache(RetiredTextures retired) => _retired = retired;

    /// <summary>Draws <paramref name="picture"/> through the tiles; false when the transform cannot be cached (the caller then draws the picture itself).</summary>
    public bool Draw(SKCanvas canvas, GRContext gr, SKPicture picture, SKRect bounds, SKSurfaceProperties? props)
    {
        _retired.Free();
        var m = canvas.TotalMatrix;
        if (m.SkewX != 0 || m.SkewY != 0 || m.Persp0 != 0 || m.Persp1 != 0 || m.ScaleX <= 0 || Math.Abs(m.ScaleX - m.ScaleY) > 1e-4f) return false;
        var scale = m.ScaleX;
        var originX = MathF.Floor(m.TransX);
        var originY = MathF.Floor(m.TransY);
        var offsetX = m.TransX - originX;
        var offsetY = m.TransY - originY;
        // A new scale or sub-pixel offset (a zoom, a smooth scroll step) needs new tiles; while it keeps changing, the picture draws directly.
        if (!_keyed || !ReferenceEquals(gr, _gr) || Math.Abs(scale - _scale) > 1e-5f || Math.Abs(offsetX - _offsetX) > 1e-3f || Math.Abs(offsetY - _offsetY) > 1e-3f)
        {
            var wasKeyed = _keyed;
            Release();
            (_gr, _scale, _offsetX, _offsetY, _keyed) = (gr, scale, offsetX, offsetY, true);
            if (wasKeyed) return false;
        }
        // The tiles the frame's clip touches, in the picture's device grid (device pixels less the integer origin).
        var clip = canvas.DeviceClipBounds;
        var extent = new SKRect(bounds.Left * scale + offsetX, bounds.Top * scale + offsetY, bounds.Right * scale + offsetX, bounds.Bottom * scale + offsetY);
        var left = Math.Max(clip.Left - originX, extent.Left);
        var top = Math.Max(clip.Top - originY, extent.Top);
        var right = Math.Min(clip.Right - originX, extent.Right);
        var bottom = Math.Min(clip.Bottom - originY, extent.Bottom);
        if (right <= left || bottom <= top) return true;
        int x0 = (int)MathF.Floor(left / TileSize), y0 = (int)MathF.Floor(top / TileSize), x1 = (int)MathF.Floor((right - 1) / TileSize), y1 = (int)MathF.Floor((bottom - 1) / TileSize);
        if ((x1 - x0 + 1) * (y1 - y0 + 1) > MaxTiles) return false;
        canvas.Save();
        canvas.ResetMatrix();
        try
        {
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                {
                    if (Tile(gr, picture, x, y, props) is not { } image) return false;
                    canvas.DrawImage(image, originX + x * TileSize, originY + y * TileSize);
                }
        }
        finally { canvas.Restore(); }
        return true;
    }

    private SKImage? Tile(GRContext gr, SKPicture picture, int x, int y, SKSurfaceProperties? props)
    {
        if (_tiles.TryGetValue((x, y), out var image)) return image;
        if (_tiles.Count >= MaxTiles) Release();
        var info = new SKImageInfo(TileSize, TileSize, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
        using var surface = SKSurface.Create(gr, true, info, props ?? LcdText);
        if (surface is null) return null;
        var canvas = surface.Canvas;
        canvas.Clear();
        canvas.Translate(_offsetX - x * TileSize, _offsetY - y * TileSize);
        canvas.Scale(_scale);
        canvas.DrawPicture(picture);
        image = surface.Snapshot();
        _tiles[(x, y)] = image;
        return image;
    }

    /// <summary>Drops every tile; the textures are freed by the next render-thread draw.</summary>
    public void Release()
    {
        foreach (var image in _tiles.Values) _retired.Add(image);
        _tiles.Clear();
        _keyed = false;
    }

}

/// <summary>GPU textures a view's retained pictures dropped; freed by the view's next render-thread draw, where the GPU context is current.</summary>
internal sealed class RetiredTextures
{
    private readonly ConcurrentQueue<SKImage> _images = new();

    public void Add(SKImage image) => _images.Enqueue(image);

    public void Free()
    {
        while (_images.TryDequeue(out var image)) image.Dispose();
    }
}
