using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

// Owns: the remembered size of the Settings window: the usable-size rules, the initial width and height and capturing the size on close (partial of PreferencesWindow).
// Tests: TabForge.Avalonia.Tests.PreferencesWindowTests.

public sealed partial class PreferencesWindow
{
    /// <summary>The remembered width <paramref name="saved"/> opens with: its stored size when usable, else the default.</summary>
    internal static double InitialSettingsWidth(AppSettings saved) => HasUsableSettingsSize(saved)
        ? saved.SettingsWindowWidth : ChromeMetrics.SettingsWindowWidth;

    /// <summary>The remembered height <paramref name="saved"/> opens with: its stored size when usable, else the default.</summary>
    internal static double InitialSettingsHeight(AppSettings saved) => HasUsableSettingsSize(saved)
        ? saved.SettingsWindowHeight : ChromeMetrics.SettingsWindowHeight;

    private static bool HasUsableSettingsSize(AppSettings saved) =>
        IsUsableSettingsSize(saved.SettingsWindowWidth, ChromeMetrics.SettingsWindowMinWidth)
        && IsUsableSettingsSize(saved.SettingsWindowHeight, ChromeMetrics.SettingsWindowMinHeight);

    private static bool IsUsableSettingsSize(double value, double minimum) =>
        double.IsFinite(value) && value >= minimum;

    /// <summary>Copies the window's bounds back into the live settings and raises <see cref="WindowSizeChanged"/>, so a resized
    /// window reopens at that size. Guarded: runs on bounds changes on the Win32 path and must never throw into layout. A minimised
    /// or maximised window has no meaningful normal bounds, and the size survives Cancel because it is written to <see cref="Saved"/>
    /// directly (never through the Working copy that <see cref="RestoreBaselineIfUnkept"/> reverts).</summary>
    private void CaptureSize()
    {
        try
        {
            if (WindowState != WindowState.Normal) return;
            // The UI-scale helper changes the size, not the user: neither that change nor its settled bounds may be remembered.
            if (WindowUiScale.IsApplyingSize(this) || WindowUiScale.MatchesAppliedSize(this, Bounds.Size)) return;
            if (TryRememberSize(Bounds.Width, Bounds.Height, Saved)) WindowSizeChanged?.Invoke();
        }
        catch (Exception ex)
        {
            try { Trace.Error(Trace.Ui, "preferences: size: " + ex.Message); }
            catch (Exception) { } // swallow: the trace write itself failed
        }
    }

    /// <summary>Writes <paramref name="width"/> by <paramref name="height"/> into the live settings when they are a usable window
    /// size (finite and at least the window minimum); false when nothing was written. Never throws.</summary>
    internal static bool TryRememberSize(double width, double height, AppSettings saved)
    {
        try
        {
            if (!IsUsableSettingsSize(width, ChromeMetrics.SettingsWindowMinWidth)
                || !IsUsableSettingsSize(height, ChromeMetrics.SettingsWindowMinHeight)) return false;
            if (saved.SettingsWindowWidth == width && saved.SettingsWindowHeight == height) return false;
            saved.SettingsWindowWidth = width;
            saved.SettingsWindowHeight = height;
            return true;
        }
        catch (Exception ex)
        {
            try { Trace.Error(Trace.Ui, "preferences: size: " + ex.Message); }
            catch (Exception) { } // swallow: the trace write itself failed
            return false;
        }
    }
}
