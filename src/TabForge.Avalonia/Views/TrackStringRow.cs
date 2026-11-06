using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Views;

// Owns: one string row of the track properties tuning card: speaker toggle (lit in the master accent while its repeat is on), number, the drawn string (click rings it, then it vibrates), minus, plus and the note box at the far right.
// Does not own: the tuning (TrackTuningDraft), the sound (IStringRinger), the frame that moves the vibrate (TrackPropertiesWindow.Strings.cs) or the string list.
// Tests: TabForge.Avalonia.Tests.TrackPropertiesWindowTests.
// Threads: UI thread only.

/// <summary>The row of one string. The window builds one per string and calls <see cref="Show"/> after every change; the row never rebuilds itself.</summary>
internal sealed class TrackStringRow : Grid
{
    private const string SpeakerShape = "M2,6 L5,6 L9,3 L9,13 L5,10 L2,10 Z M11,5 Q14,8 11,11";
    private const double HoverLightOpacity = 0.14, GlowPeak = 0.9, GlowHeight = 8, GlowBlur = 6, RestOpacity = 0.92;
    private readonly Border _hoverLight, _glow;
    private readonly TranslateTransform _shake = new();
    private readonly ToggleButton _speaker;
    private readonly Func<int, string, bool> _typed;
    private int _midi;
    private bool _syncing;

