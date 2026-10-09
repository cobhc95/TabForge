using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Mixer group collapse, the track colour chip and the group rules (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    private static List<Border> StripsOf(MixerWindow window) =>
        VisualDescendants<Border>(window).Where(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Mixer strip: ", StringComparison.Ordinal)).ToList();

    private static Border StripNamed(MixerWindow window, string name) =>
        StripsOf(window).First(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Mixer strip: " + name, StringComparison.Ordinal));

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static Button ButtonNamed(MixerWindow window, string name) =>
        VisualDescendants<Button>(window).First(b => System.Windows.Automation.AutomationProperties.GetName(b) == name);

    /// <summary>Collapse and expand in the mixer: toggle, keyboard, persistence, dragging a collapsed group, dropping a track onto one, the colour chip.</summary>
    private static void TestMixerGroupCollapse()
    {
        var host = new FakeMixerHost();
        foreach (var t in OrderedSong().Tracks) host.Project.Tracks.Add(t);
        using var alive = KeepAlive();
        var window = new MixerWindow(host, null);
        host.Window = window;
        try
        {
            ShowTestWindow(window);
            Click(ButtonNamed(window, "Collapse Guitars"));
            PumpUi(); window.UpdateLayout(); PumpUi();
            Check("the chevron collapses a group: its tracks are hidden, other groups stay",
                host.Project.Mixer.CollapsedGroups.SequenceEqual(new[] { "Guitars" })
                && StripNamed(window, "Lead").Visibility == Visibility.Collapsed && StripNamed(window, "Rhythm").Visibility == Visibility.Collapsed
                && StripNamed(window, "Bass").Visibility == Visibility.Visible);
            Check("a collapsed group row still shows its track count",
                VisualDescendants<TextBlock>(window).Any(t => t.Text == "2 tracks"));
            Check("the collapse-all button now offers to collapse the rest", VisualDescendants<Button>(window).Any(b => b.Content as string == "Collapse all"));

            var reloaded = JsonSerializer.Deserialize<MixerSettings>(JsonSerializer.Serialize(host.Project.Mixer))!;
            Check("the collapsed state is saved with the song", reloaded.CollapsedGroups.SequenceEqual(new[] { "Guitars" }));
            window.Close();
            window = new MixerWindow(host, null); host.Window = window;
            ShowTestWindow(window);
            Check("a reopened mixer shows the group still collapsed", StripNamed(window, "Lead").Visibility == Visibility.Collapsed);

            // Keyboard: Right expands, Left collapses a focused group row.
            Border GroupRow(string g) => VisualDescendants<Border>(window).First(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Mixer group: " + g, StringComparison.Ordinal));
            void Key(Border row, System.Windows.Input.Key key) => row.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, key)
                { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent, Source = row });
            Key(GroupRow("Guitars"), System.Windows.Input.Key.Right); PumpUi();
            Check("Right arrow on a group row expands it", host.Project.Mixer.CollapsedGroups.Count == 0 && StripNamed(window, "Lead").Visibility == Visibility.Visible);
            Key(GroupRow("Guitars"), System.Windows.Input.Key.Left); PumpUi();
            Check("Left arrow on a group row collapses it", host.Project.Mixer.CollapsedGroups.Contains("Guitars"));
            Key(GroupRow("Guitars"), System.Windows.Input.Key.Enter); PumpUi();
            Check("Enter on a group row toggles it", host.Project.Mixer.CollapsedGroups.Count == 0);
            Click(VisualDescendants<Button>(window).First(b => b.Content as string == "Collapse all")); PumpUi();
            Check("Collapse all collapses every group", host.Project.Mixer.CollapsedGroups.Count == 3);
            Click(VisualDescendants<Button>(window).First(b => b.Content as string == "Expand all")); PumpUi();
            Check("Expand all expands every group", host.Project.Mixer.CollapsedGroups.Count == 0);

            // Dragging a collapsed group moves the whole group (hidden tracks included), below the last group.
            Click(ButtonNamed(window, "Collapse Guitars")); PumpUi(); window.UpdateLayout(); PumpUi();
            Point NameAt(string name)
            {
                var text = VisualDescendants<TextBlock>(window).First(t => t.Text == name && t.FontWeight == FontWeights.SemiBold);
                DependencyObject? up = text; while (up is not null && !(up is Border rowBorder && System.Windows.Automation.AutomationProperties.GetName(rowBorder).StartsWith("Mixer ", StringComparison.Ordinal))) up = System.Windows.Media.VisualTreeHelper.GetParent(up);
                _simTarget = up as UIElement ?? text;
                return text.PointToScreen(new Point(text.ActualWidth / 2, text.ActualHeight / 2));
            }
            var last = VisualDescendants<Border>(window).First(b => b.Tag as string == "Other instruments");
            var from = NameAt("Guitars");
            var below = last.PointToScreen(new Point(60, last.ActualHeight - 2));
            MouseTo(from); MouseDown();
            for (var i = 1; i <= 8; i++) MouseTo(new Point(from.X, from.Y + (below.Y - from.Y) * i / 8));
            MouseUp();
            Check("dragging a collapsed group moves all its tracks in order", Names(host.Project) == "Bass,Piano,Lead,Rhythm", Names(host.Project));
            window.Rebuild(); PumpUi(); window.UpdateLayout(); PumpUi();
            Check("the moved group is still collapsed", host.Project.Mixer.CollapsedGroups.Contains("Guitars") && StripNamed(window, "Rhythm").Visibility == Visibility.Collapsed);

            // Dropping a track onto a collapsed group: it joins the group, which stays collapsed with the count updated.
            Click(ButtonNamed(window, "Collapse Basses")); PumpUi(); window.UpdateLayout(); PumpUi();
            var piano = NameAt("Piano");
            var basses = VisualDescendants<Border>(window).First(b => b.Tag as string == "Basses");
            var onto = basses.PointToScreen(new Point(60, basses.ActualHeight / 2));
            MouseTo(piano); MouseDown();
            for (var i = 1; i <= 8; i++) MouseTo(new Point(piano.X, piano.Y + (onto.Y - piano.Y) * i / 8));
            MouseUp();
            window.Rebuild(); PumpUi(); window.UpdateLayout(); PumpUi();
            var pianoTrack = host.Project.Tracks.First(t => t.Name == "Piano");
            Check("dropping a track on a collapsed group moves it in and the group stays collapsed",
                MixerGroups.GroupOf(host.Project, pianoTrack) == "Basses" && host.Project.Mixer.CollapsedGroups.Contains("Basses"), pianoTrack.MixerGroup ?? "(none)");
            Check("the collapsed group's count includes the dropped track", VisualDescendants<TextBlock>(window).Any(t => t.Text == "2 tracks" && t.Parent is StackPanel));

            // Colour chip: opens the track palette; the choice reaches the host (one edit) and the chip.
            var chip = ButtonNamed(window, "Colour of Lead");
            host.SetTrackColour(host.Project.Tracks.First(t => t.Name == "Lead"), "#123456");
            window.SyncValues();
            Check("a track's colour shows on its mixer chip", host.ColourEdits == 1 && chip.Content is Border { Background: System.Windows.Media.SolidColorBrush { Color: { R: 0x12, G: 0x34, B: 0x56 } } },
                "edits=" + host.ColourEdits);
        }
        finally { window.Close(); }
        var ids = HotkeyCatalog.All.Select(c => c.Id).ToHashSet();
        Check("collapse all and expand all groups are bindable commands with no default key", ids.Contains("Mixer.CollapseAllGroups") && ids.Contains("Mixer.ExpandAllGroups")
            && !HotkeyCatalog.BuildMap(new HotkeySettings()).Values.Any(v => v is "Mixer.CollapseAllGroups" or "Mixer.ExpandAllGroups"));
    }

    /// <summary>Collapsed mixer groups are view state: collapsing leaves the song clean, an undo keeps the collapse, and the file keeps it.</summary>
    private static void TestMixerCollapseIsViewState()
    {
        var song = OrderedSong();
        var doc = Documents.DocumentSession.FromProject(song, null);
        doc.MarkClean();
        song.Mixer.CollapsedGroups.Add("Guitars");   // what the mixer stores on a collapse
        Check("collapsing a group leaves the song clean", !doc.HasUnsavedChanges && !doc.Project.IsDirty);
        var name = doc.Project.Tracks[0].Name;
        Documents.DocumentEdits.Run(doc, p => { p.Tracks[0].Name = "Renamed for undo"; return true; });
        Check("an undo keeps the collapsed group", Documents.DocumentEdits.Undo(doc) is not null && doc.Project.Tracks[0].Name == name
            && doc.Project.Mixer.CollapsedGroups.SequenceEqual(new[] { "Guitars" }));
        var folder = Path.Combine(Path.GetTempPath(), "tf-mixer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "collapsed.tforge");
            ProjectService.Save(path, doc.Project);
            Check("the collapsed group is saved with the song", ProjectService.Load(path)!.Mixer.CollapsedGroups.SequenceEqual(new[] { "Guitars" }));
        }
        finally { Directory.Delete(folder, true); }
    }

    /// <summary>App-wide group rules: they apply to a new song, a song's own rules win, resetting returns to the app rules, and they are saved in the app settings.</summary>
    private static void TestMixerAppGroupRules()
    {
        TrackModel T(string name, TrackKind kind, int program) => new() { Name = name, InstrumentName = name, Kind = kind, MidiProgram = program };
        SongProject Song() { var s = new SongProject(); s.Tracks.AddRange(new[] { T("Lead", TrackKind.Guitar, 30), T("Keys", TrackKind.Keys, 0), T("Pad", TrackKind.Keys, 88) }); return s; }
        var app = new MixerAppRules();
        var open = Song(); open.Mixer.App = app;
        string G(SongProject s, int i) => MixerGroups.GroupOf(s, s.Tracks[i]);
        Check("without app rules every song uses the defaults", G(open, 1) == "Other instruments" && G(open, 2) == "Other instruments");

        var groups = MixerRules.Defaults();
        groups.Add(new MixerGroupDef { Name = "Piano", Rules = { new(MixerRules.KindFamily, MixerRules.Piano) } });
        MixerRules.ApplyAppWide(app, open, groups, "Rest");
        Check("app rules regroup the open song and it keeps no rules of its own", G(open, 1) == "Piano" && G(open, 2) == "Rest" && !open.Mixer.HasOwnRules);
        var fresh = Song(); fresh.Mixer.App = app;
        Check("app rules apply to a new song automatically", G(fresh, 1) == "Piano" && G(fresh, 2) == "Rest" && fresh.Mixer.IsDefault);

        var saved = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { MixerRules = app }))!;
        var later = Song(); later.Mixer.App = saved.MixerRules;
        Check("app rules are saved in the app settings and reload", G(later, 1) == "Piano" && saved.MixerRules.Fallback == "Rest" && saved.MixerRules.Groups!.Count == 4);

        var own = MixerRules.Defaults();
        own.Add(new MixerGroupDef { Name = "Pads", Rules = { new(MixerRules.KindProgram, "88-95") } });
        MixerRules.Apply(fresh, own, "Everything else");
        Check("a song's own rules win over the app rules", fresh.Mixer.HasOwnRules && G(fresh, 2) == "Pads" && G(fresh, 1) == "Everything else" && G(open, 1) == "Piano");
        fresh.Mixer = JsonSerializer.Deserialize<MixerSettings>(JsonSerializer.Serialize(fresh.Mixer))!;
        Check("own rules are saved with the song and a replaced mixer still follows the app", G(fresh, 2) == "Pads" && fresh.Mixer.App == app);

        MixerRules.ResetToApp(fresh);
        Check("Reset to app rules makes the song follow the app again", !fresh.Mixer.HasOwnRules && G(fresh, 1) == "Piano" && G(fresh, 2) == "Rest");
        MixerRules.Apply(fresh, groups.Select(g => g.Clone()).ToList(), "Rest");
        Check("own rules equal to the app rules are stored as following the app", !fresh.Mixer.HasOwnRules);
        fresh.Tracks[1].MixerGroup = "Rest";
        Check("a manual move beats app rules too", G(fresh, 1) == "Rest");
        MixerRules.ApplyAppWide(app, open, MixerRules.Defaults(), MixerRules.DefaultFallback);
        Check("resetting the app rules to defaults stores nothing", app.Groups is null && app.Fallback is null && G(open, 1) == "Other instruments" && G(fresh, 1) == "Other instruments");
    }

    /// <summary>Default classification, custom groups with mixed rule types, priority, persistence and the manual override.</summary>
    private static void TestMixerGroupRules()
    {
        TrackModel T(string name, TrackKind kind, int program, int channel = 0) =>
            new() { Name = name, InstrumentName = name, Kind = kind, MidiProgram = program, MidiChannel = channel };
        var song = new SongProject();
        var guitar = T("Lead", TrackKind.Guitar, 30); var bass = T("Bass", TrackKind.Bass, 33); var drums = T("Kit", TrackKind.Drums, 0, 9);
        var piano = T("Piano", TrackKind.Guitar, 0); var pad = T("Pad / Strings", TrackKind.Guitar, 88); var strings = T("Strings", TrackKind.Guitar, 48);
        var vocals = T("Vocals", TrackKind.Keys, 53); var organ = T("Organ", TrackKind.Keys, 16);
        song.Tracks.AddRange(new[] { guitar, bass, drums, piano, pad, strings, vocals, organ });
        string G(TrackModel t) => MixerGroups.GroupOf(song, t);

        Check("default grouping: guitars, bass and drums by instrument",
            G(guitar) == "Guitars" && G(bass) == "Basses" && G(drums) == "Drums");
        Check("default grouping: piano, pad, strings, vocals and organ are Other instruments, whatever the track kind",
            new[] { piano, pad, strings, vocals, organ }.All(t => G(t) == "Other instruments"), string.Join(",", new[] { piano, pad, strings, vocals, organ }.Select(G)));
        Check("by instrument has exactly four groups plus the audio family",
            string.Join("|", MixerGroups.Names(song.Mixer)) == "Guitars|Basses|Drums|Other instruments");
        Check("a default song has default mixer settings", song.Mixer.IsDefault && song.Mixer.Rules is null);

        // Custom groups: five groups with mixed rule types; the first matching group wins.
        song.Mixer.Rules = new List<MixerGroupDef>
        {
            new() { Name = "Rhythm section", Rules = { new(MixerRules.KindFamily, MixerRules.Bass), new(MixerRules.KindFamily, MixerRules.Drums) } },
            new() { Name = "Piano", Rules = { new(MixerRules.KindProgram, "0-7") } },
            new() { Name = "Atmosphere", Rules = { new(MixerRules.KindProgram, "88-95"), new(MixerRules.KindFamily, MixerRules.Strings) } },
            new() { Name = "Voices", Rules = { new(MixerRules.KindName, "vocal"), new(MixerRules.KindChannel, "4") } },
            new() { Name = "Keys", Rules = { new(MixerRules.KindTrack, "Keys") } },
            new() { Name = "Guitars", Rules = { new(MixerRules.KindFamily, MixerRules.Guitar) } },
        };
        organ.MidiChannel = 3;   // channel 4 as shown
        Check("custom groups: family, program range, name, channel and kind rules place each track",
            G(bass) == "Rhythm section" && G(drums) == "Rhythm section" && G(piano) == "Piano" && G(pad) == "Atmosphere" && G(strings) == "Atmosphere"
            && G(vocals) == "Voices" && G(guitar) == "Guitars", string.Join(",", song.Tracks.Select(G)));
        Check("the first matching group wins (the organ matches Voices by channel and Keys by kind)", G(organ) == "Voices");
        organ.MidiChannel = 0;
        Check("a track no rule matches goes to the fallback group", G(T("Weird", TrackKind.Other, 120)) == "Other instruments");
        song.Mixer.Fallback = "Rest";
        Check("the fallback group is renamable", G(T("Weird", TrackKind.Other, 120)) == "Rest");
        Check("Names lists groups in priority order, then the fallback", string.Join("|", MixerGroups.Names(song.Mixer)) == "Rhythm section|Piano|Atmosphere|Voices|Keys|Guitars|Rest");

        var round = JsonSerializer.Deserialize<MixerSettings>(JsonSerializer.Serialize(song.Mixer))!;
        Check("rules, their order and the fallback are saved and loaded",
            round.Rules!.Count == 6 && round.Rules[2].Name == "Atmosphere" && round.Rules[2].Rules[1].Value == MixerRules.Strings && round.Fallback == "Rest");
        var loaded = new SongProject { Mixer = round }; loaded.Tracks.AddRange(song.Tracks);
        Check("a reloaded song groups the same way", song.Tracks.All(t => MixerGroups.GroupOf(loaded, t) == G(t)));

        // Manual move overrides the rules; returning clears it; deleting the group drops it.
        pad.MixerGroup = "Piano";
        Check("a track moved by hand stays in the group chosen, whatever the rules say", G(pad) == "Piano");
        TrackOrdering.AssignGroup(song, pad, "Atmosphere");
        Check("moving it back to the rule group clears the manual choice", pad.MixerGroup is null && G(pad) == "Atmosphere");
        pad.MixerGroup = "Piano";
        MixerRules.Apply(song, song.Mixer.Rules!.Where(g => g.Name != "Piano").ToList(), "Rest");
        Check("deleting a group the track was moved to puts it back by rule", pad.MixerGroup is null && G(pad) == "Atmosphere");

        // Renaming a group carries its levels, collapse state and manual members; resetting returns to the stored defaults.
        song.Mixer.Edit("Atmosphere").Volume = 60; song.Mixer.CollapsedGroups.Add("Atmosphere"); vocals.MixerGroup = "Atmosphere";
        var renamedDefs = song.Mixer.Rules!.Select(g => g.Clone()).ToList(); renamedDefs.First(g => g.Name == "Atmosphere").Name = "Pads";
        MixerRules.Apply(song, renamedDefs, "Rest", new Dictionary<string, string> { ["Atmosphere"] = "Pads" });
        Check("renaming a group keeps its level, collapsed state and manual members",
            song.Mixer.Levels("Pads").Volume == 60 && song.Mixer.CollapsedGroups.Contains("Pads") && vocals.MixerGroup == "Pads");
        MixerRules.Apply(song, MixerRules.Defaults(), MixerRules.DefaultFallback);
        Check("resetting to the defaults stores nothing", song.Mixer.Rules is null && song.Mixer.Fallback is null);

        // Piano group rule from the owner's example: pianos in their own group.
        var pianoSong = new SongProject(); pianoSong.Tracks.AddRange(new[] { guitar, piano, pad });
        MixerRules.Apply(pianoSong, MixerRules.Defaults().Append(new MixerGroupDef { Name = "Piano", Rules = { new(MixerRules.KindFamily, MixerRules.Piano) } }).ToList(), MixerRules.DefaultFallback);
        Check("a rule \"pianos go to Piano\" regroups only the pianos",
            MixerGroups.GroupOf(pianoSong, piano) == "Piano" && MixerGroups.GroupOf(pianoSong, pad) == "Other instruments" && MixerGroups.GroupOf(pianoSong, guitar) == "Guitars");
    }
}
