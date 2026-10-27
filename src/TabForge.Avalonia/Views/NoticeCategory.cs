using System;

namespace TabForge.Avalonia.Views;

// Owns: which notices the top bar may show (Error, Warning, Recording, Render; Info is suppressed) and the
//   Action<string> sink helpers that carry a category to the sink (report.Error(text)); a plain report(text) is Info.
// Does not own: the bar (NotificationBar), the texts (the callers).
// Tests: TabForge.Avalonia.Tests.NotificationBarTests.
// Threads: UI thread; the category travels to a sink whose delegate target is an INoticeReceiver (no static state).
/// <summary>What a notice is about; only <see cref="Error"/>, <see cref="Warning"/>, <see cref="Recording"/> and <see cref="Render"/> show the bar.</summary>
public enum NoticeCategory
{
    /// <summary>Ordinary action feedback (loop set, copied, mode switched, saved OK): never shown on the bar.</summary>
    Info,
    /// <summary>A failure the user must know about (load/save failure, engine crash, device lost, plug-in crash).</summary>
    Error,
    /// <summary>A warning that needs the user.</summary>
    Warning,
    /// <summary>Video recording started, progress, finished or failed.</summary>
    Recording,
    /// <summary>Audio or video rendering or export started, progress, finished or failed.</summary>
    Render,
}

/// <summary>Category helpers for notice sinks.</summary>
public static class NoticeChannel
{
    /// <summary>True when the bar shows this category.</summary>
    public static bool Shows(NoticeCategory category) => category != NoticeCategory.Info;

    /// <summary>The tint of a category.</summary>
    public static NotificationKind KindOf(NoticeCategory category) => category switch
    {
        NoticeCategory.Error => NotificationKind.Error,
        NoticeCategory.Warning => NotificationKind.Warn,
        _ => NotificationKind.Info,
    };

    /// <summary>Calls a text sink; a sink bound to an <see cref="INoticeReceiver"/> (a method group on it) receives the category, any other sink just gets the text.</summary>
    public static void Send(Action<string> sink, string text, NoticeCategory category)
    {
        if (sink.Target is INoticeReceiver receiver) receiver.Receive(text, category);
        else sink(text);
    }

    public static void Error(this Action<string> sink, string text) => Send(sink, text, NoticeCategory.Error);
    public static void Warning(this Action<string> sink, string text) => Send(sink, text, NoticeCategory.Warning);
    public static void Recording(this Action<string> sink, string text) => Send(sink, text, NoticeCategory.Recording);
    public static void Render(this Action<string> sink, string text) => Send(sink, text, NoticeCategory.Render);
    public static void Info(this Action<string> sink, string text) => Send(sink, text, NoticeCategory.Info);
}

/// <summary>A notice sink that takes the category; its Info-text method group is the plain Action&lt;string&gt; sink.</summary>
public interface INoticeReceiver
{
    void Receive(string text, NoticeCategory category);
}

/// <summary>Sends notices to the top bar (Info is dropped by the bar).</summary>
public sealed class TopBarNoticeSink : INoticeReceiver
{
    private readonly NotificationBar _bar;
    public TopBarNoticeSink(NotificationBar bar) => _bar = bar;
    public void Receive(string text, NoticeCategory category) => _bar.Show(text, NoticeChannel.KindOf(category), category);
    /// <summary>The plain sink: Info.</summary>
    public void Show(string text) => Receive(text, NoticeCategory.Info);
}
