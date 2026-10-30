using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Services;

namespace TabForge.Avalonia.Views.TransportPopups;

// Owns: the scale list under the selected Pick another card: the "Find a scale…" box that filters as you type, the grouped result list ("Best matches" first, then
//   family headers, the starred Favourites first; name, notes, the confidence the detected cards show, the fit as a short text and a star to favourite the scale type) and the keyboard (type to filter, Up/Down, Enter picks).
//   Choosing a scale is a manual pick and the list stays open.
// Does not own: the filter itself (ScaleSearch in Core), the ranking (SectionScaleDetector), the pick (the host), the rest of the popup (ScalePanel).
// Tests: TabForge.Avalonia.Tests.ScaleSearchPopupTests, PickAnotherListTests.
// Threads: UI thread only. The list is filtered over a cached list of every scale, so a key press is a few hundred string checks.
public sealed partial class ScalePanel
{
    private const string SearchHint = "Find a scale…", NoMatchText = "No scale matches “{0}”.", BestMatchesText = "Best matches";
    private const string ColumnTip = "The percentage is how likely this is the key (the same number as the detected cards). Fit is the share of the notes that belong to the scale.";
    private const int BestCount = 5;

    /// <summary>One line of the list: a header (<see cref="HitIndex"/> -1) or a scale. The text is what the row shows; the star toggles the scale type as a favourite.</summary>
    internal sealed record ScaleRow(string Text, int HitIndex, string Scale = "", bool Favourite = false)
    {
        public override string ToString() => Text;
    }

    private string _query = "";
    private ScaleSearch? _search;
    private IReadOnlyList<ScaleSearchHit> _hits = Array.Empty<ScaleSearchHit>();
    private List<int> _rowToHit = new();
    private PitchHistogram? _histogram;
    private Dictionary<(int, string), double> _confidence = new();
    private ListBox? _results;
    private TextBlock? _count;
    private TextBox? _searchBox;

    /// <summary>The scale list the search is open on, each library entry once (tests).</summary>
    internal IReadOnlyList<ScaleSearchHit> Hits => _hits;

    /// <summary>The "Best matches" scales (top detected over every ticked family, best first) of the open list (tests).</summary>
    internal IReadOnlyList<string> BestMatches { get; private set; } = Array.Empty<string>();

    private void AddList(global::Avalonia.Controls.Controls rows, ScaleReadoutResult result)
    {
        _search ??= new ScaleSearch();
        _histogram = _host.Histogram;
        var box = new TextBox { Name = "ScaleSearchBox", Watermark = SearchHint, Text = _query }.WithMargin(ScalePopupLayout.LinkRowMargin);
        AutomationProperties.SetName(box, SearchHint);
        _searchBox = box;
        _count = TransportPopupFrame.Helper("");
        ToolTip.SetTip(_count, ColumnTip);
        _results = new ListBox { Name = "ScaleSearchResults", MaxHeight = ScalePopupLayout.ListHeight, SelectionMode = SelectionMode.Single, ItemTemplate = new FuncDataTemplate<ScaleRow>((row, _) => RowView(row)) };
        AutomationProperties.SetName(_results, "Scales");
        ToolTip.SetTip(_results, ColumnTip);
        box.TextChanged += (_, _) => { if (_searchBox != box || _results is null) return; _query = box.Text ?? ""; Filter(); };
        box.KeyDown += (_, e) => OnSearchKey(e);
        _results.Tapped += (_, _) => PickSelected();
        rows.Add(box);
        rows.Add(_count);
        rows.Add(_results);
        Filter();
    }

