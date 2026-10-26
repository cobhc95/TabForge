using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the info line under the toolbar: how many notes are selected and their Start, End, Length, Pitch, Velocity and Muted, as numbers you can
//       type. One note shows its own values; several show a range ("C4 … G4"), and a typed start, pitch or velocity shifts them all by the same
//       amount while a typed length or mute sets them all. Each committed field (Enter or leaving it) is one undo step.
// Does not own: the text formats (PianoRollPositionText) or the edits (PianoRollEdits through PianoRollPresenter).
// Tests: TabForge.Avalonia.Tests.PianoRollViewTests.
public sealed partial class PianoRollView
{
    private const double FieldSize = 86, InfoGap = 10;

    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _start = Field("Start");
    private readonly TextBox _end = Field("End");
    private readonly TextBox _length = Field("Length");
    private readonly TextBox _pitch = Field("Pitch");
    private readonly TextBox _velocity = Field("Velocity");
    private readonly TextBox _channel = Field("Channel");
    private readonly StackPanel _channelRow = new() { Orientation = Orientation.Horizontal };
    private readonly CheckBox _muted = new() { Content = "Muted", VerticalAlignment = VerticalAlignment.Center };
    private bool _showing;

    private static TextBox Field(string name) => new() { Name = "PianoRoll" + name, MinWidth = FieldSize, VerticalAlignment = VerticalAlignment.Center };

