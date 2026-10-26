using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Models;
using TabForge.Platform;

namespace TabForge.Avalonia.Features.Recording.View;

/// <summary>
/// Owns: a track's input picker: a button showing the track's inputs and a drop-down with two sections, "Audio input" (exactly one, radio rows) and
/// "MIDI inputs" (one checkbox row per MIDI input device, any number ticked), kept current while the picker is on screen: audio changes arrive with
/// <see cref="AudioInputs.Changed"/>, MIDI devices are listed by a cheap poll on a worker thread (never on the UI thread).
/// Does not own: the lists (AudioInputs, the MIDI driver behind the owner's device lister), writing the choice to the track (the owner's callbacks).
/// Tests: TabForge.Avalonia.Tests.ArmedRowTests.
/// </summary>
public sealed class TrackInputPicker : Button
{
    /// <summary>The narrowest drop-down: a long input name reads whole, whatever the width of the button.</summary>
    private const double ListMinimum = 260;
    private const double ListMaximum = 360;
    private static readonly TimeSpan MidiPollInterval = TimeSpan.FromSeconds(2);
    private const string Tip = "Track inputs: one audio input, and any MIDI input devices (merged into the track while armed)";

    private readonly Func<string> _audio;
    private readonly Func<IReadOnlyList<string>> _midi;
    private readonly Action<string> _setAudio;
    private readonly Action<IReadOnlyList<string>> _setMidi;
    private readonly TextBlock _text = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel _rows = new() { MinWidth = ListMinimum };
    private readonly Flyout _flyout;
    private readonly string _group = "audio-" + Guid.NewGuid().ToString("N");
    private readonly IUiThread _ui;
    private readonly Func<IReadOnlyList<string>> _listMidiDevices;
    private readonly IUiTimer _poll;
    private IReadOnlyList<string> _devices = Array.Empty<string>();   // the MIDI input devices present at the last poll
    private bool _listing;   // a poll is running on a worker
    private string _built = "";

    /// <param name="ui">The UI thread (the device poll's timer and its result).</param>
    /// <param name="listMidiDevices">Lists the MIDI input devices present now; called on a worker, never throws.</param>
    public TrackInputPicker(Func<string> audio, Func<IReadOnlyList<string>> midi, Action<string> setAudio, Action<IReadOnlyList<string>> setMidi,
        IUiThread ui, Func<IReadOnlyList<string>> listMidiDevices)
    {
        (_audio, _midi, _setAudio, _setMidi) = (audio, midi, setAudio, setMidi);
        (_ui, _listMidiDevices) = (ui, listMidiDevices);
        _poll = ui.CreateTimer(MidiPollInterval);
        // The app's plain button look ("Button"): themed border and background, hover and pressed surfaces, focus ring, light and dark; the sizes stay this control's own.
        Classes.Add("Button");
        this.WithNoMargin();   // the canvas places it; the look's button margin does not apply
        HorizontalContentAlignment = HorizontalAlignment.Left;
        VerticalContentAlignment = VerticalAlignment.Center;
        Content = _text;
        ToolTip.SetTip(this, Tip);
        _flyout = new Flyout { Content = new ScrollViewer { MaxHeight = ListMaximum, Content = _rows } };
        // The tooltip would sit on top of the open list: it is off while the list is open.
        _flyout.Opened += (_, _) => { ToolTip.SetTip(this, null); Refresh(); };
        _flyout.Closed += (_, _) => ToolTip.SetTip(this, Tip);
        Flyout = _flyout;
        _poll.Tick += (_, _) => PollMidi();
        Action changed = () => _ui.Post(Refresh);
        AttachedToVisualTree += (_, _) => { AudioInputs.Changed -= changed; AudioInputs.Changed += changed; _poll.Start(); PollMidi(); Refresh(); };
        DetachedFromVisualTree += (_, _) => { AudioInputs.Changed -= changed; _poll.Stop(); };
        Refresh();
    }

    /// <summary>Rebuilds the text and the rows when the track's inputs or the device lists changed (no-op otherwise).</summary>
    public void Refresh()
    {
        var audio = _audio();
        var midi = _midi();
        var audioIds = AudioInputs.For(AudioInputUse.Recording, AudioInputs.IsValid(audio) && !AudioInputs.IsMidi(audio) ? audio : null);
        var devices = _devices.Concat(midi.Where(name => !_devices.Contains(name))).ToList();   // a ticked device that is unplugged stays, to be unticked
        var key = string.Join("|", audioIds.Select(AudioInputs.Label)) + "#" + audio + "#" + string.Join("|", midi) + "#" + string.Join("|", devices);
        _text.Text = Summary(audio, midi) + " \u25BE";
        if (key == _built) return;
        _built = key;
        _rows.Children.Clear();
        _rows.Children.Add(Header("Audio input"));
        foreach (var id in audioIds)
        {
            var row = new RadioButton { GroupName = _group, Content = AudioInputs.Label(id), IsChecked = id == audio };
            var chosen = id;
            row.Click += (_, _) => { _setAudio(chosen); Refresh(); };
            _rows.Children.Add(row);
        }
        _rows.Children.Add(Header("MIDI inputs"));
        if (devices.Count == 0) _rows.Children.Add(new TextBlock { Text = "No MIDI input devices found", Opacity = 0.7 });
        foreach (var name in devices)
        {
            var present = _devices.Contains(name);
            var row = new CheckBox { Content = present ? name : name + AudioInputs.NotAvailable, IsChecked = midi.Contains(name) };
            var device = name;
            row.Click += (_, _) =>
            {
                var next = _midi().Where(n => n != device).ToList();
                if (row.IsChecked == true) next.Add(device);
                _setMidi(next);
                Refresh();
            };
            _rows.Children.Add(row);
        }
    }

    private static string Summary(string audio, IReadOnlyList<string> midi)
    {
        var first = AudioInputs.IsMidi(audio) ? AudioInputs.Midi : audio.Length == 0 ? "No input" : AudioInputs.Label(audio);
        return midi.Count == 0 ? first : $"{first} + {midi.Count} MIDI";
    }

    private static TextBlock Header(string text)
    {
        var header = new TextBlock { Text = text, FontWeight = FontWeight.SemiBold };
        header[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        return header;
    }

    // The device list is read on a worker (the driver call can be slow) and applied on the UI thread only when it changed.
    private void PollMidi()
    {
        if (_listing) return;
        _listing = true;
        var ui = _ui;
        var list = _listMidiDevices;
        Task.Run(() =>
        {
            var names = list();
            ui.Post(() =>
            {
                _listing = false;
                if (names.SequenceEqual(_devices)) return;
                _devices = names;
                Refresh();
            });
        });
    }
}
