using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Platform;
using TabForge.Presenters;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The tuner's cents needle: a -50..+50 scale with a bar at the cents value, coloured by <see cref="TunerPresenter.ToneForCents"/>.
/// Owns: the needle's drawing. Does not own: the cents value (the tuner window sets it) or the colour rule (TunerPresenter).
/// Thread: UI thread. Drawing goes through one reused <see cref="AvaloniaDrawContext"/> and palette colours (structs), so a frame allocates
/// nothing and never asks for layout.
/// Tests: TabForge.Avalonia.Tests.TunerWindowTests.
/// </summary>
public sealed class TunerNeedle : Control
{
    public static readonly StyledProperty<double> CentsProperty =
        AvaloniaProperty.Register<TunerNeedle, double>(nameof(Cents), double.NaN);

    private readonly AvaloniaDrawContext _draw = new();

    static TunerNeedle() => AffectsRender<TunerNeedle>(CentsProperty);

    /// <summary>Cents away from the note; NaN when nothing is heard (no bar is drawn).</summary>
    public double Cents { get => GetValue(CentsProperty); set => SetValue(CentsProperty, value); }

    public override void Render(DrawingContext context)
    {
        var light = ActualThemeVariant == ThemeVariant.Light;
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width < 20 || height < 10) return;
        var ctx = _draw.Use(context);
        var scale = new UiStroke(AvaloniaPalette.Get("TextPrimary", light), 1);
        ctx.Line(scale, TunerPresenter.NeedleX(-50, width), height - 8, TunerPresenter.NeedleX(50, width), height - 8);
        for (var cents = -50; cents <= 50; cents += 10)
            ctx.Line(scale, TunerPresenter.NeedleX(cents, width), height - 8, TunerPresenter.NeedleX(cents, width), height - (cents == 0 ? 34 : 18));
        if (double.IsNaN(Cents)) return;
        var paletteName = TunerPresenter.ToneForCents(Cents) switch
        {
            TunerTone.InTune => "Success",
            TunerTone.Close => "Warning",
            _ => "Danger",
        };
        ctx.Rect(AvaloniaPalette.Get(paletteName, light), null, TunerPresenter.NeedleX(Cents, width) - 3, 4, 6, height - 12, 2);
    }
}
