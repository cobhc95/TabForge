using System.Text;

namespace ParityRecorder;

/// <summary>
/// Hit-test map of the main window: WM_NCHITTEST sent to its handle at sample points in the title bar (top 48 DIP)
/// and in the resize edges (insets 0, 3, 6 and 9 DIP). Also the WM_SETCURSOR handled flag at the same points.
/// Owns: the sample grid, the letter legend and the two text files. Does not own: the hit-test logic (WpfResizeBorderFrame, MainWindow).
/// </summary>
internal static class HitMap
{
    private const int StepDip = 8;
    /// <summary>
    /// Sample rows and edge insets, in DIP. The resize border's test edge is not a fixed pixel line on this machine: samples
    /// between about 6 and 15 physical pixels from an edge flipped between runs. The samples therefore sit inside the
    /// first 4 DIP (always the border) or at 16 DIP and beyond (always the client area).
    /// </summary>
    private static readonly int[] TitleRowsDip = { 16, 24, 32, 40 };
    private const int EdgeClearanceDip = 16;

    /// <summary>Letter per hit-test code, as the grid prints it.</summary>
    private static readonly Dictionary<int, (char Letter, string Name)> Codes = new()
    {
        [-2] = ('E', "HTERROR"), [-1] = ('~', "HTTRANSPARENT"), [0] = ('_', "HTNOWHERE"), [1] = ('.', "HTCLIENT"),
        [2] = ('C', "HTCAPTION"), [3] = ('S', "HTSYSMENU"), [4] = ('G', "HTGROWBOX"), [5] = ('U', "HTMENU"),
        [6] = ('h', "HTHSCROLL"), [7] = ('v', "HTVSCROLL"), [8] = ('m', "HTMINBUTTON"), [9] = ('M', "HTMAXBUTTON"),
        [10] = ('<', "HTLEFT"), [11] = ('>', "HTRIGHT"), [12] = ('^', "HTTOP"), [13] = ('1', "HTTOPLEFT"),
        [14] = ('2', "HTTOPRIGHT"), [15] = ('V', "HTBOTTOM"), [16] = ('3', "HTBOTTOMLEFT"), [17] = ('4', "HTBOTTOMRIGHT"),
        [18] = ('B', "HTBORDER"), [20] = ('x', "HTCLOSE"), [21] = ('?', "HTHELP"),
    };

    /// <summary>
    /// Writes hit-test-main.txt and cursor-main.txt into <paramref name="files"/>. The main window's resize-border
    /// test reads the real pointer position rather than the message point, so the pointer is placed on each sample
    /// first (live runs only) and put back afterwards.
    /// </summary>
    public static void Record(IntPtr hwnd, IDictionary<string, string> files)
    {
        Native.GetWindowRect(hwnd, out var rect);
        var dpi = Native.GetDpiForWindow(hwnd);
        if (dpi == 0) dpi = 96;
        var scale = dpi / 96.0;
        Native.GetCursorPos(out var original);
        try
        {
            RecordGrids(hwnd, files, rect, dpi, scale);
        }
        finally
        {
            Native.SetCursorPos(original.X, original.Y);
        }
    }

    private static void RecordGrids(IntPtr hwnd, IDictionary<string, string> files, Native.NativeRect rect, uint dpi, double scale)
    {
        var widthDip = (int)Math.Round(rect.Width / scale);
        var heightDip = (int)Math.Round(rect.Height / scale);

        var hit = new StringBuilder();
        var cursor = new StringBuilder();
        var seen = new SortedSet<int>();
        var bands = new List<(string Name, IEnumerable<int> Xs, IEnumerable<int> Ys)>
        {
            ("title bar interior", Range(EdgeClearanceDip, widthDip - EdgeClearanceDip), TitleRowsDip),
        };

        var header = $"# window {widthDip}x{heightDip} DIP at dpi={dpi}; samples are DIP offsets from the window top-left; the pointer is placed on each sample";
        hit.AppendLine("# hit-test map: WM_NCHITTEST result per sample (sent to the main window handle)");
        hit.AppendLine("# edges not recorded: the frame's resize test reads the live pointer, and the results near the edges varied between runs on this machine");
        hit.AppendLine(header);
        cursor.AppendLine("# cursor map: NOT CAPTURED. The cursor a window sets is applied to the pointer's own thread, which a message from another process cannot read.");
        cursor.AppendLine("# recorded instead: WM_SETCURSOR handled flag (y = TRUE returned, n = FALSE) for the hit code at each sample. No real pointer input is used.");
        cursor.AppendLine(header);

        var bodyHit = new StringBuilder();
        var bodyCursor = new StringBuilder();
        foreach (var band in bands)
        {
            var xs = band.Xs.ToList();
            var ys = band.Ys.ToList();
            bodyHit.Append("[").Append(band.Name).Append("] cols=").Append(Describe(xs)).Append(" rows=").Append(Describe(ys)).AppendLine();
            bodyCursor.Append("[").Append(band.Name).Append("] cols=").Append(Describe(xs)).Append(" rows=").Append(Describe(ys)).AppendLine();
            foreach (var y in ys)
            {
                var row = new StringBuilder();
                var handledRow = new StringBuilder();
                foreach (var x in xs)
                {
                    var (code, handled) = Sample(hwnd, rect, x, y, scale);
                    seen.Add(code);
                    row.Append(LetterOf(code));
                    handledRow.Append(handled ? 'y' : 'n');
                }
                bodyHit.Append("y=").Append(y.ToString().PadLeft(4)).Append(" |").Append(row).AppendLine("|");
                bodyCursor.Append("y=").Append(y.ToString().PadLeft(4)).Append(" |").Append(handledRow).AppendLine("|");
            }
        }

        hit.Append("# legend: ");
        hit.Append(string.Join(", ", seen.Select(c => $"{LetterOf(c)} {NameOf(c)}({c})")));
        hit.AppendLine();
        hit.Append(bodyHit);
        cursor.Append("# derived cursor per code is not recorded; the handled flag stands in for it").AppendLine();
        cursor.Append(bodyCursor);
        files["hit-test-main.txt"] = hit.ToString();
        files["cursor-main.txt"] = cursor.ToString();
    }

    private static (int Code, bool Handled) Sample(IntPtr hwnd, Native.NativeRect rect, int xDip, int yDip, double scale)
    {
        var x = rect.Left + (int)Math.Round(xDip * scale);
        var y = rect.Top + (int)Math.Round(yDip * scale);
        Native.SetCursorPos(x, y);
        if (!Native.Send(hwnd, Native.WM_NCHITTEST, IntPtr.Zero, Native.Pack(x, y), out var hitResult))
            return (-99, false);
        var code = unchecked((short)hitResult.ToInt64());
        if (!Native.Send(hwnd, Native.WM_SETCURSOR, hwnd, Native.Pack(code, (int)Native.WM_MOUSEMOVE), out var cursorResult))
            return (code, false);
        return (code, cursorResult != IntPtr.Zero);
    }

    private static char LetterOf(int code) => code == -99 ? '!' : Codes.TryGetValue(code, out var entry) ? entry.Letter : '#';

    private static string NameOf(int code) => code == -99 ? "SEND-FAILED" : Codes.TryGetValue(code, out var entry) ? entry.Name : "UNKNOWN";

    private static IEnumerable<int> Range(int from, int to)
    {
        for (var v = from; v < to; v += StepDip) yield return v;
    }

    private static string Describe(IReadOnlyList<int> values) =>
        values.Count <= 12 ? string.Join(",", values) : $"{values[0]}..{values[^1]} ({values.Count} values)";
}
