using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Services;

namespace TabForge.Avalonia.Views.TransportPopups;

/// <summary>What the scale popup shows and changes; the status bar's scale readout implements it.</summary>
public interface IScalePopupHost
{
    /// <summary>The readout the popup shows now.</summary>
    ScaleReadoutResult Current { get; }

    /// <summary>The saved scale readout settings the popup edits.</summary>
    ScaleReadoutSettings Settings { get; }

    /// <summary>True when the next pick is kept in the song for its section and track, not only for this session.</summary>
    bool KeepChoice { get; set; }

    /// <summary>True when the song key is a flat key, so the popup spells roots with flats.</summary>
    bool Flats { get; }

    /// <summary>The user chose a scale (a detected card or the pick-another drop-downs).</summary>
    void Pick(ScaleMatch scale);

    /// <summary>Auto: forget the pick of this section and track and go back to detection.</summary>
    void UseAuto();

    /// <summary>A setting changed in the popup: save it and read the section again.</summary>
    void SettingChanged();

    /// <summary>Raised after the readout changed (a pick, a setting, the playhead entering another section), so an open popup shows it.</summary>
    event Action? Refreshed;
}

// Owns: the rows of the scale popup: the header and context line, the detected candidates as radio cards with a confidence bar, the pick-another card
//     with its key and scale drop-downs, READ FROM and LISTEN TO segmented buttons, the scale family chips, the options and the footnote.
//     It shows the host readout and rebuilds when the host refreshes; every change goes to the host.
// Does not own: the popup look and opening (TransportPopupFrame), the detection and picks (ScaleReadoutEngine, the host), the settings file.
// Tests: TabForge.Avalonia.Tests.StatusBarScaleTests.
// Threads: UI thread only.
public sealed class ScaleReadoutPopup
{
    public const string Title = "SCALE";
    private const string CloseTip = "Close scale";
    private const string ReadFromLabel = "READ FROM", ListenToLabel = "LISTEN TO", FamiliesLabel = "SCALES TO CONSIDER", OptionsLabel = "OPTIONS", DetectedLabel = "DETECTED";
    private const string PreferText = "Prefer common scales when it is close", FretboardText = "Show on the fretboard (scale highlight)", KeepText = "Keep my choice for this section";
    private const string Footnote = "Your pick sticks to this section and track; Auto returns to detection.";
    private const string PickAnotherTitle = "Pick another…", PickAnotherHint = "Any key, any scale in the library.";
    private const string NoNotesText = "No pitched notes to read here yet.";
    private const string AutoText = "Auto";
    private const int PickAnotherIndex = 3;

    private static readonly IReadOnlyList<(string Value, string Text)> ReadFromChoices = new (string Value, string Text)[]
    {
        (ScaleReadFrom.Section, "Auto: this section"), (ScaleReadFrom.Song, "Whole song"), (ScaleReadFrom.Key, "Song key only"),
    };

    private static readonly IReadOnlyList<(string Value, string Text)> ListenToChoices = new (string Value, string Text)[]
    {
        (ScaleListenTo.Track, "Selected track"), (ScaleListenTo.Pitched, "All pitched tracks"),
    };

    private readonly IScalePopupHost _host;
    private readonly TransportPopupFrame _frame = new(Title, CloseTip, ScalePopupLayout.PanelSpan);
    private bool _building;

    public ScaleReadoutPopup(IScalePopupHost host)
    {
        _host = host;
        _host.Refreshed += Rebuild;
        _frame.Closed += () => _host.Refreshed -= Rebuild;
        Rebuild();
    }

    /// <summary>The popup frame (its panel and rows), for the opener and for tests.</summary>
    public TransportPopupFrame Frame => _frame;

    /// <summary>Opens the popup above <paramref name="anchor"/>.</summary>
    public void OpenAbove(Control anchor)
    {
        _host.Refreshed -= Rebuild;
        _host.Refreshed += Rebuild;
        _frame.OpenAbove(anchor);
    }

