using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Platform;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the "Find missing audio" window: its words, the live list, the summary and Relink button, Locate and its siblings, Cancel.</summary>
[Collection("MediaPolicyFileSystem")]
public sealed class MediaRelinkWindowTests
{
    private sealed class Dialogs : IFileDialogs
    {
        public string? Located;
        public string? Folder;
        public string? SaveFile(FileDialogOptions options, object? owner) => null;
        public string? OpenFile(FileDialogOptions options, object? owner) => Located;
        public string[]? OpenFiles(FileDialogOptions options, object? owner) => null;
        public string? PickFolder(FileDialogOptions options, object? owner) => Folder;
        public string[]? PickFolders(FileDialogOptions options, object? owner) => null;
    }

    private static MissingMedia Gone(MediaTestFolder tmp, string name) => new(Path.Combine(tmp.Root, "gone", name), name, Path.Combine(tmp.Root, "gone"));

    private static T Named<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    private static List<string?> Statuses(Window window) =>
        window.GetVisualDescendants().OfType<Grid>().Where(g => g.Children.Count == 3 && g.Children[1] is TextBlock).Select(g => ((TextBlock)g.Children[1]).Text).ToList();

    private static async Task Search(MediaRelinkWindow window, string folder)
    {
        await window.SearchAsync(folder);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task TheWindowSearchesSubfoldersFillsTheListAndRelinksWhatWasFound()
    {
        using var tmp = new MediaTestFolder();
        var search = tmp.Dir("found");
        tmp.File(tmp.Dir(Path.Combine("found", "one")), "kick.wav", "k");
        tmp.File(tmp.Dir(Path.Combine("found", "two")), "snare.wav", "s");
        var window = new MediaRelinkWindow(new[] { Gone(tmp, "kick.wav"), Gone(tmp, "snare.wav"), Gone(tmp, "hat.wav") }, new Dialogs());
        window.Show();
        Assert.Equal("3 audio files can\u0027t be found. Pick a folder; TabForge searches it and every subfolder by file name.", Named<TextBlock>(window, "IntroText").Text);
        Assert.Equal("Relink 0", Named<Button>(window, "RelinkButton").Content);
        Assert.False(Named<Button>(window, "RelinkButton").IsEnabled);

        await Search(window, search);

        Assert.Equal(new[] { "\u2713 found \u00B7 one", "\u2713 found \u00B7 two", "\u2717 not found" }, Statuses(window));
        Assert.Equal("2 of 3 found. Search another folder for the rest, or relink these now.", Named<TextBlock>(window, "SummaryText").Text);
        Assert.Equal("Relink 2", Named<Button>(window, "RelinkButton").Content);
        Assert.True(Named<Button>(window, "RelinkButton").IsEnabled);

        tmp.File(tmp.Dir(Path.Combine("more", "deep")), "hat.wav", "h");   // a second folder for the rest: the first two stay as they are
        await Search(window, Path.Combine(tmp.Root, "more"));
        Assert.Equal("3 of 3 found.", Named<TextBlock>(window, "SummaryText").Text);
        Assert.Equal("Relink 3", Named<Button>(window, "RelinkButton").Content);

        Named<Button>(window, "RelinkButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(3, window.Fixes.Count);
        Assert.Equal(Path.Combine(search, "one", "kick.wav"), window.Fixes[Gone(tmp, "kick.wav").FullPath]);
    }

    [AvaloniaFact]
    public async Task SeveralMatchesAreSaidInTheRow()
    {
        using var tmp = new MediaTestFolder();
        var search = tmp.Dir("found");
        var a = tmp.File(tmp.Dir(Path.Combine("found", "a")), "take.wav", "0123456789");
        var b = tmp.File(tmp.Dir(Path.Combine("found", "b")), "take.wav", "abcdefghij");
        File.SetLastWriteTimeUtc(a, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(b, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var window = new MediaRelinkWindow(new[] { Gone(tmp, "take.wav") }, new Dialogs());
        window.Show();

        await Search(window, search);

        Assert.Equal(new[] { "\u2713 2 matches \u00B7 picked same size" }, Statuses(window));
        Assert.Equal("1 of 1 found.", Named<TextBlock>(window, "SummaryText").Text);
    }

    [AvaloniaFact]
    public async Task LocateOnOneRowAlsoFixesTheFilesBesideItAndCancelHandsBackNothing()
    {
        using var tmp = new MediaTestFolder();
        var folder = tmp.Dir("moved");
        var kick = tmp.File(folder, "kick.wav", "k");
        tmp.File(folder, "snare.wav", "s");
        var dialogs = new Dialogs { Located = kick };
        var window = new MediaRelinkWindow(new[] { Gone(tmp, "kick.wav"), Gone(tmp, "snare.wav"), Gone(tmp, "hat.wav") }, dialogs);
        window.Show();

        var firstLocate = window.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, MediaRelinkWindow.LocateText));
        firstLocate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(2, window.FixCount);
        Assert.Equal("2 of 3 found. Search another folder for the rest, or relink these now.", Named<TextBlock>(window, "SummaryText").Text);
        Named<Button>(window, "CancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(window.Fixes);
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task AFolderThatDoesNotExistSaysSoAndNothingChanges()
    {
        using var tmp = new MediaTestFolder();
        var window = new MediaRelinkWindow(new[] { Gone(tmp, "kick.wav") }, new Dialogs());
        window.Show();

        await Search(window, Path.Combine(tmp.Root, "nowhere"));

        Assert.Equal("That folder does not exist.", Named<TextBlock>(window, "SummaryText").Text);
        Assert.Equal(0, window.FixCount);
        window.Close();
    }

    [AvaloniaFact]
    public async Task BrowsePutsTheFolderInTheBoxAndStartsTheSearch()
    {
        using var tmp = new MediaTestFolder();
        var folder = tmp.Dir("found");
        tmp.File(folder, "kick.wav", "k");
        var window = new MediaRelinkWindow(new[] { Gone(tmp, "kick.wav") }, new Dialogs { Folder = folder });
        window.Show();

        Named<Button>(window, "BrowseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(folder, Named<TextBox>(window, "FolderBox").Text);
        for (var i = 0; i < 200 && window.FixCount == 0; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }

        Assert.Equal(1, window.FixCount);
        window.Close();
    }
}
