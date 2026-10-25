using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using TabForge.Audio;
using TabForge.Avalonia.Views.Docking;
using TabForge.Platform.Theme;
using TabForge.Platform.Windows;
using TabForge.Services;
using TabForge.Services.Features;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The preview's view of the app's settings catalogue: the real SettingsCatalog rows, bound to in-memory settings.
/// Owns: the preview's catalogue composition (the shared feature sources from Core; preview-only platform calls) and its default hotkey map.
/// Does not own: the rows (SettingsCatalog), the cards and enable rules (PreferencesPagePresenter) or the controls (PreferencesWindow).
/// Deviations: no VST folder is listed; ASIO channels are numbered generically until the engine's probe has reported the driver's names.
/// Tests: TabForge.Avalonia.Tests.PreferencesWindowTests.
/// </summary>
public static class PreferencesCatalog
{
    public const string Home = SettingsCatalog.Home, General = SettingsCatalog.General, Appearance = SettingsCatalog.Appearance;

    public static IReadOnlyList<(string Band, string[] Pages)> Bands => SettingsCatalog.Bands;

    /// <summary>Composes the setting and hotkey catalogues when this assembly loads, so every reader (the app, the key table, the palette, tests without an App) sees them installed.</summary>
#pragma warning disable CA2255 // A module initializer is the single install point every path through this assembly passes.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void InstallCatalog() => CatalogComposition.Install(new IFeatureSettingsSource[] { new BandFeatureSettings(), new VideoFeatureSettings(), new KeyboardModeFeatureSettings(), new LiveInputFeatureSettings() }, new CatalogPlatform(
        ApplyTheme,
        AsioChannels,
        () => DeviceNames(AudioDeviceNames.Capture),
        DeviceNames,   // names only: ASIO lists the installed drivers (registry), none is opened here
        Array.Empty<string>(),
        (driver, device) => driver == AudioDrivers.Asio ? AsioRates(device) : Capability(() => new WindowsAudioDevices().SupportedSampleRates(driver, device), Array.Empty<int>()),
        (driver, device) => driver == AudioDrivers.Asio ? AsioOffer(device) : Task.CompletedTask,
        (driver, device) => driver == AudioDrivers.Asio ? Engine?.KnownAsio(device)?.PreferredBuffer ?? 0 : 0,
        key => App?.DeviceNames.Settled(key) ?? Task.CompletedTask));

    private static App? App => global::Avalonia.Application.Current as App;

    private static AudioEngineClient? Engine => App?.Engine;

    /// <summary>The devices of <paramref name="key"/> (a driver, or <see cref="AudioDeviceNames.Capture"/>): none without an app, null while the app's cache lists them on a worker.</summary>
    private static IReadOnlyList<string>? DeviceNames(string key) => App is { } app ? app.DeviceNames.Names(key) : Array.Empty<string>();

    /// <summary>The rates ASIO driver <paramref name="driver"/> offers: none without an engine, null while the engine is still asked (see <see cref="AsioOffer"/>).</summary>
    private static IReadOnlyList<int>? AsioRates(string driver)
    {
        if (Engine is not { } engine) return Array.Empty<int>();
        if (engine.KnownAsio(driver) is { } known) return known.Rates;
        if (engine.AsioGivenUp(driver)) return Array.Empty<int>();
        _ = AsioOffer(driver);
        return null;
    }

    /// <summary>The channel names ASIO driver <paramref name="driver"/> reported: none without an engine or when unreadable, null while the engine is still asked (see <see cref="AsioOffer"/>).</summary>
    private static (IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs)? AsioChannels(string driver)
    {
        if (Engine is not { } engine) return (Array.Empty<string>(), Array.Empty<string>());
        if (engine.AsioChannelNamesOf(driver) is { } names) return names;
        if (engine.KnownAsio(driver) is not null || engine.AsioGivenUp(driver)) return (Array.Empty<string>(), Array.Empty<string>());
        _ = AsioOffer(driver);
        return null;
    }

    /// <summary>Asks the app's engine what ASIO driver <paramref name="driver"/> offers (the settings never open ASIO in this process: the engine may hold the driver); completes when answered, at once without an engine.</summary>
    private static Task AsioOffer(string driver) => (Engine?.ProbeAsioAsync(driver) as Task) ?? Task.CompletedTask;

    /// <summary>The catalogue's default gestures (the preview keeps no user bindings) to command ids, for the preview score editor.</summary>
    public static Dictionary<string, string> DefaultHotkeys() => HotkeyCatalog.BuildMap(new HotkeySettings());

    public static string Subtitle(string page) => SettingsCatalog.Subtitle(page);

    /// <summary>Every catalogue row bound to <paramref name="s"/>.</summary>
    public static List<SettingDescriptor> Build(AppSettings s) => SettingsCatalog.Build(s).ToList();

    private static readonly JsonSerializerOptions CopyOptions = new() { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public static AppSettings Copy(AppSettings s) => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s, CopyOptions), CopyOptions)!;

    /// <summary>The values a full reset-all restores: every setting at its default and the dock layout at DockWorkspaceController.DefaultLayout().</summary>
    public static AppSettings ResetAllDefaults() => new() { Workspace = DockWorkspaceController.DefaultLayout() };

    /// <summary>
    /// Copies the values of <paramref name="from"/> into <paramref name="to"/> in place: every section object (Audio, Plugins, Render, ...) and every nested
    /// settings object keeps its identity, so holders of a section see the new values. Lists and dictionaries are assigned. The dock layout (Workspace) is replaced only when <paramref name="includeWorkspace"/> is true.
    /// </summary>
    public static void CopyInto(AppSettings from, AppSettings to, bool includeWorkspace)
    {
        TabForge.Plugins.PluginQuarantine.KeepLive(from.Plugins, to.Plugins);   // a crash while Preferences was open must survive the copy
        // The Settings window's size is chrome, not a setting row: resizes write into the live settings directly
        // (PreferencesWindow.CaptureSize), so preview, Apply and the Cancel restore must not overwrite it from a stale copy.
        var settingsWidth = to.SettingsWindowWidth;
        var settingsHeight = to.SettingsWindowHeight;
        CopyValues(from, to, skip: nameof(AppSettings.Workspace));
        to.SettingsWindowWidth = settingsWidth;
        to.SettingsWindowHeight = settingsHeight;
        if (includeWorkspace) to.Workspace = from.Workspace;
    }

    private static void CopyValues(object from, object to, string? skip = null)
    {
        foreach (var property in from.GetType().GetProperties().Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && p.Name != skip))
        {
            var value = property.GetValue(from);
            var existing = property.GetValue(to);
            if (value is not null && existing is not null && IsNestedSettings(property.PropertyType)) CopyValues(value, existing);
            else property.SetValue(to, value);
        }
    }

    private static bool IsNestedSettings(Type type) =>
        type.IsClass && type != typeof(string) && !typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null;

    /// <summary>
    /// ThemeColours.ApplyPreset for the chosen mode: only explicit Dark and Light write the preset colours. System follows the OS and
    /// Custom keeps the current colours, so both write nothing.
    /// </summary>
    private static void ApplyTheme(AppearanceSettings a, string mode)
    {
        ThemeColours.SwitchTheme(a, mode);   // Dark and Light write their preset (Light lightens a too-dark accent once); System and Custom write nothing
    }

    /// <summary>A device answer, or <paramref name="unknown"/> when the device cannot be asked (driver missing, busy or unplugged).</summary>
    private static T Capability<T>(Func<T> ask, T unknown)
    {
        try { return ask(); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or NotSupportedException or DllNotFoundException or TypeInitializationException)
        {
            Services.Trace.Error(Services.Trace.Ui, $"audio device capability not read: {ex.Message}");
            return unknown;
        }
    }
}
