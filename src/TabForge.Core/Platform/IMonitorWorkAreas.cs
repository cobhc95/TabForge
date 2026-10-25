namespace TabForge.Platform;

// Owns: the question "which usable screen area is near this rectangle".
// Does not own: enumerating monitors (the adapter is src/TabForge.Avalonia/Platform/AvaloniaMonitorWorkAreas.cs).
// Tests: no dedicated test class.

/// <summary>Monitor work areas (the screen minus taskbars), in the same units as the rectangles passed in.</summary>
public interface IMonitorWorkAreas
{
    /// <summary>The work area of the monitor that contains most of <paramref name="bounds"/>, or the nearest one when it is off every monitor.</summary>
    UiRect WorkAreaNear(UiRect bounds);
}
