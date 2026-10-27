using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>The answer to the copy-media question; closing the window by any other route reads as Cancel (the save is then not done).</summary>
public enum MediaPromptAnswer { Cancel, Keep, Copy }

/// <summary>
/// "Copy audio with this song?": the external files a save would bring in with their sizes, a "Layout" of two clickable radio cards each with a tiny folder tree
/// (One song folder, the default; Media folder beside), "Remember my choice", and the buttons Keep links / Copy audio (primary). Enter is Copy audio, Esc closes
/// (the save is not done).
/// Owns: the layout and the answer (Answer, Layout, Remember). Does not own: the plan (MediaCollectPlan), the copying
/// (MediaCollector), the move of the song and the settings that remember the choice (MediaSaveStep, MainWindowMediaCollect).
/// Tests: TabForge.Avalonia.Tests.MediaCollectPromptTests.
/// </summary>
public sealed class MediaCollectPromptWindow : Window
{
    public const string TitleText = "Copy audio with this song?";
    public const string KeepText = "Keep links";
    public const string CopyText = "Copy audio";
    public const string RememberText = "Remember my choice";
    public const string RememberHint = "(Settings \u203A Files)";
    public const string LayoutHeading = "Layout";
    public const string SongFolderTitle = "One song folder", SongFolderHint = "Song and audio together";
    public const string BesideTitle = "Media folder beside", BesideHint = "Song stays where it is";
    public const string LayoutHint = "One song folder moves the .gp into the new folder (nothing is deleted). Same-named files get a number.";
    private const int PreviewFiles = 2;

    private readonly CheckBox _remember = new() { Name = "RememberBox", Content = RememberText, FontSize = DialogMetrics.MediaSmallText };
    private readonly RadioCard _songFolder;
    private readonly RadioCard _beside;

    /// <param name="songName">The name of the song (file name without extension, else its title).</param>
    /// <param name="songExtension">The song file extension for the previews (".gp" when unknown).</param>
    /// <param name="layout">The layout card selected at first.</param>
    public MediaCollectPromptWindow(string songName, string? songExtension, MediaCollectPlan plan, MediaCollectLayout layout)
    {
        PromptChrome.Setup(this, TitleText, DialogMetrics.MediaPromptWidth);

        var list = new StackPanel();
        foreach (var item in plan.Items)
            list.Children.Add(MediaWindowParts.FileRow(item.Source, item.Reuse ? "already there" : MediaWindowParts.Size(item.Bytes)));
        var frame = new Border { BorderThickness = DialogMetrics.MediaOutline, CornerRadius = new CornerRadius(DialogMetrics.MediaCorner), Child = new ScrollViewer { Content = list, MaxHeight = DialogMetrics.MediaListMaxHeight } }.WithMargin(DialogMetrics.MediaListGap);
        frame[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");

        var body = PromptChrome.Column(TitleText, PromptChrome.Muted("MessageText", Message(songName, plan.ExternalCount)));
        body.Children.Add(frame);
        body.Children.Add(MediaWindowParts.Muted($"Total {MediaWindowParts.Size(plan.BytesToCopy)}"));

        body.Children.Add(new TextBlock { Name = "LayoutHeading", Text = LayoutHeading, FontWeight = FontWeight.SemiBold }.WithMargin(DialogMetrics.MediaLayoutHeadingGap));
        var group = new RadioCardGroup("MediaLayoutChoice");
        var ext = string.IsNullOrEmpty(songExtension) ? ".gp" : songExtension;
        var files = plan.Items.Select(i => Path.GetFileName(i.Target)).ToList();
        _songFolder = new RadioCard(group, "SongFolder", SongFolderTitle, SongFolderHint, layout == MediaCollectLayout.SongFolder, SongFolderTree(songName, ext, files)).WithMargin(DialogMetrics.MediaCardLeftGap);
        _beside = new RadioCard(group, "Beside", BesideTitle, BesideHint, layout == MediaCollectLayout.MediaBeside, BesideTree(songName, ext, files)).WithMargin(DialogMetrics.MediaCardRightGap);
        Grid.SetColumn(_beside, 1);
        body.Children.Add(new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Children = { _songFolder, _beside } });
        body.Children.Add(MediaWindowParts.Muted(LayoutHint));

        var hint = MediaWindowParts.Muted(RememberHint);
        hint.Name = "RememberHint";

        var keep = Button("KeepButton", KeepText, "Leave the files where they are; the song keeps linking to them", MediaPromptAnswer.Keep, isDefault: false);
        var copy = Button("CopyButton", CopyText, "Copy the files as chosen above and use the copies", MediaPromptAnswer.Copy, isDefault: true);
        body.Children.Add(PromptChrome.Footer(_remember, hint, keep, copy));
        Content = body;
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) { Answer = MediaPromptAnswer.Cancel; Close(); }
            else if (e.Key == Key.Enter && e.Source is not global::Avalonia.Controls.Button) { Answer = MediaPromptAnswer.Copy; Close(); }
            else return;
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        Opened += (_, _) => (_songFolder.IsSelected ? _songFolder : _beside).Focus();
    }

    /// <summary>The button pressed; Cancel until one is.</summary>
    public MediaPromptAnswer Answer { get; private set; }

    /// <summary>"Remember my choice" is ticked.</summary>
    public bool Remember => _remember.IsChecked == true;

    /// <summary>The layout card selected now.</summary>
    public MediaCollectLayout Layout => _songFolder.IsSelected ? MediaCollectLayout.SongFolder : MediaCollectLayout.MediaBeside;

    internal static string Message(string songName, int count) =>
        $"'{songName}' uses {count} audio file{(count == 1 ? "" : "s")} outside its folder. Copy them so the song works on another PC. Originals aren't changed.";

    private Button Button(string name, string text, string tip, MediaPromptAnswer answer, bool isDefault)
    {
        var button = PromptChrome.Action(name, text, isDefault);
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => { Answer = answer; Close(); };
        return button;
    }

    // The folder named after the song, holding the song file and the Audio folder with the files.
    private static Control SongFolderTree(string song, string ext, List<string> files)
    {
        var lines = new List<(int Depth, string Text, bool Folder)> { (0, song + "/", true), (1, song + ext, false), (1, MediaLayouts.AudioFolderName + "/", true) };
        lines.AddRange(FileLines(2, files));
        return Tree(lines);
    }

    // The song file next to the Media folder holding the files.
    private static Control BesideTree(string song, string ext, List<string> files)
    {
        var lines = new List<(int Depth, string Text, bool Folder)> { (0, song + ext, false), (0, song + " Media/", true) };
        lines.AddRange(FileLines(1, files));
        return Tree(lines);
    }

    private static IEnumerable<(int Depth, string Text, bool Folder)> FileLines(int depth, List<string> files)
    {
        foreach (var file in files.Take(PreviewFiles)) yield return (depth, file, false);
        if (files.Count > PreviewFiles) yield return (depth, "\u2026", false);
    }

    private static Control Tree(IEnumerable<(int Depth, string Text, bool Folder)> lines)
    {
        var panel = new StackPanel { Name = "TreePreview" };
        foreach (var (depth, text, folder) in lines)
        {
            var block = new TextBlock { Text = text, FontSize = DialogMetrics.MediaSmallText, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 }
                .WithMargin(DialogMetrics.MediaTreeIndentOf(depth));
            if (folder) block.FontWeight = FontWeight.SemiBold;
            else block[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
            panel.Children.Add(block);
        }
        return panel;
    }
}
