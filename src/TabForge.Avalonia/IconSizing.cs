using System;
using Avalonia;
using TabForge.Services;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia;

// Owns: the toolbar icon size the Appearance setting gives, kept on the application's resources (written once per Apply, read by every icon site).
// Does not own: the clamp rule (UiFonts.ClampedIconSize) or the icons themselves.
// Tests: none dedicated.
internal static class IconSizing
{
    private const string Key = "TabForge.IconSize";

    /// <summary>Takes the appearance's icon size once per Apply.</summary>
    public static void Apply(AppearanceSettings appearance)
    {
        if (Application.Current is not { } app) return;
        var size = UiFonts.ClampedIconSize(appearance);
        var ui = global::Avalonia.Threading.Dispatcher.UIThread;
        if (ui.CheckAccess()) { app.Resources[Key] = size; return; }
        try { ui.Post(() => app.Resources[Key] = size); }   // off the UI thread: the write happens there
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException)
        {
            System.Diagnostics.Debug.WriteLine("IconSizing: no UI dispatcher, icon size not applied");
        }
    }

    /// <summary>The toolbar icon size in DIP (14 until an Apply).</summary>
    public static double Current =>
        Application.Current is { } app && global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess() && app.Resources.TryGetValue(Key, out var stored) && stored is double size ? size : UiFonts.DefaultIconSize;

    /// <summary>The icon size against the 14 DIP default, for icons drawn at another base size.</summary>
    public static double Scale => Current / UiFonts.DefaultIconSize;
}
