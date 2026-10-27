using System;
using System.Linq;
using Avalonia.Media;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Platform;

// Owns: turning a logical family name (or a comma list) into the Avalonia FontFamily, through FontProvider: with the bundled set on, Inter and
//   JetBrains Mono load from the app's Avalonia resources (Assets/Fonts).
// Does not own: which set is on (FontProvider) or the mapping rules.
// Tests: FontProviderTests.
public static class AvaloniaFonts
{
    /// <summary>Where the bundled TTF files live as Avalonia resources (the assembly is TabForge).</summary>
    public const string BundledFolder = "avares://TabForge/Assets/Fonts#";

    /// <summary>The key of the application resource that holds the app's one <see cref="FontProvider"/>.</summary>
    public const string ResourceKey = "TabForge.FontProvider";

    /// <summary>The app's font provider: the one in the application resources (the App makes it), else a default one for this operating system.</summary>
    public static FontProvider Provider =>
        global::Avalonia.Application.Current is { } app && app.Resources.TryGetResource(ResourceKey, null, out var found) && found is FontProvider fonts ? fonts : new FontProvider();

    /// <summary>The FontFamily for <paramref name="logical"/> under the current font set.</summary>
    public static FontFamily Family(string logical, FontProvider? fonts = null)
    {
        fonts ??= Provider;
        var real = string.Join(", ", logical.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(n => Real(n, fonts)));
        return new FontFamily(real.Length == 0 ? fonts.Map("Segoe UI") : real);
    }

    private static string Real(string name, FontProvider fonts)
    {
        var mapped = fonts.Map(name);
        return FontProvider.IsBundledFamily(mapped) ? BundledFolder + mapped : mapped;
    }
}
