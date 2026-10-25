using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: "Show this track as" in the fretboard menu changes the shown track's view only: the other tracks keep their own kind, the Preferences'
/// instrument view setting is not written, and the panel shows the new kind at once.
/// Does not own: the kind rules (FretboardSettingsBinding.KindFor), the menu rows' widgets (ContextMenuBuilder) or the score's track selection.
/// Tests: TabForge.Avalonia.Tests.FretboardShowTrackAsTests.
/// </summary>
public sealed class FretboardShowTrackAsTests
{
    [AvaloniaFact]
    public void ShowThisTrackAsChangesOnlyTheShownTrack()
    {
        var project = DemoSongFactory.Create();
        project.Tracks[0].Kind = TrackKind.Guitar;
        project.Tracks.Add(NewTrackFactory.CreateTrack(project, TrackKind.Guitar));
        var otherIndex = project.Tracks.Count - 1;
        var score = new ScorePreviewView();
        score.Show(new SongDocument(() => null) { Project = project });
        var settings = new AppSettings();
        var settingBefore = settings.Editing.InstrumentView;
        var menu = new FretboardContextMenu(() => settings, () => { }, () => { }, () => score.Editor.Track);
        var strip = new InstrumentPanelStrip(score, null, null, menu);
        var window = new Window { Width = 1100, Height = 400, Content = strip };
        window.Show();
        score.ShowTrack(0);
        Assert.Equal(InstrumentPanelKind.Fretboard, strip.Panel.Kind);

        menu.Run(FretboardContextMenu.ViewId, InstrumentViews.Keyboard);
        Assert.Equal(InstrumentPanelKind.Keyboard, strip.Panel.Kind);
        Assert.Equal(settingBefore, settings.Editing.InstrumentView);

        score.ShowTrack(otherIndex);
        Assert.Equal(InstrumentPanelKind.Fretboard, strip.Panel.Kind);
        score.ShowTrack(0);
        Assert.Equal(InstrumentPanelKind.Keyboard, strip.Panel.Kind);
        window.Close();
    }
}