    /// <param name="index">The string's place, 0 = highest.</param>
    /// <param name="step">Runs with (index, ±1) for the minus and plus buttons.</param>
    /// <param name="typed">Runs with (index, text) on Enter in the note box; false when the text is no note.</param>
    /// <param name="ring">Runs when the drawn string is clicked.</param>
    /// <param name="loop">Runs when the speaker toggle flips.</param>
    public TrackStringRow(int index, Action<int, int> step, Func<int, string, bool> typed, Action<int> ring, Action<int> loop)
    {
        Index = index;
        _typed = typed;
        Height = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringRowHeight;
        var numberWidth = TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringNumberWidth;
        var speakerWidth = TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringSpeakerWidth;
        ColumnDefinitions = new ColumnDefinitions($"{speakerWidth},{numberWidth},*,Auto,Auto,Auto");
        var number = new TextBlock { Text = (index + 1).ToString(), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
        _speaker = Speaker(index);
        _speaker.IsCheckedChanged += (_, _) => { if (!_syncing) loop(index); };
        Line = new Rectangle { Name = $"StringLine{index}", VerticalAlignment = VerticalAlignment.Center, RenderTransform = _shake };
        _hoverLight = new Border { Name = $"StringHover{index}", IsVisible = false, IsHitTestVisible = false, Opacity = HoverLightOpacity, CornerRadius = new CornerRadius(ChromeMetrics.RadiusSmall),
            [!Border.BackgroundProperty] = new DynamicResourceExtension("AccentBrush") };
        _glow = new Border { Name = $"StringGlow{index}", Opacity = 0, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(GlowHeight / 2),
            Height = GlowHeight, Effect = new BlurEffect { Radius = GlowBlur }, [!Border.BackgroundProperty] = new DynamicResourceExtension("AccentBrush") };
        var hit = new Panel { Name = $"StringHit{index}", Background = AvaloniaPalette.TransparentBrush,
            Margin = new Thickness(0, 0, 8, 0), ClipToBounds = true, Cursor = new Cursor(StandardCursorType.Hand), Children = { _hoverLight, _glow, Line } };   // the hand says: clickable
        ToolTip.SetTip(hit, "Click to hear this string");
        hit.PointerEntered += (_, _) => SetHover(true);
        hit.PointerExited += (_, _) => SetHover(false);
        hit.PointerPressed += (_, e) => { if (e.GetCurrentPoint(hit).Properties.IsLeftButtonPressed) ring(index); };
        Line.Opacity = RestOpacity;
        NoteBox = NoteEntry(index);
        Add(_speaker, 0);
        Add(number, 1);
        Add(hit, 2);
        Add(StepButton($"StringDown{index}", "−", "Tune this string down a semitone", () => step(index, -1)), 3);
        Add(StepButton($"StringUp{index}", "+", "Tune this string up a semitone", () => step(index, 1)), 4);
        Add(NoteBox, 5);
    }

    public int Index { get; }
    public Rectangle Line { get; }
    public TextBox NoteBox { get; }

    /// <summary>Shows the string at this pitch: the note in the box and the line as thick as a string of that pitch.</summary>
    public void Show(int midi, bool light)
    {
        _midi = midi;
        var thickness = TrackPropertiesPresenter.StringThickness(midi);
        Line.Height = thickness;
        Line.RadiusX = Line.RadiusY = thickness / 2;
        Line.Fill = AvaloniaPalette.TuningStringBrush(light);
        ShowNote();
        ToolTip.SetTip(NoteBox, $"MIDI {midi} - type a note (e.g. D2, Eb3, F#4) and press Enter");
    }

    /// <summary>Puts the speaker toggle in a state without running the loop callback.</summary>
    public void SetLooping(bool on)
    {
        _syncing = true;
        _speaker.IsChecked = on;
        _syncing = false;
    }

    public bool Looping => _speaker.IsChecked == true;

    /// <summary>Moves the drawn string sideways for the ring's vibrate (0 = at rest).</summary>
    public void Vibrate(double offset) => _shake.Y = offset;

    /// <summary>The strike light behind the string: 1 at the moment it is struck, fading to 0 as the ring dies away.</summary>
    public void Glow(double strength) => _glow.Opacity = Math.Clamp(strength, 0, 1) * GlowPeak;

    /// <summary>The string under the pointer: a soft light behind it and the line at full strength, so it reads as clickable.</summary>
    private void SetHover(bool on)
    {
        _hoverLight.IsVisible = on;
        Line.Opacity = on ? 1 : RestOpacity;
    }

    private void ShowNote() => NoteBox.Text = MusicTheoryService.NoteName(_midi);

    private void Add(Control child, int column)
    {
        SetColumn(child, column);
        Children.Add(child);
    }

    private TextBox NoteEntry(int index)
    {
        var box = new TextBox
        {
            Name = $"NoteBox{index}", Width = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringNoteBoxWidth, MinHeight = 0, Height = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringStepHeight,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold,
        };
        global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithMargin(box, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringNoteBoxMargin);
        global::TabForge.Avalonia.UiMetrics.WithPadding(box, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringNoteBoxPadding.Left, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringNoteBoxPadding.Top);
        AutomationProperties.SetName(box, $"String {index + 1} note");
        // Enter applies a typed note; Esc, a bad note or leaving the box puts the string's note back.
        box.KeyDown += (_, k) =>
        {
            if (k.Key == Key.Enter) { if (!_typed(index, box.Text ?? "")) ShowNote(); k.Handled = true; }
            else if (k.Key == Key.Escape) { ShowNote(); k.Handled = true; }
        };
        box.LostFocus += (_, _) => ShowNote();
        return box;
    }

    private static ToggleButton Speaker(int index)
    {
        var body = new Path { Data = Geometry.Parse(SpeakerShape), Stretch = Stretch.Uniform, Width = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringSpeakerIconSize, Height = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringSpeakerIconSize, StrokeThickness = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringSpeakerStroke };
        body[!Shape.FillProperty] = new DynamicResourceExtension("TextPrimary");
        body[!Shape.StrokeProperty] = new DynamicResourceExtension("TextPrimary");
        var toggle = new ToggleButton
        {
            Name = $"SpeakerToggle{index}", Width = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringStepWidth, Height = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringStepHeight, Content = body,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
        };
        global::TabForge.Avalonia.UiMetrics.WithPadding(toggle, 0, 0);
        LightWhileOn(toggle, body);
        AutomationProperties.SetName(toggle, $"Repeat string {index + 1}");
        ToolTip.SetTip(toggle, "Ring this string over and over until switched off");
        return toggle;
    }

    // The speaker's own look: the panel fill with a soft edge at rest, the master accent (AccentBrush, the Appearance accent) with the on-accent icon
    // while its repeat is on, and the hover and press shades over either; one border template paints it, so no theme style hides the lit fill.
    private static void LightWhileOn(ToggleButton toggle, Path icon)
    {
        toggle.Template = AvaloniaButtonTemplates.SingleBorder("Bd");
        toggle.BorderThickness = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.SwatchOutline;
        toggle.CornerRadius = new CornerRadius(ChromeMetrics.RadiusMedium);
        IBrush Resource(string key) => toggle.TryFindResource(key, toggle.ActualThemeVariant, out var found) && found is IBrush brush ? brush : AvaloniaPalette.TransparentBrush;
        IBrush Rest() => Resource(toggle.IsChecked == true ? "AccentBrush" : "Panel2Brush");
        _ = HoverShade.Wire(toggle, brush => toggle.Background = brush, Rest);
        void Paint()
        {
            var on = toggle.IsChecked == true;
            toggle.Background = Rest();
            toggle[!TemplatedControl.BorderBrushProperty] = new DynamicResourceExtension(on ? "AccentBrush" : "BorderSoftBrush");
            icon[!Shape.FillProperty] = new DynamicResourceExtension(on ? "OnAccentBrush" : "TextPrimary");
            icon[!Shape.StrokeProperty] = new DynamicResourceExtension(on ? "OnAccentBrush" : "TextPrimary");
        }
        toggle.IsCheckedChanged += (_, _) => Paint();
        toggle.ActualThemeVariantChanged += (_, _) => Paint();
        toggle.AttachedToVisualTree += (_, _) => Paint();
        Paint();
    }

    private static Button StepButton(string name, string text, string tip, Action click)
    {
        var button = new Button
        {
            Name = name, Content = text, Width = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringStepWidth, Height = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringStepHeight,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithPadding(button, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringStepButtonPadding);
        global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithMargin(button, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringStepButtonGap);
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => click();
        return button;
    }
}
