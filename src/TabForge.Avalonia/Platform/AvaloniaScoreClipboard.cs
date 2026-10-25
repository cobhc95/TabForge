using System;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// <see cref="IScoreClipboard"/> over Avalonia's asynchronous clipboard. Writes put the clip JSON on the system clipboard as the private format
/// <see cref="ScoreClip.ClipboardFormat"/> (UTF-8 bytes) plus the same JSON as text, the bytes the WPF build writes; reads answer from the copy
/// <see cref="RefreshAsync"/> last fetched, so call it before a paste.
/// Owns: the fetched clipboard text and its change counter. Does not own: the clip rules or caching (ClipboardService).
/// Tests: TabForge.Avalonia.Tests.MainWindowClipboardTests.
/// </summary>
internal sealed class AvaloniaScoreClipboard : IScoreClipboard
{
    // The WPF build's private format by its exact system name (a platform format: Avalonia adds no prefix).
    private static readonly DataFormat<byte[]> ScoreClipFormat = DataFormat.CreateBytesPlatformFormat(ScoreClip.ClipboardFormat);
    private static readonly DataFormat<byte[]> RangeFormat = DataFormat.CreateBytesPlatformFormat(RangeSnapshot.ClipboardFormat);
    private readonly Func<IClipboard?> _clipboard;
    private string? _text;

    /// <param name="clipboard">The window's clipboard (TopLevel.Clipboard); null when the window has none (headless tests).</param>
    public AvaloniaScoreClipboard(Func<IClipboard?> clipboard) => _clipboard = clipboard;

    /// <summary>Starts at 1 and steps whenever the held text changes (ClipboardService caches its parse until then).</summary>
    public uint SequenceNumber { get; private set; } = 1;

    public bool TryWrite(string json)
    {
        Hold(json);
        if (_clipboard() is not { } clipboard) return false;
        var item = new DataTransferItem();
        item.Set(ScoreClipFormat, Encoding.UTF8.GetBytes(json));
        item.SetText(json);
        var data = new DataTransfer();
        data.Add(item);
        _ = Write(clipboard, data);
        return true;
    }

    public bool TryWriteRange(string json, string asciiTab)
    {
        Hold(asciiTab);
        if (_clipboard() is not { } clipboard) return false;
        var item = new DataTransferItem();
        item.Set(RangeFormat, Encoding.UTF8.GetBytes(json));
        item.SetText(asciiTab);
        var data = new DataTransfer();
        data.Add(item);
        _ = Write(clipboard, data);
        return true;
    }

    private static async Task Write(IClipboard clipboard, DataTransfer data)
    {
        try { await clipboard.SetDataAsync(data); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { } // Not logged: clipboard busy; the clip stays in TabForge's own clipboard
    }

    public ScoreClipboardRead TryRead(int maxBytes) =>
        _text is null ? new ScoreClipboardRead(true, null)
        : Encoding.UTF8.GetByteCount(_text) > maxBytes ? new ScoreClipboardRead(true, null, TooLarge: true)
        : new ScoreClipboardRead(true, _text);

    /// <summary>Fetches the system clipboard: the private format first, then text. Keeps the held text when the clipboard cannot be read.</summary>
    public async Task RefreshAsync()
    {
        if (_clipboard() is not { } clipboard) return;
        try
        {
            var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
            // A range from another TabForge instance comes first as its private JSON (ClipboardService validates it); our own range write holds the ASCII tab text until then.
            var text = await clipboard.TryGetValueAsync(RangeFormat) is { } rangeBytes ? strict.GetString(rangeBytes)
                : await clipboard.TryGetValueAsync(ScoreClipFormat) is { } bytes ? strict.GetString(bytes)
                : await clipboard.TryGetTextAsync();
            Hold(text);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or DecoderFallbackException) { } // Not logged: clipboard busy or damaged; the last held text stays
    }

    private void Hold(string? text)
    {
        if (text == _text) return;
        _text = text;
        SequenceNumber++;
    }
}
