using System.Windows;
using System.Windows.Controls;
using TabForge.Models;

namespace TabForge.Views;

// Owns: the rows of a track's input picker (a ComboBox of AudioInputChoice: label shown, id selected) and keeping them
//       current while the picker is on screen (the device reports its inputs, or the shown inputs change).
// Does not own: the list (AudioInputs), writing the chosen input to the track (the picker's owner, on a user selection).
// Tests: TestAudioInputChannels (a rebuild selects the same id and writes nothing).
internal static class AudioInputPicker
{
    /// <param name="selected">The id the picker shows now (the track's input); it always stays in the list.</param>
    public static ComboBox Bind(ComboBox picker, Func<string> selected)
    {
        picker.DisplayMemberPath = nameof(AudioInputChoice.Label);
        picker.SelectedValuePath = nameof(AudioInputChoice.Id);
        // A narrow box (the track header) still gets a readable list: rows are at least this wide and show the whole name on hover.
        var row = new Style(typeof(ComboBoxItem), Application.Current?.TryFindResource(typeof(ComboBoxItem)) as Style);
        row.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 200.0));
        row.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding(nameof(AudioInputChoice.Label))));
        picker.ItemContainerStyle = row;
        Refresh(picker, selected());
        Action refresh = () => Refresh(picker, selected());
        // Subscribed only while the picker is on screen, so a closed header or dialog is not kept alive by the list.
        picker.Loaded += (_, _) => { AudioInputs.Changed -= refresh; AudioInputs.Changed += refresh; refresh(); };
        picker.Unloaded += (_, _) => AudioInputs.Changed -= refresh;
        return picker;
    }

    /// <summary>Rebuilds the rows and selects the same id again. The selection passes through "none" and back to the same id, which an owner's change handler ignores.</summary>
    internal static void Refresh(ComboBox picker, string selected)
    {
        picker.ItemsSource = AudioInputs.Choices(selected);
        picker.SelectedValue = selected;
    }
}
