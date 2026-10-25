using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Audio;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.LiveInput;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: one window's live input in the Avalonia app: the Transport.LiveInput command (the transport Live input button and its hotkey),
//   the button's right-click menu (open the chain, auto-open, audio input rows, MIDI rows, audio device settings), the lit state of every
//   transport bar of the window, the restore question (asked before the Core session needs its answer) and the live FX chain window.
// Does not own: the live input logic (LiveInputSession in Core), the arm and stand-in track (LiveInputArm), the engine sync
//   (AvaloniaPlaybackSessions), the button itself (Platform/TransportBarView), the mixer's live input row (Views/MixerWindow).
// Tests: TabForge.Avalonia.Tests.LiveInputSessionTests.
public static class MainWindowLiveInput
{
    public const string CommandId = "Transport.LiveInput";
    public const string OpenFxHeader = "Open live FX chain", AutoOpenHeader = "Open FX window when turned on", DeviceHeader = "Audio device settings…";
    public const string FxChainName = "Live input";
    public const string RestoreQuestion = "Restore your previous live FX chain?", RestoreCaption = "Live input", RememberText = "Remember my answer";

    /// <summary>The command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { CommandId };

    /// <summary>Registers the command and hooks every new arrangement's transport bar; the returned session is disposed when the window closes.</summary>
    public static IDisposable? Register(MainWindowCommands commands, MainWindowDocumentPanes panes, AvaloniaPlaybackSessions? playback, MainWindowTabs songs,
        Window owner, MainWindowSettings? settings, Action<string> showStatus)
    {
        if (playback?.Engine is not { } engine || settings is null || Views.Fx.FxChainWindows.For(owner) is not { } windows) return null;
        var host = new Host(engine, windows, settings, owner, showStatus, () => playback.SyncEngine(songs.Active, settings.Settings.Plugins));
        var session = new LiveInputSession(host);
        host.Session = session;
        commands.Add(CommandId, () => _ = AskThen(host, session.Toggle));
        windows.OpenLiveChain = () => _ = AskThen(host, session.OpenFx);
        // The dock builds the first arrangement before this runs, so that one is hooked here too; later ones are hooked as they are made.
        void HookBar(ArrangementView arrangement)
        {
            var bar = arrangement.Transport;
            bar.LiveInputOn = () => session.IsOn;
            bar.LiveInputMenu = () => BuildMenu(session, host, () => MainWindowSettings.ShowPreferencesPage(settings, owner, SettingsCatalog.AudioVst));
            session.Changed += bar.RefreshLiveInput;
            session.MenuRequested += () => bar.LiveInputMenu?.Invoke()?.Open(bar);
        }
        if (panes.Arrangement is { } existing) HookBar(existing);
        panes.ArrangementCreated += HookBar;
        return session;
    }

    /// <summary>Built at each opening, so the ticks show the current choices.</summary>
    public static ContextMenu BuildMenu(LiveInputSession session, ILiveInputSessionHost host, Action openAudioSettings)
    {
        var menu = MenuStability.CreateContextMenu();
        menu.Items.Add(Item(OpenFxHeader, () => _ = AskThen(host, session.OpenFx)));
        menu.Items.Add(Check(AutoOpenHeader, session.OpenFxOnTurnOn, () => session.OpenFxOnTurnOn = !session.OpenFxOnTurnOn));
        menu.Items.Add(new Separator());
        var pairsApart = AudioInputs.Names.Count > 4; var pairs = false;   // many inputs: the stereo pairs follow a separator
        foreach (var input in session.Inputs)
        {
            if (pairsApart && !pairs && AudioInputs.TryParse(input, out _, out var stereo) && stereo) { menu.Items.Add(new Separator()); pairs = true; }
            menu.Items.Add(Check(AudioInputs.Label(input), session.Input == input, () => session.SetInput(input)));
        }
        menu.Items.Add(new Separator());
        foreach (var (label, device) in session.MidiChoices())
            menu.Items.Add(Check(label, session.MidiDevice == device, () => session.SetMidiInput(device)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(DeviceHeader, openAudioSettings));
        return menu;
    }

    // The restore question is a modal dialog: asked first, its answer handed to the session through the host, then the action runs.
    private static async Task AskThen(ILiveInputSessionHost host, Action action)
    {
        if (host is Host own && own.Session is { RestoreQuestionPending: true }) own.Answer = await own.Ask();
        action();
    }

    private static MenuItem Item(string header, Action run)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => run();
        return item;
    }

    private static MenuItem Check(string header, bool ticked, Action run)
    {
        var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = ticked };
        item.Click += (_, _) => run();
        return item;
    }

    private sealed class Host : ILiveInputSessionHost
    {
        private readonly Views.Fx.FxChainWindows _windows;
        private readonly MainWindowSettings _settings;
        private readonly Window _owner;
        private readonly Action<string> _status;
        private readonly Action _sync;

        public Host(AudioEngineClient engine, Views.Fx.FxChainWindows windows, MainWindowSettings settings, Window owner, Action<string> status, Action sync)
        {
            Engine = engine; _windows = windows; _settings = settings; _owner = owner; _status = status; _sync = sync;
        }

        public LiveInputSession? Session { get; set; }
        /// <summary>The answer asked before the session's call; the session reads it once.</summary>
        public (bool Restore, bool Remember) Answer { get; set; }
        public PluginSettings Settings => _settings.Settings.Plugins;
        public AudioEngineClient Engine { get; }
        public IUiThread UiThread { get; } = AvaloniaUiThread.ForApplication();
        public void SyncAudioEngine() => _sync();
        public void SaveSettings() => _settings.SaveSettings();
        public void SaveLevelQuietly() => _settings.SaveSettingsQuietly();
        public void SetStatus(string text) => _status(text);
        public (bool Restore, bool Remember) AskRestoreChain() => Answer;

        public async Task<(bool, bool)> Ask()
        {
            var (answer, ticked) = await AvaloniaMessageBox.ShowWithCheckBox(new AvaloniaDialogHost(), _owner, RestoreQuestion, RestoreCaption,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, RememberText, MessageBoxAnswer.Yes);
            return (answer == MessageBoxAnswer.Yes, ticked);
        }

        public void ShowFxChain(Action closed)
        {
            if (Session is not { } session) return;
            _windows.OpenLive(() =>
            {
                // The live chain belongs to no song: each change saves the chain and brings the engine in line at once.
                var chain = new Views.Fx.StandaloneFxChainHost(Settings, _settings.Settings.Hotkeys, _windows.Trust, new Views.Fx.AudioEngineFxAdapter(Engine), new[] { session.Track },
                    changed: _ => { session.SaveChain(); _sync(); }, saveSettings: SaveSettings, openSettings: owner => MainWindowSettings.ShowPreferencesPage(_settings, owner, SettingsCatalog.AudioVst), saveQuietly: SaveLevelQuietly)
                { AppWideChainTitle = FxChainPresenter.WindowTitle(FxChainName), AppWideChainNote = Views.Fx.LiveInputHint.Build(session), LiveStrip = new Views.Fx.LiveInputStrip(session, Engine) };
                var window = new FxChainWindow(chain, session.Track);
                window.Closed += (_, _) => { closed(); UiThread.Post(() => { session.SetSolo(false); session.SaveChain(); _sync(); }, UiPriority.Background); };
                return window;
            }, _owner);
        }
    }
}
