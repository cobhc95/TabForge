using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TabForge.Models;

namespace TabForge.Views;

// TrackPropertiesDialog: the audio-track variant. An audio track has no instrument, tuning, MIDI sound or capo: the window shows
// name, colour, notes, the recording input and the mix (volume, pan, FX) only.
internal sealed partial class TrackPropertiesDialog
{
    private ComboBox _audioInput = null!;

    /// <summary>The right column: the instrument cards, or for an audio track (no instrument, drum or tuning cards) its input and mix only.</summary>
    private StackPanel BuildRightColumn(Border instrumentCard, Border drumCard, Border mixerCard)
    {
        var right = new StackPanel();
        if (_track.IsAudio) { right.Children.Add(BuildAudioInputCard()); right.Children.Add(mixerCard); return right; }
        right.Children.Add(instrumentCard);
        right.Children.Add(drumCard);
        right.Children.Add(mixerCard);
        right.Children.Add(_tuningCard);
        return right;
    }

    /// <summary>The recording input (Input 1, Input 2, stereo, MIDI).</summary>
    private Border BuildAudioInputCard()
    {
        _audioInput = new ComboBox { ItemsSource = AudioInputs.All, SelectedItem = _track.AudioInput, MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
        System.Windows.Automation.AutomationProperties.SetName(_audioInput, "Recording input");
        var stack = new StackPanel();
        stack.Children.Add(_audioInput);
        stack.Children.Add(Hint("The input recorded and monitored when this track is armed. An audio track holds audio and MIDI clips and has no notation."));
        return Card("Input", stack);
    }

    /// <summary>The audio track's picture: its waveform in the track colour's gradient.</summary>
    private void DrawAudioPicture()
    {
        var icon = AudioTrackIcon.Element(120);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        _picture.Child = icon;
        _pictureCaption.Text = "Audio track";
    }

    /// <summary>OK on an audio track: the identity, input and mix only (nothing of the instrument side exists).</summary>
    private void ApplyAudioTrack()
    {
        var track = _track;
        track.Name = string.IsNullOrWhiteSpace(_name.Text) ? track.Name : _name.Text.Trim();
        track.TrackNotes = _notes.Text;
        track.ColorHex = TabForge.Visualization.ColourText.Hex(_colour);
        track.TintRow = _tint.IsChecked == true;
        if (_audioInput.SelectedItem is string input) track.AudioInput = input;
        track.Volume = (int)_volume.Value;
        track.Pan = (int)_pan.Value;
    }
}