    public void Close() => _frame.Close();

    private void Rebuild()
    {
        if (_building) return;
        _building = true;
        try
        {
            var rows = _frame.Rows.Children;
            while (rows.Count > 1) rows.RemoveAt(rows.Count - 1);   // the first row is the title
            var result = _host.Current;
            rows.Add(TransportPopupFrame.Helper(result.Context.Text));
            AddDetected(rows, result);
            AddSegmented(rows, ReadFromLabel, ReadFromChoices, ScaleReadFrom.Normalize(_host.Settings.ReadFrom), "ReadFrom", value => _host.Settings.ReadFrom = value);
            AddSegmented(rows, ListenToLabel, ListenToChoices, ScaleListenTo.Normalize(_host.Settings.ListenTo), "ListenTo", value => _host.Settings.ListenTo = value);
            AddFamilies(rows);
            AddOptions(rows);
            rows.Add(TransportPopupFrame.Helper(Footnote));
        }
        finally { _building = false; }
    }

    private void AddDetected(global::Avalonia.Controls.Controls rows, ScaleReadoutResult result)
    {
        var auto = new Button { Name = "ScaleAuto", Content = AutoText, IsEnabled = result.Pick is not null, HorizontalAlignment = HorizontalAlignment.Right };
        TextLinkButton.Apply(auto);
        AutomationProperties.SetName(auto, "Back to automatic scale detection");
        auto.Click += (_, _) => _host.UseAuto();
        var header = new DockPanel();
        DockPanel.SetDock(auto, Dock.Right);
        header.Children.Add(auto);
        header.Children.Add(ScalePopupLayout.SectionLabel(DetectedLabel));
        rows.Add(header);
        var group = new RadioCardGroup("ScaleCandidates");
        var flats = _host.Flats;
        var musical = _host.Settings.MusicalKeyNames;
        var effective = result.Effective;
        var matched = result.Top.Select(m => m.Root == effective.Root && m.Scale == effective.Scale).ToList();
        for (var i = 0; i < result.Top.Count; i++)
        {
            var match = result.Top[i];
            var notes = string.Join(" ", ScaleFamilies.NotesOf(match.Root, match.Scale, flats, musical));
            var card = new RadioCard(group, "ScaleCandidate" + i, match.Display(flats, musical), $"{match.Confidence:P0}  ·  {notes}", matched[i], ScalePopupLayout.ConfidenceBar(match.Confidence));
            if (i > 0) card.CardSpacing();
            card.Radio.IsCheckedChanged += (_, _) => { if (!_building && card.IsSelected) _host.Pick(match); };
            rows.Add(card);
        }
        if (result.Top.Count == 0) rows.Add(TransportPopupFrame.Helper(NoNotesText));
        AddPickAnother(rows, group, result, flats, musical, custom: !matched.Contains(true));
    }

    private void AddPickAnother(global::Avalonia.Controls.Controls rows, RadioCardGroup group, ScaleReadoutResult result, bool flats, bool musical, bool custom)
    {
        var keyNames = Enumerable.Range(0, 12).Select(pc => ScaleFamilies.NoteName(pc, musical && (flats || ScaleFamilies.PrefersFlats(pc, "Major")), musical)).ToList();
        var scaleNames = MusicTheoryService.Scales.Keys.ToList();
        var key = ScalePopupLayout.DropDown("ScalePickKey", keyNames, result.Effective.Root);
        var scale = ScalePopupLayout.DropDown("ScalePickScale", scaleNames, Math.Max(0, scaleNames.IndexOf(result.Effective.Scale)));
        AutomationProperties.SetName(key, "Key");
        AutomationProperties.SetName(scale, "Scale");
        var pickers = new StackPanel { Orientation = Orientation.Horizontal, Children = { key, scale } };
        var card = new RadioCard(group, "ScalePickAnother", PickAnotherTitle, PickAnotherHint, custom, pickers).CardSpacing();
        void PickNow() { if (!_building && key.SelectedIndex >= 0 && scale.SelectedIndex >= 0) _host.Pick(new ScaleMatch(key.SelectedIndex, scaleNames[scale.SelectedIndex], 1, 0)); }
        card.Radio.IsCheckedChanged += (_, _) => { if (card.IsSelected) PickNow(); };
        key.SelectionChanged += (_, _) => PickNow();
        scale.SelectionChanged += (_, _) => PickNow();
        rows.Add(card);
    }