    private Control BuildInfoLine()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = InfoGap, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_count);
        foreach (var (label, box) in new[] { ("Start", _start), ("End", _end), ("Length", _length), ("Pitch", _pitch), ("Velocity", _velocity), ("Channel", _channel) })
        {
            var caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            caption[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
            var field = ReferenceEquals(box, _channel) ? _channelRow : new StackPanel { Orientation = Orientation.Horizontal };
            field.Spacing = GroupPad;
            field.Children.Add(caption);
            field.Children.Add(box);
            row.Children.Add(field);
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Apply(box); e.Handled = true; } };
            box.LostFocus += (_, _) => Apply(box);
        }
        _muted.IsCheckedChanged += (_, _) =>
        {
            if (_showing || Presenter.Selected.Count == 0) return;
            var want = _muted.IsChecked == true;
            if (Presenter.Selected.All(i => Presenter.Notes[i].Muted == want)) return;
            Surface.Run(Presenter.CommitAsync(PianoRollEdits.SetMuted(Presenter.Notes, Presenter.Selected.ToList(), want), PianoRollEditKind.Expression, "Mute notes"));
        };
        row.Children.Add(_muted);
        var hint = new TextBlock { Text = "Typing a value with several notes selected shifts them all by the same amount.", VerticalAlignment = VerticalAlignment.Center };
        hint[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        row.Children.Add(hint);
        return new Border { Child = row }.WithPadding(GroupPad, GroupGap);
    }

    /// <summary>Shows the selection's values (empty fields with none selected).</summary>
    internal void RefreshInfo()
    {
        if (_stale) { Dispatch(); if (!_stale) return; }
        _showing = true;
        try
        {
            var notes = Presenter.Selected.Where(i => i < Presenter.Notes.Count).Select(i => Presenter.Notes[i]).ToList();
            _count.Text = notes.Count switch { 0 => "No notes selected", 1 => "1 note selected", var n => n.ToString(CultureInfo.InvariantCulture) + " notes selected" };
            _channelRow.IsVisible = !Presenter.IsTab;
            ToolTip.SetTip(_channel, "The notes' own MIDI channel (1-16), or Track for the track's channel");
            foreach (var box in new[] { _start, _end, _length, _pitch, _velocity, _channel }) box.IsEnabled = notes.Count > 0;
            _muted.IsEnabled = notes.Count > 0;
            if (notes.Count == 0)
            {
                foreach (var box in new[] { _start, _end, _length, _pitch, _velocity, _channel }) Show(box, "");
                _muted.IsChecked = false;
                return;
            }
            var map = Presenter.Map;
            Show(_start, PianoRollPositionText.Position(map, notes.Min(n => n.Start)));
            Show(_end, PianoRollPositionText.Position(map, notes.Max(n => n.End)));
            Show(_length, Range(notes.Select(n => PianoRollPositionText.Length(n.Length)).ToList()));
            var low = notes.Min(n => n.Pitch);
            var high = notes.Max(n => n.Pitch);
            Show(_pitch, low == high ? PianoRollPositionText.PitchName(low) : PianoRollPositionText.PitchName(low) + " … " + PianoRollPositionText.PitchName(high));
            var average = (int)Math.Round(notes.Average(n => n.Velocity));
            var vLow = notes.Min(n => n.Velocity);
            var vHigh = notes.Max(n => n.Velocity);
            Show(_velocity, vLow == vHigh ? vLow.ToString(CultureInfo.InvariantCulture) : string.Create(CultureInfo.InvariantCulture, $"{average} ({vLow}–{vHigh})"));
            _muted.IsChecked = notes.All(n => n.Muted) ? true : notes.Any(n => n.Muted) ? null : false;
            var channels = notes.Select(n => n.Channel).Distinct().ToList();
            Show(_channel, channels.Count > 1 ? "…" : channels[0] < 0 ? "Track" : (channels[0] + 1).ToString(CultureInfo.InvariantCulture));
        }
        finally { _showing = false; }
    }

    private static string Range(List<string> values) => values.Distinct().Count() == 1 ? values[0] : values.Min()! + " … " + values.Max()!;

    // The text a field shows, kept so leaving it unchanged commits nothing.
    private readonly Dictionary<TextBox, string> _shown = new();

    private void Show(TextBox box, string text)
    {
        box.Text = text;
        _shown[box] = text;
        box.FontStyle = text.Contains('…') || text.Contains('(') ? global::Avalonia.Media.FontStyle.Italic : global::Avalonia.Media.FontStyle.Normal;   // mixed values show in italics
    }

    /// <summary>A field was committed: its typed value applies to the selection as one step; a value that does not read restores the field.</summary>
    internal void Apply(TextBox box)
    {
        if (_showing || Presenter.Selected.Count == 0) return;
        var text = box.Text ?? "";
        if (_shown.TryGetValue(box, out var shown) && shown == text) return;
        var edit = Edit(box, text);
        if (edit is null) { RefreshInfo(); return; }
        Surface.Run(edit);
    }

    private Task<bool>? Edit(TextBox box, string text)
    {
        var notes = Presenter.Notes;
        var picked = Presenter.Selected.ToList();
        // A typed +n / -n moves by that much (Start, End, Length in beats.16ths; Pitch in semitones; Velocity in steps).
        if (PianoRollPositionText.ParseDelta(text, beats: ReferenceEquals(box, _start) || ReferenceEquals(box, _end) || ReferenceEquals(box, _length)) is { } delta)
        {
            if (ReferenceEquals(box, _start)) return Presenter.MoveAsync(delta, 0);
            if (ReferenceEquals(box, _end) || ReferenceEquals(box, _length)) return Presenter.ResizeAsync(delta);
            if (ReferenceEquals(box, _pitch)) return Presenter.TransposeAsync((int)Math.Round(delta));
            if (ReferenceEquals(box, _velocity)) return Presenter.CommitAsync(PianoRollEdits.ShiftVelocity(notes, picked, (int)Math.Round(delta)), PianoRollEditKind.Expression, "Set velocity");
        }
        if (ReferenceEquals(box, _start) && PianoRollPositionText.ParsePosition(Presenter.Map, text) is { } start)
            return Presenter.MoveAsync(start - picked.Min(i => notes[i].Start), 0);
        if (ReferenceEquals(box, _end) && PianoRollPositionText.ParsePosition(Presenter.Map, text) is { } end)
            return Presenter.ResizeAsync(end - picked.Max(i => notes[i].End));
        if (ReferenceEquals(box, _length) && PianoRollPositionText.ParseLength(text) is { } length)
            return Presenter.CommitAsync(PianoRollEdits.SetLength(notes, picked, length), PianoRollEditKind.Notes, "Set length");
        if (ReferenceEquals(box, _pitch) && PianoRollPositionText.ParsePitch(text.Split('…')[0]) is { } pitch)
            return Presenter.TransposeAsync(pitch - picked.Min(i => notes[i].Pitch));
        if (ReferenceEquals(box, _velocity) && int.TryParse(text.Split('(')[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var velocity))
        {
            var average = (int)Math.Round(picked.Average(i => notes[i].Velocity));
            return picked.Count == 1 ? Presenter.SetVelocityAsync(velocity)
                : Presenter.CommitAsync(PianoRollEdits.ShiftVelocity(notes, picked, velocity - average), PianoRollEditKind.Expression, "Set velocity");
        }
        if (ReferenceEquals(box, _channel))
        {
            if (text.Trim().Length == 0 || text.Trim().Equals("Track", StringComparison.OrdinalIgnoreCase)) return Presenter.SetChannelAsync(-1);
            if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var channel) && channel is >= 1 and <= 16) return Presenter.SetChannelAsync(channel - 1);
        }
        return null;
    }
}