    private void OnSearchKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: Step(1); break;
            case Key.Up: Step(-1); break;
            case Key.Enter: PickSelected(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Step(int by)
    {
        if (_results is null || _hits.Count == 0 || _rowToHit.Count == 0) return;
        var index = _results.SelectedIndex;
        if (index < 0) index = by > 0 ? -1 : _rowToHit.Count;
        for (var guard = 0; guard < _rowToHit.Count; guard++)
        {
            index = Math.Clamp(index + by, 0, _rowToHit.Count - 1);
            if (_rowToHit[index] >= 0) break;
        }
        if (_rowToHit[index] < 0) return;
        _results.SelectedIndex = index;
        _results.ScrollIntoView(_results.SelectedIndex);
    }

    /// <summary>Filters the cached list by the box text (also called by tests after setting the query).</summary>
    internal void SetQuery(string query)
    {
        _query = query;
        if (_results is null) return;
        Filter();
        if (_searchBox is not null && _searchBox.Text != query) _searchBox.Text = query;
    }

    private void Filter()
    {
        _search ??= new ScaleSearch();
        var found = _search.Find(_query);
        var blank = string.IsNullOrWhiteSpace(_query);
        var families = ScaleFamilies.Normalize(_host.Settings.Families);
        var favourites = _host.Settings.Favourites.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var favouritesOn = families.Contains(ScaleFamilies.Favourites);
        if (blank)
        {
            var considered = _host.Settings.ConsideredScales().ToHashSet(StringComparer.OrdinalIgnoreCase);
            found = found.Where(h => considered.Contains(h.Scale)).ToList();
        }
        string GroupOf(ScaleSearchHit h) => favouritesOn && favourites.Contains(h.Scale) ? ScaleFamilies.Favourites : ScaleFamilies.FamilyOf(h.Scale);
        // The same ranking the detected cards use, so the numbers agree; scales of unticked families (found by searching) have no confidence.
        var ranked = _histogram is { NoteCount: > 0 } ? SectionScaleDetector.Rank(_histogram, _host.Settings.ConsideredScales(), _host.Settings.PreferCommon) : Array.Empty<ScaleMatch>();
        _confidence = ranked.ToDictionary(m => (m.Root, m.Scale), m => m.Confidence);
        var order = MusicTheoryService.Scales.Keys.ToList();
        var familyOrder = ScaleFamilies.All.Where(f => f != ScaleFamilies.Favourites).Prepend(ScaleFamilies.Favourites).ToList();
        _hits = found
            .OrderBy(h => familyOrder.IndexOf(GroupOf(h)))
            .ThenByDescending(h => _confidence.TryGetValue((h.Root, h.Scale), out var c) ? c : -1)
            .ThenBy(h => order.IndexOf(h.Scale))
            .ThenBy(h => h.Root)
            .ToList();
        var flats = _host.Flats;
        var musical = _host.Settings.MusicalKeyNames;
        var rows = new List<ScaleRow>();
        _rowToHit = new List<int>();
        var best = new List<string>();
        if (blank && ranked.Count > 0)
        {
            rows.Add(new ScaleRow(BestMatchesText, -1));
            _rowToHit.Add(-1);
            foreach (var match in ranked.Take(BestCount))
            {
                var index = _hits.ToList().FindIndex(h => h.Root == match.Root && h.Scale == match.Scale);
                if (index < 0) continue;
                best.Add(_hits[index].Highlight);
                rows.Add(new ScaleRow(RowText(_hits[index], flats, musical), index, _hits[index].Scale, favourites.Contains(_hits[index].Scale)));
                _rowToHit.Add(index);
            }
        }
        BestMatches = best;
        string? lastFamily = null;
        for (var i = 0; i < _hits.Count; i++)
        {
            var family = GroupOf(_hits[i]);
            if (family != lastFamily)
            {
                lastFamily = family;
                rows.Add(new ScaleRow(family, -1));
                _rowToHit.Add(-1);
            }
            rows.Add(new ScaleRow(RowText(_hits[i], flats, musical), i, _hits[i].Scale, favourites.Contains(_hits[i].Scale)));
            _rowToHit.Add(i);
        }
        _results!.ItemsSource = rows;
        _results.SelectedIndex = _rowToHit.FindIndex(r => r >= 0);
        _count!.Text = _hits.Count == 0 ? string.Format(NoMatchText, _query.Trim()) : ScaleSearch.CountText(_hits.Count) + (ranked.Count > 0 ? "  ·  % = how likely this is the key" : "");
    }

    // A header is a bold line; a scale row is its text with a star at the right. The star marks the scale type (all 12 keys) as a favourite.
    private Control RowView(ScaleRow row)
    {
        if (row is null) return new TextBlock();
        if (row.HitIndex < 0) return new TextBlock { Text = row.Text, FontWeight = global::Avalonia.Media.FontWeight.SemiBold };
        var star = ScalePopupLayout.FavouriteStar(row.Favourite);
        TextLinkButton.Apply(star);
        AutomationProperties.SetName(star, (row.Favourite ? "Remove " : "Add ") + row.Scale + " " + (row.Favourite ? "from" : "to") + " favourites (all keys)");
        ToolTip.SetTip(star, "Favourite scale: " + row.Scale + " in every key");
        star.Tapped += (_, e) => e.Handled = true;
        star.Click += (_, _) => ToggleFavourite(row.Scale);
        var line = new DockPanel();
        DockPanel.SetDock(star, Dock.Right);
        line.Children.Add(star);
        line.Children.Add(new TextBlock { Text = row.Text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis });
        return line;
    }

    /// <summary>Stars or unstars a scale type (the star on any key's row): saved with the settings, and the list and the detected cards follow.</summary>
    internal void ToggleFavourite(string scale)
    {
        var list = _host.Settings.Favourites;
        var existing = list.FirstOrDefault(f => f.Equals(scale, StringComparison.OrdinalIgnoreCase));
        if (existing is null) list.Add(scale); else list.Remove(existing);
        _host.SettingChanged();
        Rebuild();
    }

    private string RowText(ScaleSearchHit hit, bool flats, bool musical)
    {
        var name = $"{ScaleFamilies.NoteName(hit.Root, musical && (flats || ScaleFamilies.PrefersFlats(hit.Root, hit.Scale)), musical)} {hit.Scale}";
        var notes = string.Join(" ", ScaleFamilies.NotesOf(hit.Root, hit.Scale, flats, musical));
        if (_histogram is not { NoteCount: > 0 } h) return $"{name}  ·  {notes}";
        var fit = ScaleSearch.Fit(hit, h);
        var fitText = fit >= 0.9995 ? "all notes fit" : $"fits {fit:P0} of notes";
        return _confidence.TryGetValue((hit.Root, hit.Scale), out var confidence) ? $"{name}  ·  {notes}  ·  {confidence:P0}  ·  {fitText}" : $"{name}  ·  {notes}  ·  {fitText}";
    }

    internal void PickSelected()
    {
        if (_results is null || _results.SelectedIndex < 0 || _results.SelectedIndex >= _rowToHit.Count) return;
        var hitIndex = _rowToHit[_results.SelectedIndex];
        if (hitIndex < 0 || hitIndex >= _hits.Count) return;
        var hit = _hits[hitIndex];
        _pickAnother = true;
        _host.Pick(new ScaleMatch(hit.Root, hit.Scale, 1, 0));
        Rebuild();   // the drop-downs show the pick; the list stays open
    }
}
