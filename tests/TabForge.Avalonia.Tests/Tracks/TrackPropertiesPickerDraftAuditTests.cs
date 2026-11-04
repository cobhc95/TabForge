using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Audit 81998: typed bank/channel lost on same-family keep; capo UI/commit desync on family switch.
/// Intended state: preserve user-edited fields unless the choice explicitly changes them (program always;
/// channel and capo only on family/kind switch; banks never); capo UI and the committed track agree.
/// </summary>
public sealed class TrackPropertiesPickerDraftAuditTests
{
    [AvaloniaFact]
    public void TypedBanksAndChannelSurviveSameFamilyKeepAndCommitAsOneStep()
    {
        var project = FullDemoSongFactory.Create();
        var track = project.Tracks.First(t => !t.IsAudio && t.MidiChannel != 9);
        var originalProgram = track.MidiProgram;
        var originalInstrument = track.InstrumentName;
        var doc = new SongDocument(() => null) { Project = project };
        var undo = doc.Undo.UndoCount;
        var window = new TrackPropertiesWindow(track, doc);
        window.Show();
        try
        {
            Named<TextBox>(window, "BankMsbBox").Text = "2";
            Named<TextBox>(window, "BankLsbBox").Text = "5";
            Named<TextBox>(window, "ChannelBox").Text = "2";
            var picked = OtherInSameFamily(track);
            InstrumentPickerRunTests.PickThroughTheChooser(window, picked);
            Assert.Equal("2", Named<TextBox>(window, "BankMsbBox").Text);
            Assert.Equal("5", Named<TextBox>(window, "BankLsbBox").Text);
            Assert.Equal("2", Named<TextBox>(window, "ChannelBox").Text);
            Assert.Equal(InstrumentCatalog.Find(picked)!.Program.ToString(), Named<TextBox>(window, "ProgramBox").Text);
            DialogTestKit.Click(Named<Button>(window, "OkButton"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.Applied);
            Assert.Equal(undo + 1, doc.Undo.UndoCount);
            var landed = doc.Project.Tracks.First(t => t.Id == track.Id);
            Assert.Equal(picked, landed.InstrumentName);
            Assert.Equal(2, landed.MidiBankMsb);
            Assert.Equal(5, landed.MidiBankLsb);
            Assert.Equal(2, landed.MidiChannel);
            Assert.NotNull(DocumentEdits.Undo(doc));
            var undone = doc.Project.Tracks.First(t => t.Id == track.Id);
            Assert.Equal(originalInstrument, undone.InstrumentName);
            Assert.Equal(originalProgram, undone.MidiProgram);
            Assert.Equal(-1, undone.MidiBankMsb);
            Assert.NotNull(DocumentEdits.Redo(doc));
            var redone = doc.Project.Tracks.First(t => t.Id == track.Id);
            Assert.Equal(picked, redone.InstrumentName);
            Assert.Equal(2, redone.MidiBankMsb);
        }
        finally { ForceClose(window); }
    }

    [AvaloniaFact]
    public void TypedBanksSurviveDrumSwitchWhileChannelAndCapoFollow()
    {
        var track = new TrackModel
        {
            Name = "Empty", Kind = TrackKind.Guitar, InstrumentName = "Distortion Guitar",
            StringTunings = TrackSetup.DefaultStrings(TrackKind.Guitar)!,
            MidiProgram = 30, MidiChannel = 0, MidiBankMsb = -1, MidiBankLsb = -1,
            NumberOfFrets = 24, Capo = 2,
        };
        var doc = new SongDocument(() => null) { Project = new SongProject { Tracks = new List<TrackModel> { track } } };
        var undo = doc.Undo.UndoCount;
        var window = new TrackPropertiesWindow(track, doc);
        window.Show();
        try
        {
            Named<TextBox>(window, "BankMsbBox").Text = "2";
            Named<TextBox>(window, "BankLsbBox").Text = "5";
            var kit = InstrumentCatalog.All.First(e => e.IsDrumKit).Name;
            InstrumentPickerRunTests.PickThroughTheChooser(window, kit);
            Assert.Equal("2", Named<TextBox>(window, "BankMsbBox").Text);
            Assert.Equal("5", Named<TextBox>(window, "BankLsbBox").Text);
            Assert.Equal("9", Named<TextBox>(window, "ChannelBox").Text);
            Assert.Equal(0, window.WorkingEffectiveCapo(0));
            DialogTestKit.Click(Named<Button>(window, "OkButton"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.Applied);
            Assert.Equal(undo + 1, doc.Undo.UndoCount);
            var landed = doc.Project.Tracks.First(t => t.Id == track.Id);
            Assert.Equal(TrackKind.Drums, landed.Kind);
            Assert.Equal(9, landed.MidiChannel);
            Assert.Equal(0, landed.Capo);
            Assert.Equal(2, landed.MidiBankMsb);
            Assert.Equal(5, landed.MidiBankLsb);
            Assert.NotNull(DocumentEdits.Undo(doc));
            Assert.Equal(TrackKind.Guitar, doc.Project.Tracks.First(t => t.Id == track.Id).Kind);
            Assert.Equal(2, doc.Project.Tracks.First(t => t.Id == track.Id).Capo);
            Assert.NotNull(DocumentEdits.Redo(doc));
            Assert.Equal(0, doc.Project.Tracks.First(t => t.Id == track.Id).Capo);
        }
        finally { ForceClose(window); }
    }

    [AvaloniaFact]
    public void CapoFollowsFamilySwitchAndCommits()
    {
        var track = new TrackModel
        {
            Name = "Empty", Kind = TrackKind.Guitar, InstrumentName = "Distortion Guitar",
            StringTunings = TrackSetup.DefaultStrings(TrackKind.Guitar)!,
            MidiProgram = 30, MidiChannel = 0, MidiBankMsb = -1, MidiBankLsb = -1,
            NumberOfFrets = 24, Capo = 2,
        };
        var doc = new SongDocument(() => null) { Project = new SongProject { Tracks = new List<TrackModel> { track } } };
        var undo = doc.Undo.UndoCount;
        var window = new TrackPropertiesWindow(track, doc);
        window.Show();
        try
        {
            Assert.Equal(2, window.WorkingEffectiveCapo(0));
            InstrumentPickerRunTests.PickThroughTheChooser(window, "Electric Bass (Pick)");
            Assert.Equal(0, window.WorkingEffectiveCapo(0));
            DialogTestKit.Click(Named<Button>(window, "OkButton"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.Applied);
            Assert.Equal(undo + 1, doc.Undo.UndoCount);
            var landed = doc.Project.Tracks.First(t => t.Id == track.Id);
            Assert.Equal(0, landed.Capo);
            Assert.NotNull(DocumentEdits.Undo(doc));
            Assert.Equal(2, doc.Project.Tracks.First(t => t.Id == track.Id).Capo);
            Assert.NotNull(DocumentEdits.Redo(doc));
            Assert.Equal(0, doc.Project.Tracks.First(t => t.Id == track.Id).Capo);
        }
        finally { ForceClose(window); }
    }

    [AvaloniaFact]
    public void CapoSurvivesSameFamilyPick()
    {
        var track = new TrackModel
        {
            Name = "Empty", Kind = TrackKind.Guitar, InstrumentName = "Distortion Guitar",
            StringTunings = TrackSetup.DefaultStrings(TrackKind.Guitar)!,
            MidiProgram = 30, MidiChannel = 0, MidiBankMsb = -1, MidiBankLsb = -1,
            NumberOfFrets = 24, Capo = 2,
        };
        var doc = new SongDocument(() => null) { Project = new SongProject { Tracks = new List<TrackModel> { track } } };
        var window = new TrackPropertiesWindow(track, doc);
        window.Show();
        try
        {
            CapoPlacementTests.ClickFret(window, 3);
            Assert.Equal(3, window.WorkingEffectiveCapo(0));
            var picked = InstrumentCatalog.All.First(e => e.Category == "Guitar" && e.Name != track.InstrumentName).Name;
            InstrumentPickerRunTests.PickThroughTheChooser(window, picked);
            Assert.Equal(3, window.WorkingEffectiveCapo(0));
            DialogTestKit.Click(Named<Button>(window, "OkButton"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.Applied);
            Assert.Equal(3, doc.Project.Tracks.First(t => t.Id == track.Id).Capo);
        }
        finally { ForceClose(window); }
    }

    [AvaloniaFact]
    public void TypedBanksAndCapoSurvivePickerCancel()
    {
        var project = FullDemoSongFactory.Create();
        var track = project.Tracks.First(t => !t.IsAudio && t.MidiChannel != 9);
        var doc = new SongDocument(() => null) { Project = project };
        var window = new TrackPropertiesWindow(track, doc);
        window.Show();
        try
        {
            Named<TextBox>(window, "BankMsbBox").Text = "2";
            Named<TextBox>(window, "BankLsbBox").Text = "5";
            Named<TextBox>(window, "ChannelBox").Text = "2";
            DialogTestKit.Click(Named<Button>(window, "InstrumentChooserButton"));
            DialogTestKit.RunQueuedUiWork();
            var picker = window.OwnedWindows.OfType<InstrumentPickerWindow>().Single();
            DialogTestKit.Click(picker.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel"));
            DialogTestKit.RunQueuedUiWork();
            Assert.Equal("2", Named<TextBox>(window, "BankMsbBox").Text);
            Assert.Equal("5", Named<TextBox>(window, "BankLsbBox").Text);
            Assert.Equal("2", Named<TextBox>(window, "ChannelBox").Text);
        }
        finally { ForceClose(window); }
    }

    [AvaloniaFact]
    public void OkWhilePickerOpenIsIgnored()
    {
        var project = FullDemoSongFactory.Create();
        var track = project.Tracks.First(t => !t.IsAudio && t.MidiChannel != 9);
        var doc = new SongDocument(() => null) { Project = project };
        var window = new TrackPropertiesWindow(track, doc);
        window.Show();
        try
        {
            DialogTestKit.Click(Named<Button>(window, "InstrumentChooserButton"));
            DialogTestKit.RunQueuedUiWork();
            Assert.Single(window.OwnedWindows.OfType<InstrumentPickerWindow>());
            DialogTestKit.Click(Named<Button>(window, "OkButton"));
            DialogTestKit.RunQueuedUiWork();
            Assert.False(window.Applied);
            Assert.True(window.IsVisible);
            var picker = window.OwnedWindows.OfType<InstrumentPickerWindow>().Single();
            DialogTestKit.Click(picker.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel"));
            DialogTestKit.RunQueuedUiWork();
            Assert.True(window.IsVisible);
        }
        finally { ForceClose(window); }
    }

    private static string OtherInSameFamily(TrackModel track)
    {
        var current = InstrumentCatalog.ForTrack(track.InstrumentName, track.MidiProgram, false)!
            ?? InstrumentCatalog.All.First(e => string.Equals(e.Name, track.InstrumentName, System.StringComparison.OrdinalIgnoreCase));
        return InstrumentCatalog.All.First(e => e.Category == current.Category && e.Name != current.Name).Name;
    }

    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    private static void ForceClose(TrackPropertiesWindow window)
    {
        foreach (var owned in window.OwnedWindows.ToList()) owned.Close();
        DialogTestKit.RunQueuedUiWork();
        if (!window.IsVisible) return;
        window.Close();
        DialogTestKit.RunQueuedUiWork();
        var prompt = window.OwnedWindows.OfType<SavePromptWindow>().SingleOrDefault();
        if (prompt is not null)
        {
            DialogTestKit.Click(prompt.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "YesButton"));
            DialogTestKit.RunQueuedUiWork();
        }
        if (window.IsVisible) window.Close();
        DialogTestKit.RunQueuedUiWork();
    }
}