    private void AddSegmented(global::Avalonia.Controls.Controls rows, string label, IReadOnlyList<(string Value, string Text)> choices, string current, string namePrefix, Action<string> store)
    {
        rows.Add(ScalePopupLayout.SectionLabel(label));
        var buttons = choices.Select(c => ScalePopupLayout.Segment(namePrefix + c.Value, c.Text, c.Value == current)).ToList();
        for (var i = 0; i < buttons.Count; i++)
        {
            var (value, button) = (choices[i].Value, buttons[i]);
            AutomationProperties.SetName(button, label + ": " + choices[i].Text);
            button.IsCheckedChanged += (_, _) =>
            {
                if (_building) return;
                if (button.IsChecked != true) { if (value == current) button.IsChecked = true; return; }   // a segment cannot be switched off by itself
                store(value);
                _host.SettingChanged();
            };
        }
        rows.Add(ScalePopupLayout.SegmentRow(buttons));
    }

    private void AddFamilies(global::Avalonia.Controls.Controls rows)
    {
        rows.Add(ScalePopupLayout.SectionLabel(FamiliesLabel));
        var on = ScaleFamilies.Normalize(_host.Settings.Families).ToHashSet();
        var wrap = new WrapPanel();
        foreach (var family in ScaleFamilies.All)
        {
            var name = family;
            var chip = ScalePopupLayout.Chip("Family" + name.Replace(" ", ""), name, on.Contains(name));
            AutomationProperties.SetName(chip, "Consider " + name.ToLowerInvariant() + " scales");
            chip.IsCheckedChanged += (_, _) =>
            {
                if (_building) return;
                var chosen = ScaleFamilies.Normalize(_host.Settings.Families).ToHashSet();
                if (chip.IsChecked == true) chosen.Add(name); else chosen.Remove(name);
                _host.Settings.Families = ScaleFamilies.All.Where(chosen.Contains).ToList();
                _host.SettingChanged();
            };
            wrap.Children.Add(chip);
        }
        rows.Add(wrap);
    }

    private void AddOptions(global::Avalonia.Controls.Controls rows)
    {
        rows.Add(ScalePopupLayout.SectionLabel(OptionsLabel));
        AddCheck(rows, "ScalePreferCommon", PreferText, _host.Settings.PreferCommon, v => { _host.Settings.PreferCommon = v; _host.SettingChanged(); });
        AddCheck(rows, "ScaleMusicalNames", "Use musical key names (E♭ instead of D#)", _host.Settings.MusicalKeyNames, v => { _host.Settings.MusicalKeyNames = v; _host.SettingChanged(); });
        AddCheck(rows, "ScaleOnFretboard", FretboardText, _host.Settings.ShowOnFretboard, v => { _host.Settings.ShowOnFretboard = v; _host.SettingChanged(); });
        AddCheck(rows, "ScaleKeepChoice", KeepText, _host.KeepChoice, v => _host.KeepChoice = v);
    }

    private void AddCheck(global::Avalonia.Controls.Controls rows, string name, string text, bool isChecked, Action<bool> store)
    {
        var box = new CheckBox { Name = name, Content = new TextBlock { Text = text, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap }, IsChecked = isChecked };
        AutomationProperties.SetName(box, text);
        box.IsCheckedChanged += (_, _) => { if (!_building) store(box.IsChecked == true); };
        rows.Add(box);
    }
}
