using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>The answer to the copy-media question; closing the window by any other route reads as Cancel (the save is then not done).</summary>
public enum MediaPromptAnswer { Cancel, Keep, Copy }

/// <summary>
/// "Copy audio with this song?": the external files a save would bring into the song's Media folder, their total size, "Remember my choice", and the
/// buttons Keep links / Copy (the default).
/// Owns: the layout and the answer. Does not own: the plan (MediaCollectPlan), the copying (MediaCollector), the setting that remembers the choice.
/// Tests: TabForge.Avalonia.Tests.MediaCollectPlanTests (the plan); the window itself is checked by use.
/// </summary>
public sealed class MediaCollectPromptWindow : Window
{
    public const string TitleText = "Copy audio with this song?";
    public const string KeepText = "Keep links";
    public const string RememberText = "Remember my choice";
    private readonly CheckBox _remember = new() { Name = "RememberBox", Content = RememberText, FontSize = DialogMetrics.MediaSmallText };

    public MediaCollectPromptWindow(string songName, MediaCollectPlan plan, string mediaFolderName)
    {
        Title = TitleText;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        MinWidth = DialogMetrics.MediaPromptMinWidth;
        MaxWidth = DialogMetrics.MediaPromptMaxWidth;
        DialogMetrics.CenterOnOwner(this);
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = DialogMetrics.FontSize;

        var list = new StackPanel();
        foreach (var item in plan.Items)
            list.Children.Add(MediaWindowParts.FileRow(item.Source, item.Reuse ? "already there" : MediaWindowParts.Size(item.Bytes)));
        var frame = new Border { BorderThickness = DialogMetrics.MediaOutline, CornerRadius = new CornerRadius(DialogMetrics.MediaCorner), Child = new ScrollViewer { Content = list, MaxHeight = DialogMetrics.MediaListMaxHeight } }.WithMargin(DialogMetrics.MediaListGap);
        frame[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");

        var body = new StackPanel().WithMargin(DialogMetrics.ContentInset);
        body.Children.Add(new TextBlock
        {
            Name = "MessageText", TextWrapping = TextWrapping.Wrap,
            Text = $"'{songName}' uses {plan.ExternalCount} audio file{(plan.ExternalCount == 1 ? "" : "s")} outside its folder. Copy them into '{mediaFolderName}' so the song works on another PC. The original files are not changed.",
        });
        body.Children.Add(frame);
        body.Children.Add(MediaWindowParts.Muted($"Total {MediaWindowParts.Size(plan.BytesToCopy)} - same-named files get a number, never overwritten"));
        body.Children.Add(_remember.WithMargin(DialogMetrics.MediaRememberGap));

        var keep = Button("KeepButton", KeepText, "Leave the files where they are; the song keeps linking to them", MediaPromptAnswer.Keep, isDefault: false);
        var copy = Button("CopyButton", $"Copy into '{mediaFolderName}'", "Copy the files into the song's Media folder and use the copies", MediaPromptAnswer.Copy, isDefault: true);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { keep, copy } }.WithMargin(DialogMetrics.FooterGap);
        body.Children.Add(actions);
        Content = body;
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { Answer = MediaPromptAnswer.Cancel; Close(); e.Handled = true; } }, RoutingStrategies.Tunnel);
    }

    /// <summary>The button pressed; Cancel until one is.</summary>
    public MediaPromptAnswer Answer { get; private set; }

    /// <summary>"Remember my choice" is ticked.</summary>
    public bool Remember => _remember.IsChecked == true;

    private Button Button(string name, string text, string tip, MediaPromptAnswer answer, bool isDefault)
    {
        var button = new Button { Name = name, Content = text, MinWidth = DialogMetrics.PromptButtonMinWidth, Height = DialogMetrics.MediaButtonHeight, IsDefault = isDefault }.WithPadding(DialogMetrics.MediaButtonPadding);
        if (isDefault) button.Classes.Add(AvaloniaDefaultLooks.AccentLook);
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => { Answer = answer; Close(); };
        return button;
    }
}

