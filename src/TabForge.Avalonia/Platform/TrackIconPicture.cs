using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The owner's track-row icon (TrackIconArt) drawn at the control's size in the legible ink and the fixed accent, for the track properties picture.
/// Owns: drawing one icon key. Does not own: which key a track shows (Core TrackIconKeys) or the art (TrackIconArt).
/// Tests: TabForge.Avalonia.Tests.TrackPropertiesWindowTests.
/// </summary>
public sealed class TrackIconPicture : Control
{
    private readonly AvaloniaDrawContext _draw = new();
    private string _key = "";

    public string IconKey
    {
        get => _key;
        set { _key = value; InvalidateVisual(); }
    }

    public override void Render(DrawingContext context)
    {
        var light = ActualThemeVariant == ThemeVariant.Light;
        var size = System.Math.Min(Bounds.Width, Bounds.Height);
        TrackIconArt.Draw(_draw.Use(context), _key, 0, 0, size, AvaloniaPalette.Get("InstrumentLegible", light), AvaloniaPalette.Get("TrackSilhouetteAccent", light));
    }
}
