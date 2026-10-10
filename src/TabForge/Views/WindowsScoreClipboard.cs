using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>The Windows clipboard: private format <see cref="ScoreClip.ClipboardFormat"/> (UTF-8 JSON bytes) plus the same JSON as text.</summary>
public sealed class WindowsScoreClipboard : IScoreClipboard
{
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    public uint SequenceNumber
    {
        get
        {
            try { return GetClipboardSequenceNumber(); }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return 0; } // Not logged: clipboard busy or empty: the caller retries or reports empty
        }
    }

    public bool TryWrite(string json)
    {
        try
        {
            var data = new System.Windows.DataObject();
            data.SetData(ScoreClip.ClipboardFormat, new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false));
            data.SetText(json, System.Windows.TextDataFormat.UnicodeText);
            System.Windows.Clipboard.SetDataObject(data, copy: true);
            return true;
        }
        catch (Exception ex) when (ex is ExternalException or ThreadStateException or InvalidOperationException) { return false; } // Not logged: clipboard busy or empty: the caller retries or reports empty
    }

    public ScoreClipboardRead TryRead(int maxBytes)
    {
        try
        {
            var data = System.Windows.Clipboard.GetDataObject();
            if (data is null) return new ScoreClipboardRead(true, null);
            if (data.GetDataPresent(ScoreClip.ClipboardFormat) && data.GetData(ScoreClip.ClipboardFormat) is MemoryStream stream)
            {
                if (stream.Length > maxBytes) return new ScoreClipboardRead(true, null, TooLarge: true);
                try { return new ScoreClipboardRead(true, new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(stream.ToArray())); }
                catch (DecoderFallbackException) { return new ScoreClipboardRead(true, null); } // Not logged: clipboard busy or empty: the caller retries or reports empty
            }
            if (data.GetDataPresent(System.Windows.DataFormats.UnicodeText) && data.GetData(System.Windows.DataFormats.UnicodeText) is string text)
                return text.Length > maxBytes ? new ScoreClipboardRead(true, null, TooLarge: true) : new ScoreClipboardRead(true, text);
            return new ScoreClipboardRead(true, null);
        }
        catch (Exception ex) when (ex is ExternalException or ThreadStateException or InvalidOperationException) { return new ScoreClipboardRead(false, null); } // Not logged: clipboard busy or empty: the caller retries or reports empty
    }
}
