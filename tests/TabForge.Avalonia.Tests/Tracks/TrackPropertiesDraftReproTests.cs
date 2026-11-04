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
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Reproduction of the track-properties audit findings: the instrument picker inside the
/// properties window mutates the song early (extra undo step), window Cancel leaves the
/// song touched, typed boxes are lost across a picker Cancel, and the discard question
/// omits tint, drum selection and instrument.
/// Each test fails on the old code and passes once draft/commit lands.
/// </summary>
public sealed class TrackPropertiesDraftReproTests
{
    private static (SongDocument Doc, TrackModel Track, TrackPropertiesWindow Window) OpenProperties()
    {
        var project = FullDemoSongFactory.Create();
        var track = project.Tracks.First(t => !t.IsAudio && t.MidiChannel != 9);
        var doc = new SongDocument(() => null) { Project = project };
        project.IsDirty = false;
        var window = new TrackPropertiesWindow(track, doc);
        window.Show();
        return (doc, track, window);
    }

    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    private static void ClosePickerCancel(TrackPropertiesWindow window)
    {
        DialogTestKit.RunQueuedUiWork();
        var picker = window.OwnedWindows.OfType<InstrumentPickerWindow>().Single();
        DialogTestKit.Click(picker.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel"));
        DialogTestKit.RunQueuedUiWork();
    }

    [AvaloniaFact]
    public void PickerSelectInsidePropertiesCreatesNoUndoStepUntilWindowOk()
    {
        var (doc, _, window) = OpenProperties();
        try
        {
            var undo = doc.Undo.UndoCount;
            InstrumentPickerRunTests.PickThroughTheChooser(window, OtherInSameFamilyName(window));
            Assert.Equal(undo, doc.Undo.UndoCount);
        }
        finally { ForceClose(window); }
    }

    [AvaloniaFact]
    public void CancellingTheWindowAfterAPickerSelectLeavesTheSongUntouched()
    {
        var (doc, track, window) = OpenProperties();
        var before = DocumentEdits.Fingerprint(doc);
        var undo = doc.Undo.UndoCount;
        var name = track.InstrumentName;
        try
        {
            InstrumentPickerRunTests.PickThroughTheChooser(window, OtherInSameFamilyName(window));
            DialogTestKit.Click(Named<Button>(window, "CancelButton"));
            DialogTestKit.RunQueuedUiWork();
            var prompt = window.OwnedWindows.OfType<SavePromptWindow>().SingleOrDefault();
            if (prompt is not null)
            {
                DialogTestKit.Click(prompt.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "YesButton"));
                DialogTestKit.RunQueuedUiWork();
            }
            Assert.Equal(name, track.InstrumentName);
            Assert.Equal(before, DocumentEdits.Fingerprint(doc));
            Assert.Equal(undo, doc.Undo.UndoCount);
        }
        finally { ForceClose(window); }
    }

    [AvaloniaFact]
    public void WindowOkAfterAPickerSelectIsOneUndoStep()
    {
        var (doc, track, window) = OpenProperties();
        var undo = doc.Undo.UndoCount;
        var originalName = track.Name;
        var originalInstrument = track.InstrumentName;
        try
        {
            var picked = OtherInSameFamilyName(window);
            InstrumentPickerRunTests.PickThroughTheChooser(window, picked);
            Named<TextBox>(window, "NameBox").Text = "Draft commit";
            DialogTestKit.Click(Named<Button>(window, "OkButton"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.Applied);
            Assert.Equal(undo + 1, doc.Undo.UndoCount);
            var landed = doc.Project.Tracks.First(t => t.Id == track.Id);
            Assert.Equal("Draft commit", landed.Name);
            Assert.Equal(picked, landed.InstrumentName);
            Assert.NotNull(DocumentEdits.Undo(doc));
            var undone = doc.Project.Tracks.First(t => t.Id == track.Id);
            Assert.Equal(originalName, undone.Name);
            Assert.Equal(originalInstrument, undone.InstrumentName);
        }
        finally { ForceClose(window); }
    }

    [AvaloniaFact]
    public void TypedProgramSurvivesAPickerCancel()
    {
        var (_, _, window) = OpenProperties();
        try
        {
            Named<TextBox>(window, "ProgramBox").Text = "31";
            DialogTestKit.Click(Named<Button>(window, "InstrumentChooserButton"));
            DialogTestKit.RunQueuedUiWork();
            ClosePickerCancel(window);
            Assert.Equal("31", Named<TextBox>(window, "ProgramBox").Text);
        }
        finally { ForceClose(window); }
    }

    [AvaloniaFact]
    public void TintOnlyChangeAsksBeforeClosing()
    {
        var (_, _, window) = OpenProperties();
        try
        {
            var tint = Named<CheckBox>(window, "TintBox");
            tint.IsChecked = !(tint.IsChecked == true);
            DialogTestKit.Click(Named<Button>(window, "CancelButton"));
            DialogTestKit.RunQueuedUiWork();
            Assert.True(window.IsVisible);
            Assert.Single(window.OwnedWindows.OfType<SavePromptWindow>());
        }
        finally { ForceClose(window); }
    }

    [AvaloniaFact]
    public void InstrumentOnlyChangeAsksBeforeClosing()
    {
        var (_, _, window) = OpenProperties();
        try
        {
            InstrumentPickerRunTests.PickThroughTheChooser(window, OtherInSameFamilyName(window));
            DialogTestKit.Click(Named<Button>(window, "CancelButton"));
            DialogTestKit.RunQueuedUiWork();
            Assert.True(window.IsVisible);
            Assert.Single(window.OwnedWindows.OfType<SavePromptWindow>());
        }
        finally { ForceClose(window); }
    }

    private static string OtherInSameFamilyName(TrackPropertiesWindow window)
    {
        var trackName = Named<TextBlock>(window, "ChooserName").Text;
        var current = InstrumentCatalog.ForTrack(trackName, 0, false)
            ?? InstrumentCatalog.All.First(e => string.Equals(e.Name, trackName, System.StringComparison.OrdinalIgnoreCase));
        return InstrumentCatalog.All.First(e => e.Category == current.Category && e.Name != current.Name).Name;
    }

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
