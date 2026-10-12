using System.Windows.Controls;
using TabForge.Models;

namespace TabForge.LiveInput;

// Owns: the live input button's right-click menu: open the chain, the "open the FX window when turned on" option, the audio input
//   rows (one at a time) and the MIDI device rows (one at a time, beside the audio one), the audio device settings.
// Does not own: what the rows do (LiveInputController), when the menu opens (LiveInputButton).
// Tests: TestLiveInput.
internal static class LiveInputMenu
{
    public const string OpenFxHeader = "Open live FX chain", AutoOpenHeader = "Open FX window when turned on", DeviceHeader = "Audio device settings…";

    /// <summary>Built at each opening, so the ticks show the current choices.</summary>
    public static ContextMenu Build(LiveInputController controller)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item(OpenFxHeader, controller.OpenFx));
        var auto = new MenuItem { Header = AutoOpenHeader, IsCheckable = true, IsChecked = controller.OpenFxOnTurnOn };
        auto.Click += (_, _) => controller.OpenFxOnTurnOn = auto.IsChecked;
        menu.Items.Add(auto);
        menu.Items.Add(new Separator());
        var pairsApart = AudioInputs.Names.Count > 4; var pairs = false;   // many inputs: the stereo pairs follow a separator
        foreach (var input in controller.Inputs)   // the device's names; a saved input the device lacks stays, marked and ticked
        {
            if (pairsApart && !pairs && AudioInputs.TryParse(input, out _, out var stereo) && stereo) { menu.Items.Add(new Separator()); pairs = true; }
            var row = new MenuItem { Header = AudioInputs.Label(input), IsCheckable = true, IsChecked = controller.Input == input };
            row.Click += (_, _) => controller.SetInput(input);
            menu.Items.Add(row);
        }
        menu.Items.Add(new Separator());
        foreach (var (label, device) in controller.MidiChoices())   // the devices connected at this opening
        {
            var row = new MenuItem { Header = label, IsCheckable = true, IsChecked = controller.MidiDevice == device };
            row.Click += (_, _) => controller.SetMidiInput(device);
            menu.Items.Add(row);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(DeviceHeader, controller.OpenAudioSettings));
        return menu;
    }

    private static MenuItem Item(string header, Action run)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => run();
        return item;
    }
}
