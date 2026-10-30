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
/// Owns: a track's input picker: a button showing the track's inputs and a drop-down with two sections, "Audio input" (None, then the inputs) and
/// "MIDI input" (None, All MIDI inputs, then each device); both are radio rows, one choice each, kept current while the picker is on screen: audio changes arrive with
/// <see cref="AudioInputs.Changed"/>, MIDI devices are listed by a cheap poll on a worker thread (never on the UI thread).
/// Does not own: the lists (AudioInputs, the MIDI driver behind the owner's device lister), writing the choice to the track (the owner's callbacks).
/// Tests: TabForge.Avalonia.Tests.ArmedRowTests.
/// </summary>
public sealed class TrackInputPicker : Button
{
    /// <summary>A subclass has its own style key and would get no Button theme (invisible): use the stock Button theme.</summary>
    protected override Type StyleKeyOverride => typeof(Button);

    /// <summary>The narrowest drop-down: a long input name reads whole, whatever the width of the button.</summary>
    private const double ListMinimum = 260;
    private const double ListMaximum = 360;
    private static readonly TimeSpan MidiPollInterval = TimeSpan.FromSeconds(2);
    private const double PillInsetLeft = 10, PillInsetRight = 9;
    private const string Tip = "Track inputs: one audio input (or none) and one MIDI input (or none)";

    private readonly Func<string> _audio;
    private readonly Func<IReadOnlyList<string>> _midi;
    private readonly Action<string> _setAudio;
    private readonly Action<IReadOnlyList<string>> _setMidi;
    private readonly TextBlock _text = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel _rows = new() { MinWidth = ListMinimum };
    private readonly Flyout _flyout;
    private readonly string _audioGroup = "audio-" + Guid.NewGuid().ToString("N");
    private readonly string _midiGroup = "midi-" + Guid.NewGuid().ToString("N");
    private readonly IUiThread _ui;
    private readonly Func<IReadOnlyList<string>> _listMidiDevices;
    private readonly IUiTimer _poll;
    private IReadOnlyList<string> _devices = Array.Empty<string>();   // the MIDI input devices present at the last poll
    private bool _listing;   // a poll is running on a worker
    private string _built = "";
    private bool _arrowInText = true;   // the plain button look ends its text with a down arrow; the armed pill draws its own chevron

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

    /// <summary>The summary of the track inputs the button shows now ("MIDI: All", "Input 1 + Keys", ...), without the arrow.</summary>
    public string Label
    {
        get
        {
            var (audio, midi) = Normalise(_audio(), _midi());
            return Summary(audio, midi);
        }
    }

    /// <summary>
    /// Switches to the armed pill look: the label then <paramref name="chevron"/> in one row (the chevron is drawn by the owner), inset from the
    /// pill ends, with none of the plain button look (the owner gives the pill styles and its own template).
    /// </summary>
    public void UsePill(Control chevron)
    {
        _arrowInText = false;
        Classes.Remove("Button");
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Content = null;   // the label leaves the button first: a control has one logical parent
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }.WithMargin(PillInsetLeft, 0, PillInsetRight, 0);
        _text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(chevron, 1);
        row.Children.Add(_text);
        row.Children.Add(chevron);
        Content = row;
        Refresh();
    }

    /// <summary>Rebuilds the text and the rows when the track's inputs or the device lists changed (no-op otherwise).</summary>
    public void Refresh()
    {
        var (audio, midi) = Normalise(_audio(), _midi());
        var audioIds = AudioInputs.For(AudioInputUse.Recording, AudioInputs.HasAudio(audio) ? audio : null);
        var devices = (midi.Length > 0 && midi != AudioInputs.AllMidi && !_devices.Contains(midi) ? _devices.Append(midi) : _devices).ToList();   // a chosen device that is unplugged stays, to be unchosen
        var key = string.Join("|", audioIds.Select(AudioInputs.Label)) + "#" + audio + "#" + midi + "#" + string.Join("|", devices);
        _text.Text = Summary(audio, midi) + (_arrowInText ? " ▾" : "");
        if (key == _built) return;
        _built = key;
        _rows.Children.Clear();
        _rows.Children.Add(Header("Audio input"));
        AddRow(_audioGroup, "None", audio.Length == 0, () => _setAudio(""));
        foreach (var id in audioIds)
        {
            var chosen = id;
            AddRow(_audioGroup, AudioInputs.Label(id), id == audio, () => _setAudio(chosen));
        }
        _rows.Children.Add(Header("MIDI input"));
        AddRow(_midiGroup, "None", midi.Length == 0, () => _setMidi(Array.Empty<string>()));
        AddRow(_midiGroup, "All MIDI inputs", midi == AudioInputs.AllMidi, () => _setMidi(new[] { AudioInputs.AllMidi }));
        foreach (var name in devices)
        {
            string device = name;
            AddRow(_midiGroup, _devices.Contains(name) ? name : name + AudioInputs.NotAvailable, midi == name, () => _setMidi(new[] { device }));
        }
        if (devices.Count == 0) _rows.Children.Add(new TextBlock { Text = "No MIDI input devices found", Opacity = 0.7 });
    }

    private void AddRow(string group, string text, bool chosen, Action pick)
    {
        var row = new RadioButton { GroupName = group, Content = text, IsChecked = chosen };
        row.Click += (_, _) => { pick(); Refresh(); };
        _rows.Children.Add(row);
    }

    /// <summary>The track's choices as one audio id ("" = None) and one MIDI choice ("" = None, <see cref="AudioInputs.AllMidi"/>, or a device name); the older stored forms are read as their new meaning.</summary>
    public static (string Audio, string Midi) Normalise(string audio, IReadOnlyList<string> midi)
    {
        var legacy = AudioInputs.IsMidi(audio);
        var choice = midi.Count > 1 || midi.Contains(AudioInputs.AllMidi) || (midi.Count == 0 && legacy) ? AudioInputs.AllMidi : midi.Count == 1 ? midi[0] : "";
        return (legacy || !AudioInputs.HasAudio(audio) ? "" : audio, choice);
    }

    /// <summary>The button's text: "Input 1 + Keys", "Input 1", "MIDI: Keys", "MIDI: All" or "No input".</summary>
    public static string Summary(string audio, string midi)
    {
        var midiName = midi == AudioInputs.AllMidi ? "All MIDI" : midi;
        if (audio.Length == 0) return midi.Length == 0 ? "No input" : "MIDI: " + (midi == AudioInputs.AllMidi ? "All" : midi);
        return midi.Length == 0 ? AudioInputs.Label(audio) : AudioInputs.Label(audio) + " + " + midiName;
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
