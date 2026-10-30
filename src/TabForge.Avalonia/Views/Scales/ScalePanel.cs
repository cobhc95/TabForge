using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Presenters;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Services;

namespace TabForge.Avalonia.Views.TransportPopups;

// Owns: the ONE master scale panel every host shows (status bar, fretboard, piano roll): the header and context line, the Auto / Manual badge, the detected
//     candidates as radio cards with a confidence bar, the inline Pick another list, READ FROM, TRACKS TO ANALYSE and SCALES TO CONSIDER, then the host's extra
//     sections and the muted sections that belong to other hosts. It shows the host readout and rebuilds when the host refreshes; every change goes to the host.
// Does not own: the popup look and opening (TransportPopupFrame), the detection and picks (the hosts), the settings file, the extras' content (the hosts).
// Tests: TabForge.Avalonia.Tests.StatusBarScaleTests, ScalePanelHostTests.
// Threads: UI thread only.
public sealed partial class ScalePanel
{
    public const string Title = "SCALE";
    private const string CloseTip = "Close scale";
    private const string ReadFromLabel = "READ FROM", ListenToLabel = "TRACKS TO ANALYSE", FamiliesLabel = "SCALES TO CONSIDER", DetectedLabel = "DETECTED";
    private const string PickAnotherTitle = "Pick another…", PickAnotherHint = "Any key, any scale in the library.";
    private const string PercussionNote = "Drums and percussion are never included.", ResetText = "Reset to defaults";
    private const string NoNotesText = "No notes yet: pick a scale to compose in";
    private const string AutoText = "Auto", ManualPrefix = "Manual: ", BackToAutoText = "Back to Auto";

    private static readonly IReadOnlyList<(string Value, string Text)> ReadFromBaseChoices = new (string Value, string Text)[]
    {
        (ScaleReadFrom.Section, "Auto: this section"), (ScaleReadFrom.Song, "Whole song"), (ScaleReadFrom.Key, "Song key only"),
    };

    private static readonly IReadOnlyList<(string Value, string Text)> ReadFromSelectionChoice = new (string Value, string Text)[]
    {
        (ScaleReadFrom.Selection, "Selection"),
    };

    private static readonly IReadOnlyList<(string Value, string Text)> ListenToChoices = new (string Value, string Text)[]
    {
        (ScaleListenTo.Track, "This track"), (ScaleListenTo.Pitched, "All pitched tracks"),
    };

    private readonly IScalePanelHost _host;
    private readonly TransportPopupFrame _frame;
    private readonly bool _wide;
    private bool _building, _pickAnother;   // _pickAnother: the user chose the pick-another card, so it stays selected even when its pick equals a detected scale

    public ScalePanel(IScalePanelHost host)
    {
        _host = host;
        _wide = host.TwoColumns;
        _frame = new(Title, CloseTip, _wide ? ScalePopupLayout.WidePanelSpan : ScalePopupLayout.PanelSpan);
        _host.Refreshed += Rebuild;
        _frame.Closed += () => _host.Refreshed -= Rebuild;
        Rebuild();
    }

    /// <summary>The popup frame (its panel and rows), for the opener and for tests.</summary>
    public TransportPopupFrame Frame => _frame;

    /// <summary>The shared section ids built last, in order (tests).</summary>
    public IReadOnlyList<string> SectionIds { get; private set; } = Array.Empty<string>();

    /// <summary>The titles of the host's extra sections built last (tests).</summary>
    public IReadOnlyList<string> ExtraTitles { get; private set; } = Array.Empty<string>();

    /// <summary>The titles of the greyed sections built last (tests).</summary>
    public IReadOnlyList<string> GreyedTitles { get; private set; } = Array.Empty<string>();

