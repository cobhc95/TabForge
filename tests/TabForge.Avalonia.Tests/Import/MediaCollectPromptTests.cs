using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the "Copy audio with this song?" window (words, layout cards, keys, answer) and the shared RadioCard behaviour (click anywhere, states, arrows).</summary>
public sealed class MediaCollectPromptTests
{
    private static MediaCollectPlan Plan(params string[] names) =>
        new(@"C:\song\song Media", names.Select(n => new MediaCollectItem(@"C:\other\" + n, 2048, @"C:\song\song Media\" + n, false)).ToList(), Array.Empty<MediaCollectSkip>());

    private static MediaCollectPromptWindow Open(MediaCollectLayout layout = MediaCollectLayout.SongFolder)
    {
        var window = new MediaCollectPromptWindow("Riff", ".gp", Plan("kick.wav", "snare.wav", "hat.wav"), layout);
        window.Show();
        return window;
    }

    private static T Named<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    private static List<string?> Lines(Control card) => card.GetVisualDescendants().OfType<StackPanel>().First(p => p.Name == "TreePreview").Children.OfType<TextBlock>().Select(t => t.Text).ToList();

    private static void Click(Window window, Control inside, Point at)
    {
        var point = inside.TranslatePoint(at, window) ?? at;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static void Press(Window window, Key key, PhysicalKey physical) => window.KeyPress(key, RawInputModifiers.None, physical, null);

    [AvaloniaFact]
    public void TheWindowShowsTheNewWordsAndDefaultsToOneSongFolder()
    {
        var window = Open();
        Assert.Equal("Copy audio with this song?", window.Title);
        Assert.Equal(MediaCollectPromptWindow.Message("Riff", 3), Named<TextBlock>(window, "MessageText").Text);
        Assert.StartsWith("'Riff' uses 3 audio files outside its folder. Copy them so the song works on another PC.", Named<TextBlock>(window, "MessageText").Text);
        Assert.Equal("Layout", Named<TextBlock>(window, "LayoutHeading").Text);
        Assert.True(Named<RadioButton>(window, "SongFolderRadio").IsChecked);
        Assert.False(Named<RadioButton>(window, "BesideRadio").IsChecked);
        Assert.Equal("One song folder", Named<RadioButton>(window, "SongFolderRadio").Content);
        Assert.Equal("Song and audio together", Named<TextBlock>(window, "SongFolderHint").Text);
        Assert.Equal("Media folder beside", Named<RadioButton>(window, "BesideRadio").Content);
        Assert.Equal("Song stays where it is", Named<TextBlock>(window, "BesideHint").Text);
        Assert.Equal("Keep links", Named<Button>(window, "KeepButton").Content);
        Assert.Equal("Copy audio", Named<Button>(window, "CopyButton").Content);
        Assert.Equal("(Settings \u203A Files)", Named<TextBlock>(window, "RememberHint").Text);
        Assert.Equal(MediaCollectLayout.SongFolder, window.Layout);
        Assert.Equal(new[] { "Riff/", "Riff.gp", "Audio/", "kick.wav", "snare.wav", "\u2026" }, Lines(Named<RadioCard>(window, "SongFolderCard")));
        Assert.Equal(new[] { "Riff.gp", "Riff Media/", "kick.wav", "snare.wav", "\u2026" }, Lines(Named<RadioCard>(window, "BesideCard")));
        window.Close();
    }

    [AvaloniaFact]
    public void ClickingAnywhereOnACardSelectsItNotOnlyTheCircle()
    {
        var window = Open();
        Click(window, Named<TextBlock>(window, "BesideHint"), new Point(4, 4));
        Assert.Equal(MediaCollectLayout.MediaBeside, window.Layout);
        Assert.True(Named<RadioButton>(window, "BesideRadio").IsChecked);
        Assert.False(Named<RadioButton>(window, "SongFolderRadio").IsChecked);

        var tree = Named<RadioCard>(window, "SongFolderCard").GetVisualDescendants().OfType<StackPanel>().First(p => p.Name == "TreePreview");
        Click(window, tree, new Point(4, 4));
        Assert.Equal(MediaCollectLayout.SongFolder, window.Layout);

        var card = Named<RadioCard>(window, "BesideCard");
        Click(window, card, new Point(card.Bounds.Width - 3, card.Bounds.Height - 3));   // the padding corner of the card
        Assert.Equal(MediaCollectLayout.MediaBeside, window.Layout);
        window.Close();
    }

    [AvaloniaFact]
    public void HoverPressedAndSelectedChangeTheCardLookAndThePointerShapeIsNeverTouched()
    {
        var window = Open();
        var beside = Named<RadioCard>(window, "BesideCard");
        var rest = (beside.Background as ISolidColorBrush)?.Color;
        var restBorder = (beside.BorderBrush as ISolidColorBrush)?.Color;
        var point = beside.TranslatePoint(new Point(6, 6), window)!.Value;
        window.MouseMove(point);
        var hover = (beside.Background as ISolidColorBrush)?.Color;
        var hoverBorder = (beside.BorderBrush as ISolidColorBrush)?.Color;
        Assert.NotNull(hover);
        Assert.NotEqual(rest, hover);
        Assert.NotEqual(restBorder, hoverBorder);
        Assert.Null(beside.Cursor);

        window.MouseDown(point, MouseButton.Left);
        var pressed = (beside.Background as ISolidColorBrush)?.Color;
        window.MouseUp(point, MouseButton.Left);
        var selected = (beside.Background as ISolidColorBrush)?.Color;
        Assert.NotEqual(hover, pressed);
        Assert.NotEqual(pressed, selected);
        Assert.NotEqual(rest, selected);
        window.MouseMove(new Point(1, 1));
        Assert.Equal(selected, (beside.Background as ISolidColorBrush)?.Color);   // selected keeps its tint when the pointer leaves
        window.Close();
    }

    [AvaloniaFact]
    public void ArrowsMoveBetweenCardsSpaceSelectsAndEnterIsCopyAudio()
    {
        var window = Open();
        var oneFolder = Named<RadioCard>(window, "SongFolderCard");
        var beside = Named<RadioCard>(window, "BesideCard");
        oneFolder.Focus();
        Press(window, Key.Right, PhysicalKey.ArrowRight);
        Assert.Equal(MediaCollectLayout.MediaBeside, window.Layout);
        Assert.True(beside.IsFocused);
        Press(window, Key.Left, PhysicalKey.ArrowLeft);
        Assert.Equal(MediaCollectLayout.SongFolder, window.Layout);
        Press(window, Key.Down, PhysicalKey.ArrowDown);
        Assert.Equal(MediaCollectLayout.MediaBeside, window.Layout);
        Press(window, Key.Up, PhysicalKey.ArrowUp);
        Assert.Equal(MediaCollectLayout.SongFolder, window.Layout);

        beside.Focus();   // focus alone selects nothing; Space does
        Assert.Equal(MediaCollectLayout.SongFolder, window.Layout);
        Press(window, Key.Space, PhysicalKey.Space);
        Assert.Equal(MediaCollectLayout.MediaBeside, window.Layout);

        Named<CheckBox>(window, "RememberBox").IsChecked = true;
        Press(window, Key.Enter, PhysicalKey.Enter);
        Assert.Equal(MediaPromptAnswer.Copy, window.Answer);
        Assert.True(window.Remember);
    }

    [AvaloniaFact]
    public void EscapeIsCancelAndKeepLinksIsKeep()
    {
        var escaped = Open();
        Press(escaped, Key.Escape, PhysicalKey.Escape);
        Assert.Equal(MediaPromptAnswer.Cancel, escaped.Answer);

        var kept = Open(MediaCollectLayout.MediaBeside);
        Assert.Equal(MediaCollectLayout.MediaBeside, kept.Layout);
        Named<Button>(kept, "KeepButton").RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(MediaPromptAnswer.Keep, kept.Answer);
    }

    [AvaloniaFact]
    public void TheOverlapPromptCardsAreClickableAnywhereToo()
    {
        var overlap = new ClipOverlapPromptWindow(new ClipOverlapQuestion("Take A", "Take B", 1));
        overlap.Show();
        Assert.True(Named<RadioButton>(overlap, "MoveRadio").IsChecked);
        Click(overlap, Named<TextBlock>(overlap, "KeepHint"), new Point(4, 4));
        Assert.True(Named<RadioButton>(overlap, "KeepRadio").IsChecked);
        Assert.False(Named<RadioButton>(overlap, "MoveRadio").IsChecked);
        overlap.Close();
    }

    [AvaloniaFact]
    public void TheSectionPromptCardsAreClickableAnywhereToo()
    {
        var project = new SongProject { Title = "S" };
        var crossing = new SectionAudioCrossing("Verse", 0, 1, 0, 1, new List<(int Track, AudioClip Clip)>());
        var section = new SectionAudioPromptWindow(project, crossing);
        section.Show();
        Assert.True(Named<RadioButton>(section, "SplitRadio").IsChecked);
        Click(section, Named<TextBlock>(section, "LeaveHint"), new Point(4, 4));
        Assert.True(Named<RadioButton>(section, "LeaveRadio").IsChecked);
        Assert.False(Named<RadioButton>(section, "SplitRadio").IsChecked);
        section.Close();
    }
}
