using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The progress dialog shown while a render runs: a bar, a status line and a Cancel button. Cancel cancels the job's token, and the
/// button turns into Close once the job has ended, as in the render dialog.
/// Owns: the bar and status text for one job, the cancel token handed to the job, and the Cancel / Close button.
/// Does not own: the job itself (AudioEngineClient.RenderAsync), the render settings, or what the caller does with the result or the error.
/// Tests: TabForge.Avalonia.Tests.RenderProgressWindowTests.
/// </summary>
public sealed class RenderProgressWindow : Window
{
    private const double WindowWidth = 420;
    private const double WindowHeight = 180;
    private const string CancelLabel = "Cancel";
    private const string CloseLabel = "Close";

    private readonly CancellationTokenSource _cancel = new();
    private readonly ProgressBar _bar = new() { Height = 10, Maximum = 1, Margin = new Thickness(0, 8, 0, 4) };
    private readonly TextBlock _status = ToolWindowParts.Status("StatusText");
    private readonly Button _cancelButton = new() { Name = "Cancel", Content = CancelLabel, Padding = new Thickness(14, 4, 14, 4) };
    private bool _running;

    /// <param name="title">The window title, for example "Render to file".</param>
    public RenderProgressWindow(string title)
    {
        Title = title;
        Width = WindowWidth; Height = WindowHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = DialogMetrics.FontSize;

        _cancelButton.Click += (_, _) => { if (_running) RequestCancel(); else Close(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(_cancelButton);
        var content = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
        content.Children.Add(_status);
        content.Children.Add(_bar);
        content.Children.Add(buttons);
        Content = content;
        Closing += (_, _) => RequestCancel();
    }

    /// <summary>Runs the job with this window as its progress display and cancel source. Returns the job's result, or throws what the job throws.</summary>
    public async Task<T> RunAsync<T>(Func<IProgress<RenderProgressInfo>, CancellationToken, Task<T>> job)
    {
        _running = true;
        _status.Text = "Preparing…";
        var progress = new Progress<RenderProgressInfo>(ShowProgress);
        try
        {
            return await job(progress, _cancel.Token);
        }
        finally
        {
            _running = false;
            _cancelButton.Content = CloseLabel;
        }
    }

    /// <summary>Asks the running job to stop. Does nothing when no job runs or it has already been asked.</summary>
    public void RequestCancel()
    {
        if (!_cancel.IsCancellationRequested) _cancel.Cancel();
    }

    private void ShowProgress(RenderProgressInfo info)
    {
        _bar.Value = info.Fraction;
        _status.Text = $"Rendering… {info.Fraction:P0}, {info.Seconds:0.0} s of audio, {info.Speed:0.#}x realtime";
    }
}
