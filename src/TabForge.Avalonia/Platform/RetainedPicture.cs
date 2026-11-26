using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace TabForge.Avalonia.Platform;

// Owns: recording a control's static drawing once into a native Skia picture and adding it to the control's render data as one
//     operation. The compositor replays a control's whole render data whenever anything over it changes (every frame under a moving playhead); Avalonia's
//     managed replay of thousands of lines and glyph runs was most of the render thread's time, the picture's native replay culls to the changed area.
// Does not own: what is drawn (the caller's callback, through the same Avalonia drawing code, so the look is unchanged), when the control repaints.
//     On a GPU the raster is also kept as textures (PictureTileCache), so a frame redrawing part of it copies pixels instead of rasterising again.
// Threads: recorded on the UI thread, replayed on the render thread (a picture is immutable); the operation disposes the picture when the render data drops it.
internal static class RetainedPicture
{
    /// <summary>Records <paramref name="draw"/> (control coordinates, clipped to <paramref name="bounds"/>) and adds the picture to <paramref name="context"/>.</summary>
    public static void Draw(DrawingContext context, Rect bounds, RenderOptions options, RetiredTextures retired, Action<DrawingContext> draw)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect((float)bounds.Left, (float)bounds.Top, (float)bounds.Right, (float)bounds.Bottom));
        var impl = global::Avalonia.Skia.Helpers.DrawingContextHelper.WrapSkiaCanvas(canvas, new Vector(96, 96));
        SubpixelText?.SetValue(impl, false);   // the wrapper turns sub-pixel text off for a canvas it cannot see; the window's layer draws it on
        using (var recording = Wrap(impl))
        using (recording.PushRenderOptions(options))
            draw(recording);
        context.Custom(new PictureOperation(recorder.EndRecording(), bounds, retired));
    }

    /// <summary>The text, edge and bitmap options <paramref name="visual"/> draws with, so a recorded picture matches its direct drawing.</summary>
    public static RenderOptions OptionsOf(Visual visual) => new()
    {
        TextRenderingMode = RenderOptions.GetTextRenderingMode(visual),
        EdgeMode = RenderOptions.GetEdgeMode(visual),
        BitmapInterpolationMode = RenderOptions.GetBitmapInterpolationMode(visual),
        BitmapBlendingMode = RenderOptions.GetBitmapBlendingMode(visual),
    };

    // Avalonia's own drawing context over a Skia canvas: the public helper gives the platform context, and its DrawingContext wrapper is internal.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors, "Avalonia.Media.PlatformDrawingContext", "Avalonia.Base")]
    private static DrawingContext Wrap(IDrawingContextImpl impl) => (DrawingContext)PlatformContext.Invoke(new object[] { impl, true });

    private static readonly System.Reflection.FieldInfo? SubpixelText =
        typeof(global::Avalonia.Skia.Helpers.DrawingContextHelper).Assembly.GetType("Avalonia.Skia.DrawingContextImpl")?
            .GetField("_disableSubpixelTextRendering", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    private static readonly System.Reflection.ConstructorInfo PlatformContext =
        typeof(DrawingContext).Assembly.GetType("Avalonia.Media.PlatformDrawingContext", throwOnError: true)!
            .GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, new[] { typeof(IDrawingContextImpl), typeof(bool) })
        ?? throw new MissingMethodException("Avalonia.Media.PlatformDrawingContext(IDrawingContextImpl, bool)");

    private sealed class PictureOperation(SKPicture picture, Rect bounds, RetiredTextures retired) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        // The whole area counts as drawn, so the control stays hit-testable as it was with its paper or backdrop.
        public bool HitTest(Point p) => bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

        private readonly PictureTileCache _tiles = new(retired);
        private readonly object _gate = new();   // the render thread draws while the render data may drop (dispose) the operation
        private bool _disposed;

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            lock (_gate)
            {
                if (_disposed) return;
                var canvas = lease.SkCanvas;
                if (lease.CurrentOpacity >= 1 && lease.GrContext is { } gr
                    && _tiles.Draw(canvas, gr, picture, picture.CullRect, lease.SkSurface?.SurfaceProperties)) return;
                if (lease.CurrentOpacity >= 1) { canvas.DrawPicture(picture); return; }
                using var paint = new SKPaint { Color = new SKColor(255, 255, 255, (byte)Math.Round(lease.CurrentOpacity * 255)) };
                canvas.DrawPicture(picture, paint);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _tiles.Release();
                picture.Dispose();
            }
        }
    }
}
