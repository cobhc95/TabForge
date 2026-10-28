using System;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Platform;

// Owns: the small "Deleted ... Undo" message every preset list offers after a delete: shown over the bottom edge of the window for a few seconds,
//   one at a time, with an Undo button that runs the restore it was given; it goes away on its own, when asked, or when the window closes.
// Does not own: the restore itself (PresetDeleteUndo), the delete or the lists that ask for the message.
// Tests: PresetUndoTests.
namespace TabForge.Avalonia.Views.Presets;

/// <summary>The timed "Deleted ... Undo" message of one window.</summary>
public sealed class PresetUndoToast
{
    private const string UndoText = "Undo", ToastName = "PresetUndoToast", UndoName = "PresetUndoButton";

    private readonly Func<TopLevel?> _owner;
    private IUiTimer? _timer;
    private Border? _shown;
    private OverlayLayer? _layer;
    private Action? _undo;

    /// <param name="owner">The window the message appears over; null while it has none (nothing is shown).</param>
    public PresetUndoToast(Func<TopLevel?> owner) => _owner = owner;

    /// <summary>True while the message is up.</summary>
    public bool IsShowing => _shown is not null;
    /// <summary>The message text while it is up.</summary>
    public string Text { get; private set; } = "";

    /// <summary>Shows the message with an Undo button that runs <paramref name="undo"/>; an earlier message is replaced.</summary>
    public void Show(string text, Action undo)
    {
        Dismiss();
        Text = text;
        _undo = undo;
        if (_owner() is not { } top || OverlayLayer.GetOverlayLayer(top) is not { } layer) return;
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        label[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        var button = new Button { Content = UndoText, Name = UndoName, VerticalAlignment = VerticalAlignment.Center }.Spaced(DialogMetrics.UndoToastButtonGap);
        AutomationProperties.SetName(button, UndoText);
        button.Click += (_, _) => Undo();
        var toast = new Border
        {
            Name = ToastName, Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { label, button } },
        }.AsUndoToast();
        toast[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        toast[!Border.BorderBrushProperty] = new DynamicResourceExtension("ChromeBorder");
        AutomationProperties.SetName(toast, text);
        layer.Children.Add(toast);
        (_layer, _shown) = (layer, toast);
        toast.SizeChanged += (_, _) => Place();
        layer.SizeChanged += (_, _) => Place();
        _timer ??= NewTimer();
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Runs the restore and takes the message down; does nothing when no message is up.</summary>
    public void Undo()
    {
        var undo = _undo;
        Dismiss();
        undo?.Invoke();
    }

    // The overlay layer lays its children out like a canvas: the message is put at the bottom centre of the window by hand.
    private void Place()
    {
        if (_shown is not { } toast || _layer is not { } layer) return;
        Canvas.SetLeft(toast, Math.Max(0, (layer.Bounds.Width - toast.Bounds.Width) / 2));
        Canvas.SetTop(toast, Math.Max(0, layer.Bounds.Height - toast.Bounds.Height - DialogMetrics.UndoToastBottomGap));
    }

    /// <summary>Takes the message down without undoing.</summary>
    public void Dismiss()
    {
        _timer?.Stop();
        if (_shown is not null) _layer?.Children.Remove(_shown);
        (_shown, _layer, _undo) = (null, null, null);
    }

    private IUiTimer NewTimer()
    {
        var timer = AvaloniaUiThread.ForApplication().CreateTimer(TimeSpan.FromMilliseconds(UiTiming.PresetUndoMs));
        timer.Tick += (_, _) => Dismiss();
        return timer;
    }
}
