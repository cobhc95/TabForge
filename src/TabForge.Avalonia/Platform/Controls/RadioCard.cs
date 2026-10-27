using System.Collections.Generic;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Platform.Controls;

/// <summary>The cards that share one choice: selecting one clears the others, and the arrow keys walk between them.</summary>
public sealed class RadioCardGroup
{
    internal readonly List<RadioCard> Cards = new();

    /// <param name="name">The radio group name its circles share (one per dialog).</param>
    public RadioCardGroup(string name) => Name = name;

    public string Name { get; }

    /// <summary>The card that is selected now; null before one is.</summary>
    public RadioCard? Selected => Cards.Find(c => c.IsSelected);
}

// Owns: the radio card of a choice dialog: a bordered card with a radio circle, a title, a muted hint and an optional preview under them. The WHOLE card selects
//   (not just the circle), with hover (soft fill, stronger border), pressed, selected (accent border and tint) and keyboard-focus states; Left/Up and Right/Down
//   move between the cards of the group and select, Space selects. Colours are theme keys; the pointer shape never changes.
// Does not own: what the choice means or the dialog around it (the window), the words (the caller / its presenter).
// Tests: TabForge.Avalonia.Tests.RadioCardTests, MediaCollectPromptTests, SectionAudioMoveTests, ClipLaneOverlapTests.
/// <summary>One choice card of a <see cref="RadioCardGroup"/>. UI thread only.</summary>
public sealed class RadioCard : Border
{
    private readonly RadioButton _radio;
    private readonly RadioCardGroup _group;
    private bool _over, _held, _keyFocus;

    /// <param name="name">The control-name prefix: "&lt;name&gt;Card", "&lt;name&gt;Radio" and "&lt;name&gt;Hint".</param>
    /// <param name="preview">Extra content under the hint (a folder tree sketch).</param>
    public RadioCard(RadioCardGroup group, string name, string title, string hint, bool selected, Control? preview = null)
    {
        _group = group;
        Name = name + "Card";
        Focusable = true;
        DialogMetrics.StyleChoiceCard(this);
        _radio = new RadioButton { Name = name + "Radio", Content = title, GroupName = group.Name, IsChecked = selected, FontWeight = FontWeight.SemiBold, IsHitTestVisible = false, Focusable = false };
        var text = new TextBlock { Name = name + "Hint", Text = hint, TextWrapping = TextWrapping.Wrap, FontSize = DialogMetrics.MediaSmallText }.WithMargin(DialogMetrics.SectionAudioHintIndent);
        text[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        var stack = new StackPanel { Children = { _radio, text } };
        if (preview is not null) stack.Children.Add(preview.WithMargin(DialogMetrics.SectionAudioHintIndent));
        Child = stack;
        AutomationProperties.SetName(this, title);
        group.Cards.Add(this);
        _radio.IsCheckedChanged += (_, _) => Paint();
        PointerEntered += (_, _) => { _over = true; Paint(); };
        PointerExited += (_, _) => { _over = false; _held = false; Paint(); };
        PointerPressed += OnPressed;
        PointerReleased += (_, _) => { _held = false; Paint(); };
        GotFocus += (_, e) => { _keyFocus = e.NavigationMethod != NavigationMethod.Pointer; Paint(); };
        LostFocus += (_, _) => { _keyFocus = false; Paint(); };
        Paint();
    }

    /// <summary>The radio circle (its IsChecked is the card's selection).</summary>
    public RadioButton Radio => _radio;

    public bool IsSelected
    {
        get => _radio.IsChecked == true;
        set { if (value) Select(); }
    }

    /// <summary>Selects this card (the others of the group are cleared).</summary>
    public void Select() => _radio.IsChecked = true;

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _held = true;
        Select();
        Focus();
        Paint();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left or Key.Up: Walk(-1); break;
            case Key.Right or Key.Down: Walk(1); break;
            case Key.Space: Select(); break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    // The next (or previous) card of the group, wrapping round: it takes the selection and the focus.
    private void Walk(int step)
    {
        var cards = _group.Cards;
        var next = cards[(cards.IndexOf(this) + step + cards.Count) % cards.Count];
        next.Select();
        next.Focus(NavigationMethod.Directional);
    }

    // Selected: accent border and tint. Otherwise rest, hover (soft fill, stronger border) or pressed. A keyboard-focused card shows the focus ring.
    private void Paint()
    {
        var on = IsSelected;
        this[!BackgroundProperty] = new DynamicResourceExtension(_held ? "PressBrush" : on ? "AccentSoftBrush" : _over ? "HoverBrush" : "PanelBackground");
        this[!BorderBrushProperty] = new DynamicResourceExtension(_keyFocus ? "FocusRingBrush" : on ? "AccentBrush" : _over || _held ? "BorderBrush" : "BorderSoftBrush");
        IsTabStop = on;
    }
}
