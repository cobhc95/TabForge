namespace TabForge.Platform;

/// <summary>A fill made by the toolkit adapter (solid, gradient or image); views keep one per look, not one per frame.</summary>
public interface IUiPaint { }

/// <summary>An outline made by the toolkit adapter (colour or gradient, width, caps, joins, dashes).</summary>
public interface IUiPen { }

/// <summary>A transform made by the toolkit adapter (translate, scale, rotate, matrix or a group of them).</summary>
public interface IUiTransform { }

/// <summary>A recorded drawing made by the toolkit adapter (an icon, a cached layer).</summary>
public interface IUiDrawing { }

/// <summary>
/// The handle form of the drawing port, for looks that colour values cannot carry (gradients, dashes, caps, cached drawings, any transform).
/// Owns: the handle-based drawing calls. Does not own: how handles are made (the adapter's factories).
/// Tests: TestDrawContextPort.
/// </summary>
public interface IDrawHandles
{
    void Rect(IUiPaint? fill, IUiPen? pen, double x, double y, double width, double height, double radiusX = 0, double radiusY = 0);
    void Line(IUiPen pen, double x1, double y1, double x2, double y2);
    void Ellipse(IUiPaint? fill, IUiPen? pen, double cx, double cy, double rx, double ry);
    void Path(IUiPaint? fill, IUiPen? pen, IUiPath path);
    void Drawing(IUiDrawing drawing);
    void PushClip(IUiPath clip);
    void PushTransform(IUiTransform transform);
    void PushOpacity(double opacity);
}

/// <summary>Unwraps port handles inside an adapter. Owns: rejecting another adapter's handle. Tests: TestDrawContextPort.</summary>
public static class UiHandles
{
    /// <summary>The adapter's own handle, or null for null; throws on a handle of another type instead of silently drawing nothing.</summary>
    public static T? Own<T>(object? handle) where T : class =>
        handle is null ? null : handle as T ?? throw new System.ArgumentException($"Expected a {typeof(T).Name}, got a {handle.GetType().Name}.", nameof(handle));
}