    /// <summary>Opens the panel above <paramref name="anchor"/>.</summary>
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
            _results = null;
            _searchBox = null;
            var result = _host.Current;
            if (result.Pick is null) _pickAnother = false;   // back to Auto from anywhere: the card is no longer the user's pick
            global::Avalonia.Controls.Controls left = rows, right = rows;
            if (_wide)
            {
                var leftColumn = new StackPanel().WithMargin(ScalePopupLayout.ColumnMargin);
                var rightColumn = new StackPanel();
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
                Grid.SetColumn(rightColumn, 1);
                grid.Children.Add(leftColumn);
                grid.Children.Add(rightColumn);
                rows.Add(grid);
                (left, right) = (leftColumn.Children, rightColumn.Children);
            }
            var ids = new List<string> { ScalePanelModel.HeaderId };
            left.Add(TransportPopupFrame.Helper(_host.ContextLine));
            AddDetected(left, result);
            ids.Add(ScalePanelModel.DetectedId);
            ids.Add(ScalePanelModel.PickAnotherId);
            var readChoices = _host.HasSelection ? ReadFromBaseChoices.Concat(ReadFromSelectionChoice).ToList() : ReadFromBaseChoices;
            var readCurrent = ScaleReadFrom.Normalize(_host.Settings.ReadFrom);
            if (readCurrent == ScaleReadFrom.Selection && !_host.HasSelection) readCurrent = ScaleReadFrom.Section;
            AddAll(left, ScalePanelParts.Segmented(ReadFromLabel, readChoices, readCurrent, "ReadFrom", value => { _host.Settings.ReadFrom = value; _host.SettingChanged(); }, () => _building));
            ids.Add(ScalePanelModel.ReadFromId);
            AddAll(left, ScalePanelParts.Segmented(ListenToLabel, ListenToChoices, ScaleListenTo.Normalize(_host.Settings.ListenTo), "ListenTo", value => { _host.Settings.ListenTo = value; _host.SettingChanged(); }, () => _building,
                new Dictionary<string, string> { [ScaleListenTo.Track] = "This track: only the track you are on", [ScaleListenTo.Pitched] = "All pitched tracks: guitars, bass, keys and other melodic tracks together" }));
            ids.Add(ScalePanelModel.ListenToId);
            left.Add(TransportPopupFrame.Helper(PercussionNote));
            AddFamilies(left);
            ids.Add(ScalePanelModel.FamiliesId);
            SectionIds = ids;
            AddHostSections(_wide ? right : left);
        }
        finally { _building = false; }
    }

    private static void AddAll(global::Avalonia.Controls.Controls rows, IEnumerable<Control> controls)
    {
        foreach (var control in controls) rows.Add(control);
    }

    private void AddDetected(global::Avalonia.Controls.Controls rows, ScaleReadoutResult result)
    {
        var header = new DockPanel();
        var manual = result.Pick is not null;
        var badge = new TextBlock { Name = "ScaleBadge", Text = manual ? ManualPrefix + result.Effective.Display(_host.Flats, _host.Settings.MusicalKeyNames) : AutoText, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(badge, manual ? "Scale is chosen by you" : "Scale is detected automatically");
        var back = new Button { Name = "ScaleBackToAuto", Content = BackToAutoText, IsVisible = manual }.WithMargin(ScalePopupLayout.BadgeLinkMargin);
        TextLinkButton.Apply(back);
        AutomationProperties.SetName(back, "Back to automatic scale detection");
        back.Click += (_, _) => { _pickAnother = false; _host.UseAuto(); };
        var badgeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { badge, back } };
        DockPanel.SetDock(badgeRow, Dock.Right);
        header.Children.Add(badgeRow);
        header.Children.Add(ScalePopupLayout.SectionLabel(DetectedLabel));
        rows.Add(header);
        var group = new RadioCardGroup("ScaleCandidates");
        var flats = _host.Flats;
        var musical = _host.Settings.MusicalKeyNames;
        var effective = result.Effective;
        var matched = result.Top.Select(m => !_pickAnother && m.Root == effective.Root && m.Scale == effective.Scale).ToList();
        for (var i = 0; i < result.Top.Count; i++)
        {
            var match = result.Top[i];
            var notes = string.Join(" ", ScaleFamilies.NotesOf(match.Root, match.Scale, flats, musical));
            var card = new RadioCard(group, "ScaleCandidate" + i, match.Display(flats, musical), $"{match.Confidence:P0}  ·  {notes}", matched[i], ScalePopupLayout.ConfidenceBar(match.Confidence));
            if (i > 0) card.CardSpacing();
            card.Radio.IsCheckedChanged += (_, _) => { if (!_building && card.IsSelected) { _pickAnother = false; _host.Pick(match); } };
            rows.Add(card);
        }
        if (result.Top.Count == 0) rows.Add(TransportPopupFrame.Helper(NoNotesText));
        AddPickAnother(rows, group, result, flats, musical, custom: !matched.Contains(true));   // a pick that is no candidate, or the pick-another card pressed
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
        void PickNow()
        {
            if (_building || key.SelectedIndex < 0 || scale.SelectedIndex < 0) return;
            _pickAnother = true;
            _host.Pick(new ScaleMatch(key.SelectedIndex, scaleNames[scale.SelectedIndex], 1, 0));
            Rebuild();   // opens the list under the card even when the pick did not change the readout
        }
        ScalePopupLayout.WirePickAnother(card, key, scale, PickNow);
        rows.Add(card);
        if (card.IsSelected) AddList(rows, result);   // the card opens the scale list under itself
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
        var reset = new Button { Name = "ScaleFamiliesReset", Content = ResetText, HorizontalAlignment = HorizontalAlignment.Left }.WithMargin(ScalePopupLayout.LinkRowMargin);
        TextLinkButton.Apply(reset);
        AutomationProperties.SetName(reset, "Reset the scales to consider to the defaults");
        reset.Click += (_, _) => { _host.Settings.ResetFamilies(); _host.SettingChanged(); Rebuild(); };
        rows.Add(reset);
    }
}
