using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TabForge.Avalonia.Features.Recording.View;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the table of window-level readers of the song (status bar, tempo bar, Band view, compact strip, Sections panel, Keyboard pane, FX chain windows,
/// armed rows, mixer) and the two undo scenarios every one of them must pass: it shows the document song after an undo, a redo and a later edit.
/// Also the guard that views never subscribe to the timeline marks of a project instance (the document is the only thing they listen to).
/// Does not own: the readers themselves, nor the arrangement (ArrangementUndoRebindTests).
/// Tests: MainWindowStatusBar, MainWindowTempoBar, BandView, CompactTrackStrip, SectionLinks, KeyboardModePane, FxChainWindows, ArmedRowLayer, MixerWindow.
/// </summary>
public class SongSubscriberUndoTableTests
{
    /// <summary>One reader on a document: Shows says whether it shows the document song as it is now; Edit is one edit it shows.</summary>
    private sealed record Reader(Func<bool> Shows, Action<SongProject, int> Edit, bool FollowsPlainEdits, Action Close);

    private static readonly string[] Names =
        { "TempoBar", "StatusBar", "BandView", "CompactTrackStrip", "SectionLinks", "KeyboardModePane", "FxChainWindows", "ArmedRowLayer", "MixerWindow" };

    public static IEnumerable<object[]> Cases() => Names.Select(name => new object[] { name });

    private static void Rename(SongProject project, int n) => project.Tracks[0].Name = $"Name{n}";

    private static void Retempo(SongProject project, int n) => project.Tempo = 100 + n;

    private static SongDocument Document()
    {
        var doc = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        doc.MarkClean();
        return doc;
    }

    private static MainWindowTabs Tabs(SongDocument doc) => new(doc, _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));

    private static Window Host(Control content)
    {
        var window = new Window { Width = 1300, Height = 700, Content = content };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static Reader Build(string name, SongDocument doc)
    {
        switch (name)
        {
            case "TempoBar":
            {
                var tabs = Tabs(doc);
                var bar = new ViewTempoBarView();
                var tempoBar = new MainWindowTempoBar(tabs, bar);
                return new Reader(() => bar.Caption(2) == doc.Project.Tempo.ToString(), Retempo, true, () => { tempoBar.Dispose(); tabs.Dispose(); });
            }
            case "StatusBar":
            {
                var tabs = Tabs(doc);
                var owner = new Window();
                var status = new MainWindowStatusBar(tabs, new ScorePreviewView(), null, null, owner);
                string Master() => status.View.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "StatusMaster").Text ?? "";
                return new Reader(() => Master().Contains(doc.Project.Tempo.ToString()), Retempo, true, () => { status.Dispose(); tabs.Dispose(); owner.Close(); });
            }
            case "BandView":
            {
                var view = new BandView(doc);
                var window = Host(view);
                return new Reader(() => view.PanelFor(0) is { } panel && ReferenceEquals(panel.Staff.Probe.Track, doc.Project.Tracks[0]), Rename, true, window.Close);
            }
            case "CompactTrackStrip":
            {
                var strip = new CompactTrackStrip();
                strip.SetOn(true, doc);
                return new Reader(() => strip.Pills.Count == doc.Project.Tracks.Count && strip.Pills[0].TrackName == doc.Project.Tracks[0].Name, Rename, true, () => strip.SetOn(false, null));
            }
            case "SectionLinks":
            {
                var tabs = Tabs(doc);
                var links = new SectionLinks(tabs, new ScorePreviewView(), null, () => null);
                var panel = links.NewPanel();
                var window = Host(panel);
                return new Reader(() => panel.Sections.Select(m => m.Title).SequenceEqual(doc.Project.Markers.OrderBy(m => m.MeasureIndex).Select(m => m.Title)),
                    (project, n) => project.Markers[0].Title = $"Section{n}", true, () => { links.Dispose(); window.Close(); tabs.Dispose(); });
            }
            case "KeyboardModePane":
            {
                var pane = new KeyboardModePane(() => { }, () => { });
                var engine = new KeyboardLearnEngine();
                pane.BindPlayback(null, doc, engine);
                var window = Host(pane);
                return new Reader(() => pane.Bar.TrackNames.SequenceEqual(doc.Project.Tracks.Select(t => t.Name)), Rename, true, () => { window.Close(); engine.Dispose(); });
            }
            case "FxChainWindows":
            {
                var owner = Host(new Border());
                var windows = new FxChainWindows(owner, () => doc, null, null, new TabForge.Plugins.PluginTrust());
                doc.Project.Tracks[0].Rig.Plugins = FxChainWindow.SampleChain();
                doc.MarkClean();
                windows.Open(doc.Project.Tracks[0]);
                return new Reader(() => windows.WindowOf(doc.Project.Tracks[0]) is { } open && ReferenceEquals(open.Track, doc.Project.Tracks[0]),
                    Rename, true, () => { windows.CloseSong(doc); owner.Close(); });
            }
            case "ArmedRowLayer":
            {
                doc.Project.Tracks[2].RecordArm = true;
                doc.MarkClean();
                var view = new ArrangementView(doc.Project, doc);
                var window = Host(view);
                return new Reader(() =>
                {
                    window.UpdateLayout();
                    return view.ArmedRows.Children.OfType<TrackInputPicker>().Single().Label is { } shown && shown.Contains(TabForge.Models.AudioInputs.Label(doc.Project.Tracks[2].AudioInput), StringComparison.Ordinal);   // the picker button shows the input's label
                }, (project, n) => project.Tracks[2].AudioInput = TabForge.Models.AudioInputs.Mono(n + 2), true, window.Close);
            }
            case "MixerWindow":
            {
                var mixer = new MixerWindow(doc);
                mixer.Show();
                mixer.EnsureStripsBuilt();
                return new Reader(() => mixer.GetVisualDescendants().OfType<Control>().Any(c => AutomationProperties.GetName(c) == $"Colour of {doc.Project.Tracks[0].Name}"),
                    Rename, true, mixer.Close);
            }
            default: throw new ArgumentOutOfRangeException(nameof(name), name, null);
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void EverySubscriberUpdatesAfterUndoThenEdit(string name)
    {
        var doc = Document();
        var reader = Build(name, doc);
        try
        {
            Assert.True(reader.Shows(), $"{name} shows the opened song");
            DocumentEdits.Run(doc, project => { reader.Edit(project, 1); return true; });
            Assert.True(!reader.FollowsPlainEdits || reader.Shows(), $"{name} shows the first edit");
            Assert.NotNull(DocumentEdits.Undo(doc));
            Assert.True(reader.Shows(), $"{name} shows the song the undo restored");
            DocumentEdits.Run(doc, project => { reader.Edit(project, 2); return true; });
            Assert.True(!reader.FollowsPlainEdits || reader.Shows(), $"{name} shows the edit made after the undo");
        }
        finally { reader.Close(); }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void EverySubscriberUpdatesAfterRedoUndoAcrossTrackDelete(string name)
    {
        var doc = Document();
        var reader = Build(name, doc);
        try
        {
            var tracks = doc.Project.Tracks.Count;
            DocumentEdits.Run(doc, project => { project.Tracks.RemoveAt(tracks - 1); return true; });
            Assert.True(!reader.FollowsPlainEdits || reader.Shows(), $"{name} shows the song without the deleted track");
            Assert.NotNull(DocumentEdits.Undo(doc));
            Assert.Equal(tracks, doc.Project.Tracks.Count);
            Assert.True(reader.Shows(), $"{name} shows the song with the track back");
            Assert.NotNull(DocumentEdits.Redo(doc));
            Assert.True(reader.Shows(), $"{name} shows the redone song");
            Assert.NotNull(DocumentEdits.Undo(doc));
            Assert.True(reader.Shows(), $"{name} shows the song of the second undo");
            DocumentEdits.Run(doc, project => { reader.Edit(project, 3); return true; });
            Assert.True(!reader.FollowsPlainEdits || reader.Shows(), $"{name} shows the edit made after redo and undo");
        }
        finally { reader.Close(); }
    }

    /// <summary>Every place in the Avalonia project allowed to name TimelineMarked, as the exact receiver expression, each reviewed to be a SongDocument (survives an undo): the views otherwise reach it
    /// through DocumentWatch, which lives in Core. Shrink-only; adding one needs a review that its receiver is a document.</summary>
    private static readonly string[] DocumentMarkReceivers = { "_watched", "_document", "document" };   // EditorPaneSwitch (SongDocument _watched, document) and PianoRollView (SongDocument _document, document)

    private static readonly Regex MarkMention = new(@"\b(?:add_|remove_)?TimelineMarked\b", RegexOptions.Compiled);

    /// <summary>A view listens on the document (its TimelineMarked and ProjectChanged, usually through a DocumentWatch), because an undo replaces the song and a
    /// subscription on that song object would go quiet. The guard flags ANY mention of TimelineMarked in the Avalonia code (+=, add_, Delegate.Combine, reflection, spacing,
    /// GetProject().TimelineMarked) except the explicit receivers in <see cref="DocumentMarkReceivers"/>.</summary>
    [Fact]
    public void ViewsNeverSubscribeToProjectInstanceMarks()
    {
        var repo = ArchitectureScan.RepoRoot();
        var avalonia = Path.Combine(repo, "Source", "src", "TabForge.Avalonia") + Path.DirectorySeparatorChar;
        var files = ArchitectureScan.SourceFiles(repo, file => !file.StartsWith(avalonia, StringComparison.OrdinalIgnoreCase));
        Assert.True(files.Count > 100, "the guard scanned no Avalonia sources");
        var offenders = new List<string>();
        foreach (var file in files)
        {
            var code = ArchitectureScan.Strip(ArchitectureScan.ReadText(file), stripStrings: false);
            foreach (Match hit in MarkMention.Matches(code))
            {
                var start = Math.Max(0, hit.Index - 40);
                var around = Regex.Replace(code.Substring(start, Math.Min(code.Length - start, hit.Index - start + hit.Length)), @"\s+", "");
                if (!DocumentMarkReceivers.Any(receiver => around.EndsWith(receiver + ".TimelineMarked", StringComparison.Ordinal)))
                    offenders.Add($"{ArchitectureScan.Relative(repo, file)}: {around}");
            }
        }
        Assert.True(offenders.Count == 0, "Subscribe to the document (DocumentWatch), not a SongProject instance:\n" + string.Join("\n", offenders));
    }

    /// <summary>The reviewed holders of a song, track or clip in the Avalonia project (file:member and why it stays correct after an undo replaces the objects). Shrink-only:
    /// a new field of these types needs the same review; a stale entry fails the guard so the list can only get shorter.</summary>
    private static readonly Dictionary<string, string> ReviewedSongHolders = new()
    {
        ["FloatingEditors.cs:Track"] = "a registry entry per track (not a view); Prune closes the windows of a track a song replace removed, as the chain window does",
        ["LiveTakes.cs:Track"] = "a presenter (not a view) of one recording pass; the pass ends when the song is replaced",
        ["InstrumentPanelView.cs:_track"] = "the track the panel is pointed at; the owner re-points it on every song replace",
        ["InstrumentPanelStrip.cs:_track"] = "the track the strip is pointed at; the owner re-points it on every song replace",
        ["ArrangementRowSource.cs:_reloadedByReplace"] = "the song the rows were last reloaded for (an identity marker, never read for content)",
        ["KeyboardLearnEngine.cs:_projectOfSource"] = "an engine (not a view); the song its source was built from, compared by identity to detect a replace",
        ["KeyboardLearnEngine.cs:_syncProject"] = "an engine (not a view); the song last synced, compared by identity to detect a replace",
        ["FxChainWindow.cs:_track"] = "one chain window per track; FxChainWindows re-resolves or closes it on a song replace",
        ["MidiProcessingWindow.cs:_track"] = "owned by the chain window of that track (Show(this)): FxChainWindows closes that window, and with it this one, when a song replace changes the track",
        ["MixerWindow.Interaction.cs:_selectedTrack"] = "the selection, re-resolved by id on every ProjectChanged",
        ["MidiParamEditors.cs:_track"] = "editors inside the MIDI processing window, which closes with its chain window",
        ["FxChainFiles.cs:_track"] = "owned by the chain window of that track",
        ["FxEditorController.cs:_track"] = "owned by the chain window of that track",
        ["TimelineView.cs:_song"] = "the arrangement view's song; the owner rebinds it on ProjectChanged",
        ["TrackHeaderColumn.cs:_song"] = "the header column's song; the owner rebinds it on ProjectChanged",
        ["RenderWindow.cs:_project"] = "opened with ShowDialog on the main window, whose input (undo included) is blocked while it is open",
        ["ScaleFinderWindow.cs:_project"] = "opened with ShowDialog on the main window, whose input (undo included) is blocked while it is open",
        ["ScaleFinderWindow.cs:_track"] = "opened with ShowDialog on the main window, whose input (undo included) is blocked while it is open",
        ["TrackPropertiesWindow.cs:_track"] = "opened with ShowDialog on the main window, whose input (undo included) is blocked while it is open",
        ["VideoExportWindow.cs:_project"] = "opened with ShowDialog on the main window, whose input (undo included) is blocked while it is open",
        ["FxParameterPane.cs:_track"] = "a part of the chain window of that track (it is made by the window and disposed with it): FxChainWindows re-resolves or closes that window, and with it this pane, on a song replace",
        ["TrackFxStrip.cs:_track"] = "a part of the chain window of that track: FxChainWindows re-resolves or closes that window, and with it this strip, on a song replace",
        ["WiringWindow.cs:_track"] = "owned by the chain window of that track (Show(this)): FxChainWindows closes that window, and with it this one, when a song replace changes the track",
        ["SelfTestImportWorker.cs:viaWorker"] = "a self-test local, not a view",
        ["SelfTestImportPlausibility.cs:viaWorker"] = "a self-test local, not a view",
    };

    private static readonly Regex SongHolder = new(@"(?:private|internal|public|protected)\s+(?:readonly\s+)?(?:SongProject|TrackModel|AudioClip)\??\s+(\w+)\s*(?:;|=(?!>)|\{)|^\s*(?:SongProject|TrackModel|AudioClip)\??\s+(viaWorker)\s*=", RegexOptions.Compiled | RegexOptions.Multiline);

    [Fact]
    public void ViewsHoldNoSongObjectsExceptReviewedOnes()
    {
        var repo = ArchitectureScan.RepoRoot();
        var avalonia = Path.Combine(repo, "Source", "src", "TabForge.Avalonia") + Path.DirectorySeparatorChar;
        var files = ArchitectureScan.SourceFiles(repo, file => !file.StartsWith(avalonia, StringComparison.OrdinalIgnoreCase));
        var found = new HashSet<string>();
        foreach (var file in files)
            foreach (Match hit in SongHolder.Matches(ArchitectureScan.Strip(ArchitectureScan.ReadText(file), stripStrings: true)))
                found.Add($"{Path.GetFileName(file)}:{(hit.Groups[1].Success ? hit.Groups[1].Value : hit.Groups[2].Value)}");
        var unreviewed = found.Where(key => !ReviewedSongHolders.ContainsKey(key)).ToList();
        var stale = ReviewedSongHolders.Keys.Where(key => !found.Contains(key)).ToList();
        Assert.True(unreviewed.Count == 0, "A view keeps a song object an undo would replace; read it from the document, or review it into ReviewedSongHolders:\n" + string.Join("\n", unreviewed));
        Assert.True(stale.Count == 0, "Remove from ReviewedSongHolders (the field is gone):\n" + string.Join("\n", stale));
    }
}
