using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using TabForge.Models;

namespace TabForge.Avalonia.Features.Recording.View;

/// <summary>
/// Owns: the rows of a track's input picker (a ComboBox of AudioInputChoice: label shown, id selected) and keeping them current while
/// the picker is on screen (the device reports its inputs, or the shown inputs change).
/// Does not own: the list (AudioInputs), writing the chosen input to the track (the picker's owner, on a user selection).
/// Tests: TabForge.Avalonia.Tests.ArmedRowTests.
/// </summary>
public static class AudioInputPicker
{
    /// <summary>The narrowest row of the list: the track header's box is narrower, and a long input name still reads in the drop-down.</summary>
    private const double ListRowMinWidth = 200;

    /// <param name="selected">The id the picker shows now (the track's input); it always stays in the list.</param>
    public static ComboBox Bind(ComboBox picker, Func<string> selected)
    {
        // The closed box shows the same label as the rows: its template fills the selection box from the display member only.
        picker.DisplayMemberBinding = new Binding(nameof(AudioInputChoice.Label));
        // A narrow box (the track header) still gets a readable list: rows are at least this wide and show the whole name on hover.
        picker.Styles.Add(new Style(selector => selector.OfType<ComboBoxItem>())
        {
            Setters =
            {
                new Setter(Layoutable.MinWidthProperty, ListRowMinWidth),
                new Setter(ToolTip.TipProperty, new Binding(nameof(AudioInputChoice.Label))),
            },
        });
        Refresh(picker, selected());
        Action refresh = () => Refresh(picker, selected());
        // The input list changes on the engine's thread; the rows are rebuilt on the UI thread.
        var ui = TabForge.Avalonia.Platform.AvaloniaUiThread.ForApplication();
        Action changed = () => ui.Post(refresh);
        // Subscribed only while the picker is on screen, so a closed header or dialog is not kept alive by the list.
        picker.AttachedToVisualTree += (_, _) => { AudioInputs.Changed -= changed; AudioInputs.Changed += changed; refresh(); };
        picker.DetachedFromVisualTree += (_, _) => AudioInputs.Changed -= changed;
        return picker;
    }

    /// <summary>Rebuilds the rows and selects the same id again.</summary>
    public static void Refresh(ComboBox picker, string selected)
    {
        var choices = AudioInputs.Choices(selected).ToList();
        var index = choices.FindIndex(choice => choice.Id == selected);
        if (index < 0)
        {
            // The device list no longer holds the track's input: keep it as a row of its own, so it can be chosen again, and select by
            // index: a missing id used to leave the closed box blank.
            choices.Add(new AudioInputChoice(selected, AudioInputs.Label(selected)));
            index = choices.Count - 1;
        }
        picker.ItemsSource = choices;
        picker.SelectedIndex = index;
    }

    /// <summary>A small window with one picker on the current inputs, for the preview, which has no track.</summary>
    public static Window SampleWindow()
    {
        var window = new Window { Title = "Track input", Width = 320, SizeToContent = SizeToContent.Height, CanResize = false, ShowInTaskbar = false };
        window[!Window.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        window[!Window.ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        window.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children = { new TextBlock { Text = "Input", Margin = new Thickness(0, 0, 0, 4) }, Bind(new ComboBox { MinWidth = 200 }, () => AudioInputs.Input1) },
        };
        return window;
    }
}