/// <summary>
/// The copy progress: the file being copied, a bar, MB done of total and Cancel. The copy runs on a worker; this window only shows it, so the app stays
/// responsive. Cancel keeps the copies already made; the rest stay linked.
/// Owns: the display and the cancel token. Does not own: the copying (MediaCollector) or what happens to the song afterwards.
/// Tests: checked by use.
/// </summary>
public sealed class MediaCopyProgressWindow : Window
{
    public const string TitleText = "Copying media…";
    private readonly CancellationTokenSource _cancel = new();
    private readonly ProgressBar _bar = new() { Height = DialogMetrics.MediaBarHeight, Maximum = 1 };
    private readonly TextBlock _file = new() { Name = "FileText", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _bytes = new() { Name = "BytesText", FontSize = DialogMetrics.MediaSmallText };
    private bool _finished;

    public MediaCopyProgressWindow()
    {
        Title = TitleText;
        Width = DialogMetrics.MediaProgressWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        DialogMetrics.CenterOnOwner(this);
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        _bytes[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        FontSize = DialogMetrics.FontSize;
        var cancel = new Button { Name = "CancelButton", Content = "Cancel", MinWidth = DialogMetrics.PromptButtonMinWidth, Height = DialogMetrics.MediaButtonHeight, HorizontalAlignment = HorizontalAlignment.Right }.WithMargin(DialogMetrics.FooterGap);
        cancel.Click += (_, _) => RequestCancel();
        Content = new StackPanel { Children = { _file, _bar.WithMargin(DialogMetrics.MediaBarGap), _bytes, MediaWindowParts.Muted("Cancel keeps the copies made so far; the rest stay linked."), cancel } }.WithMargin(DialogMetrics.ContentInset);
        Closing += OnClosing;
    }

    public void RequestCancel() { if (!_cancel.IsCancellationRequested) _cancel.Cancel(); }

    // The close box asks the copy to stop; the window closes itself when the copy has ended.
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_finished) return;
        RequestCancel();
        e.Cancel = true;
    }

    /// <summary>Shows this window over <paramref name="owner"/> while the plan is copied on a worker; returns when the copy ended (the window then closes).</summary>
    public async Task<MediaCollectResult> RunAsync(Window? owner, MediaCollectPlan plan)
    {
        var shown = owner is null ? Task.CompletedTask : ShowDialog(owner);
        if (owner is null) Show();
        try { return await MediaCollector.RunAsync(plan, new Progress<MediaCopyProgress>(Report), _cancel.Token); }
        finally
        {
            _finished = true;
            Close();
            await shown;
        }
    }

    private void Report(MediaCopyProgress p)
    {
        _file.Text = p.FileName.Length == 0 ? "Done" : $"File {p.FileIndex} of {p.FileCount} - {p.FileName}";
        _bar.Value = p.BytesTotal <= 0 ? 0 : Math.Clamp((double)p.BytesDone / p.BytesTotal, 0, 1);
        _bytes.Text = $"{MediaWindowParts.Size(p.BytesDone)} of {MediaWindowParts.Size(p.BytesTotal)}";
    }
}

/// <summary>The small pieces the media windows share: a file row, muted text and a size in MB.</summary>
internal static class MediaWindowParts
{
    public static string Size(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0} MB" : $"{Math.Max(1, bytes / 1024)} KB";

    public static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, FontSize = DialogMetrics.MediaSmallText, TextWrapping = TextWrapping.Wrap };
        block[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        return block;
    }

    public static Control FileRow(string path, string detail)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }.WithMargin(DialogMetrics.MediaRowInset);
        var name = new TextBlock { Text = path, TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(name, path);
        var size = Muted(detail);
        size.WithMargin(DialogMetrics.MediaDetailGap);
        Grid.SetColumn(size, 1);
        grid.Children.Add(name);
        grid.Children.Add(size);
        return grid;
    }
}
