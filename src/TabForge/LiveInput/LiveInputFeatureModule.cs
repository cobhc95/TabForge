using System.Windows;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Services;
using TabForge.Services.Features;
using TabForge.Views;

namespace TabForge.LiveInput;

// Owns: what live input adds to the central tables (the Transport.LiveInput hotkey row and command, the Preferences group, the settings bounds), the restore question's window, and
//   making one controller per main window (kept on the window) for the command and the button.
// Does not own: on and off (LiveInputController), the program-wide arm (LiveInputArm), the button (LiveInputButton).
// Tests: TestLiveInput, TestFeatureModuleContributions.
public sealed class LiveInputFeatureModule : IFeatureModule
{
    public const string CommandId = "Transport.LiveInput";

    public string Name => "Live input";

    public IReadOnlyList<FeatureHotkey> Hotkeys { get; } = new FeatureHotkey[]
    {
        new(new(CommandId, HotkeyCatalog.CategoryTransport, "Live input", "",
            "Hear your instrument (the chosen audio input) through the live FX chain and the master, without a track and without recording; again turns it off. One for the whole program: it stays on across tabs and windows."), "Transport.Record"),
    };

    public IReadOnlyList<FeatureLayout> Layout { get; } = new FeatureLayout[]
    {
        new(SettingsCatalog.AudioVst, SettingsCatalog.LiveInputGroup, "live.restore", "Plug-ins"),
    };

    public IEnumerable<SettingDescriptor> SettingRows(AppSettings settings) => SettingsCatalog.LiveInputRows(settings);

    public IEnumerable<FeatureCommand> Commands(IFeatureHost host)
    {
        ControllerFor(host as Window);   // now: the window's exit save does not wait for its button to load
        yield return new(CommandId, () => ControllerFor(host as Window)?.Toggle());
    }

    /// <summary>The controller of a main window, kept on that window: the command and the button reach the same one, whichever is used first.</summary>
    private static readonly DependencyProperty ControllerProperty =
        DependencyProperty.RegisterAttached("LiveInputController", typeof(LiveInputController), typeof(LiveInputFeatureModule));

    /// <summary>The live input controller of <paramref name="window"/>'s main window (the window itself or an owner), made at first use; null when there is none.</summary>
    internal static LiveInputController? ControllerFor(Window? window)
    {
        for (; window is not null; window = window.Owner)
        {
            if (window.GetValue(ControllerProperty) is LiveInputController made) return made;
            if (window is not IFeatureHost || window is not IMixerSurface surface) continue;
            var controller = new LiveInputController(new SurfaceHost(surface));
            var main = window;
            main.SetValue(ControllerProperty, controller);
            // Closed, not Closing: the close is real (the save question was answered) and the engine still has the chain.
            main.Closed += (_, _) => { controller.SaveStatesOnClose(!Application.Current.Windows.OfType<IFeatureHost>().Any(other => !ReferenceEquals(other, main))); controller.Dispose(); };
            return controller;
        }
        return null;
    }

    public void Normalize(AppSettings settings) => (settings.Plugins.LiveInput ??= new LiveInputSettings()).Normalize();

    /// <summary>The main window as live input sees it.</summary>
    private sealed class SurfaceHost : ILiveInputHost
    {
        private readonly IMixerSurface _s;
        private FxChainWindow? _fx;

        public SurfaceHost(IMixerSurface surface) => _s = surface;

        public IReadOnlyList<TrackModel> Tracks => _s.Project.Tracks;
        public PluginSettings Settings => _s.Settings.Plugins;
        public AudioEngineClient Engine => _s.Engine;
        public IntPtr OwnerWindow => new System.Windows.Interop.WindowInteropHelper(_s.Window).Handle;
        public void SyncAudioEngine() => _s.SyncAudioEngine();
        public void SaveSettings() => _s.SaveSettings();
        public void SetStatus(string text) => _s.SetStatus(text);
        public void OpenAudioSettings() => _s.OpenAudioSettings();

        public (bool Restore, bool Remember) AskRestoreChain()
        {
            var prompt = new ThemedConfirmDialog("Live input", "Restore your previous live FX chain?", yesToolTip: "Load the chain you used last time",
                noToolTip: "Start with an empty chain", showCancel: false, rememberText: "Remember my answer") { Owner = _s.Window };
            var restore = DialogHost.ShowModal(prompt) == true && prompt.Result == System.Windows.MessageBoxResult.Yes;
            return (restore, prompt.RememberChoice);
        }

        public void ShowFxChain(TrackModel track, IFxChainHost fx, Action closed)
        {
            if (_fx is { IsLoaded: true }) { _fx.Activate(); return; }
            _fx = new FxChainWindow(fx, track, _s.Window);
            _fx.Closed += (_, _) => { _fx = null; closed(); };
            _fx.Show();
        }
    }
}
