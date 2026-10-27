namespace TabForge.Platform.Theme;

// Owns: which typefaces the app really draws with: the Windows set (Segoe UI / Consolas) or the bundled set (Inter / JetBrains Mono), the
//   one switch, and the mapping from the logical family names the drawing code uses to the real ones.
// Does not own: loading the bundled font files (the Avalonia adapter, AvaloniaFonts) or the setting row.
// Tests: FontProviderTests.
/// <summary>
/// The font provider (one per app, held in the application resources and read by the text adapters). Drawing code names a family by its logical name ("Segoe UI" for interface text, "Consolas" for tab digits); every
/// text adapter passes the name through <see cref="Map"/>, so the setting switches the whole app (interface, score, tab text) at once.
/// </summary>
public sealed class FontProvider
{
    public const string WindowsSource = "Windows", BundledSource = "Bundled";
    public const string WindowsSans = "Segoe UI", WindowsMono = "Consolas";
    public const string BundledSans = "Inter", BundledMono = "JetBrains Mono";

    public static IReadOnlyList<string> SourceChoices { get; } = new[] { "Windows (Segoe UI / Consolas)", "TabForge bundled (Inter / JetBrains Mono)" };

    /// <summary>Windows on Windows, the bundled set elsewhere (those systems have neither Segoe UI nor Consolas).</summary>
    public static string DefaultSource => OperatingSystem.IsWindows() ? WindowsSource : BundledSource;

    private volatile bool _bundled = !OperatingSystem.IsWindows();

    /// <summary>True while the bundled set is in use.</summary>
    public bool IsBundled => _bundled;

    /// <summary>Takes the stored source (empty or unknown = the default of this operating system); true when the set in use changed.</summary>
    public bool Configure(string? source)
    {
        var bundled = (source is WindowsSource or BundledSource ? source : DefaultSource) == BundledSource;
        var changed = bundled != _bundled;
        _bundled = bundled;
        return changed;
    }

    /// <summary>The choice text shown in Settings for a stored source.</summary>
    public static string ChoiceOf(string? source) => SourceChoices[(source is WindowsSource or BundledSource ? source : DefaultSource) == BundledSource ? 1 : 0];

    /// <summary>The stored source for a choice text.</summary>
    public static string SourceOf(string? choice) => string.Equals(choice, SourceChoices[1], StringComparison.Ordinal) ? BundledSource : WindowsSource;

    /// <summary>The interface typeface in use.</summary>
    public string Sans => _bundled ? BundledSans : WindowsSans;

    /// <summary>The fixed-width typeface in use.</summary>
    public string Mono => _bundled ? BundledMono : WindowsMono;

    /// <summary>The real family for a logical one: with the bundled set on, Segoe UI becomes Inter and Consolas becomes JetBrains Mono; any other family (a user choice, the symbol font) is unchanged.</summary>
    public string Map(string family)
    {
        if (!_bundled) return family;
        if (string.Equals(family, WindowsSans, StringComparison.OrdinalIgnoreCase)) return BundledSans;
        if (string.Equals(family, WindowsMono, StringComparison.OrdinalIgnoreCase)) return BundledMono;
        return family;
    }

    /// <summary>True for the two families TabForge ships as files.</summary>
    public static bool IsBundledFamily(string family) =>
        string.Equals(family, BundledSans, StringComparison.OrdinalIgnoreCase) || string.Equals(family, BundledMono, StringComparison.OrdinalIgnoreCase);
}
