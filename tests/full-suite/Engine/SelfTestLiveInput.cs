using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Documents;
using TabForge.LiveInput;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Live input: one armed, monitored stand-in track in the engine that is in no song and belongs to the program; its chain lives in the app settings.</summary>
public static partial class SelfTest
{
    private sealed class LiveInputTestHost : ILiveInputHost
    {
        public LiveInputTestHost(AudioEngineClient engine, PluginSettings settings, DocumentSession doc) { Engine = engine; Settings = settings; Doc = doc; }
        public DocumentSession Doc { get; }
        public int Saves, FxShown, Asked;
        public (bool Restore, bool Remember) Answer;
        public string Status = "";
        public IReadOnlyList<TrackModel> Tracks => Doc.Project.Tracks;
        public PluginSettings Settings { get; }
        public AudioEngineClient Engine { get; }
        public IntPtr OwnerWindow => IntPtr.Zero;
        public void SyncAudioEngine() => Engine.Sync(Doc.Project.Tracks, Settings, AudioRouting.Levels(Doc.Project, 100), Doc.Project, Doc);
        public void SaveSettings() => Saves++;
        public void SetStatus(string text) => Status = text;
        public void OpenAudioSettings() { }
        public void ShowFxChain(TrackModel track, IFxChainHost fx, Action closed) => FxShown++;
        public (bool Restore, bool Remember) AskRestoreChain() { Asked++; return Answer; }
    }

    private static AudioEngineClient LiveInputFakeEngine()
    {
        var client = new AudioEngineClient();
        client.Mixer.PlayAllThroughEngine = false;
        client.AttachFakeForTest();
        return client;
    }

    /// <summary>A window's host and controller that sync with live input on and are then disposed; only a weak reference to the host leaves.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference LiveInputArmedThenClosed(AudioEngineClient client, PluginSettings settings)
    {
        var host = new LiveInputTestHost(client, settings, DocumentSession.Blank());
        var controller = new LiveInputController(host);
        controller.TurnOn();
        host.SyncAudioEngine();
        controller.Dispose();
        return new WeakReference(host);
    }

    private static void TestLiveInput()
    {
        Section("Live input");
        var client = LiveInputFakeEngine();
        var sent = new List<EngineCommand>();
        client.SentForTest = c => sent.Add(c);
        try
        {
            var settings = new PluginSettings { PlayAllThroughEngine = false };
            var hostA = new LiveInputTestHost(client, settings, DocumentSession.Blank());
            var hostB = new LiveInputTestHost(client, settings, DocumentSession.Blank());
            client.LiveInput.Midi = new LiveInputMidi(client, client.LiveInput.Track, new MidiInputHub(new FakeMidiDeviceSet()));   // no real device
            using var a = new LiveInputController(hostA);
            using var b = new LiveInputController(hostB);
            var track = client.LiveInput.Track;
            var songA = hostA.Doc.Project;
            var undoBefore = hostA.Doc.Undo.UndoCount;
            var buttonA = new LiveInputButton();
            var buttonB = new LiveInputButton();
            buttonA.Attach(a);
            buttonB.Attach(b);

            hostA.SyncAudioEngine();
            Check("live input: off, the engine has no stand-in track", !a.IsOn && client.SlotOf(track) < 0);

            a.Toggle();
            var slot = client.SlotOf(track);
            Check("live input: on adds one armed, monitored engine track that is not a track of the song",
                a.IsOn && slot >= 0 && track.RecordArm && track.MonitorInput && track.AudioInput == AudioInputs.Input1 && sent.Contains(EngineCommand.SetArm)
                && !songA.Tracks.Contains(track) && songA.Tracks.All(t => client.SlotOf(t) < 0), $"slot {slot}, sent {string.Join(",", sent.Distinct())}");
            Check("live input: turning it on opens the live FX chain window", hostA.FxShown == 1);
            Check("live input: the song is unchanged (not dirty, no undo step, nothing of it in the saved data)",
                !songA.IsDirty && hostA.Doc.Undo.UndoCount == undoBefore && !System.Text.Json.JsonSerializer.Serialize(songA.Tracks).Contains("Live input"));

            // One arm for the program: another song becoming the active one keeps the same engine track, slot and chain.
            var loads = client.ChainLoadsSentForTest;
            sent.Clear();
            hostB.SyncAudioEngine();
            var resent = sent.Where(c => c is EngineCommand.SetArm or EngineCommand.SetTrackMix or EngineCommand.SetClips or EngineCommand.RemoveTrack).ToList();
            Check("live input: another song becoming active sends nothing for the stand-in (no level, clip or arm message)", resent.Count == 0, string.Join(",", resent));
            Check("live input: another song becoming active keeps the one stand-in in its slot without a chain reload; it is on in both and both buttons are lit",
                client.SlotOf(track) == slot && ReferenceEquals(client.LiveInput.Track, track) && client.ChainLoadsSentForTest == loads && !client.IsParkedForTest(track)
                && a.IsOn && b.IsOn && buttonA.IsLit && buttonB.IsLit, $"slot {slot} -> {client.SlotOf(track)}, loads {loads} -> {client.ChainLoadsSentForTest}");
            hostA.SyncAudioEngine();
            Check("live input: back on the first song it is still the same slot", client.SlotOf(track) == slot && client.ChainLoadsSentForTest == loads && a.IsOn);

            // No song's track: the song's group mute and levels do not apply, and a plug-in edit marks no song.
            hostB.Doc.Project.Mixer.Edit(MixerGroups.Audio).Mute = true;
            hostB.SyncAudioEngine();
            Check("live input: the stand-in takes its own level, not the song's mute, solo and group rule", client.LiveInput.Owns(track) && client.LiveInput.Mix == (100, 64) && !track.Solo && !track.Mute,
                $"{client.LiveInput.Mix}; the song's rule would give {AudioRouting.Levels(hostB.Doc.Project, 100)(track)}");
            client.LiveInput.SetMaster(0, 10);
            var mastered = client.LiveInput.Mix;
            client.LiveInput.SetMaster(100, 0);
            Check("live input: master volume 0 silences it, and the master pan moves it", mastered == (0, 74), $"{mastered}");
            // The app-wide monitor chain is no song's either; a song's own monitor chain is.
            settings.MonitorFx.Rig.Plugins.Add(new TabForge.Plugins.PluginSlot { Name = "Room", Path = @"C:\NoSuch\room.vst3", Format = "VST3" });
            hostB.SyncAudioEngine();
            object? monitorOwner = "none", ownMonitorOwner = "none";
            Action<object?> onMonitor = o => monitorOwner = o;
            client.PluginEdited += onMonitor;
            client.OnPluginEdited(MixerBuses.MonitorSlot);
            PumpUi();
            client.PluginEdited -= onMonitor;
            hostB.Doc.Project.Mixer.MonitorUseGlobal = false;
            hostB.Doc.Project.Mixer.MonitorFx.Rig.Plugins.Add(new TabForge.Plugins.PluginSlot { Name = "Own", Path = @"C:\NoSuch\own.vst3", Format = "VST3" });
            hostB.SyncAudioEngine();
            Action<object?> onOwnMonitor = o => ownMonitorOwner = o;
            client.PluginEdited += onOwnMonitor;
            client.OnPluginEdited(MixerBuses.MonitorSlot);
            PumpUi();
            client.PluginEdited -= onOwnMonitor;
            Check("monitor FX: a plug-in edit in the app-wide monitor chain names no song; in a song's own monitor chain it names that song", monitorOwner is null && ReferenceEquals(ownMonitorOwner, hostB.Doc), $"{monitorOwner} / {ownMonitorOwner}");
            settings.MonitorFx.Rig.Plugins.Clear(); hostB.Doc.Project.Mixer.MonitorFx.Rig.Plugins.Clear(); hostB.Doc.Project.Mixer.MonitorUseGlobal = true; hostB.Doc.Project.IsDirty = false;
            hostB.SyncAudioEngine();
            var hintOn = ((System.Windows.FrameworkElement)LiveInputHint.Build(a)).Visibility;
            object? editedOwner = "none";
            Action<object?> onEdited = o => editedOwner = o;
            client.PluginEdited += onEdited;
            client.OnPluginEdited(slot);
            PumpUi();
            client.PluginEdited -= onEdited;
            Check("live input: a plug-in edit in the live chain names no song (none is marked changed)", editedOwner is null && !hostB.Doc.Project.IsDirty && !songA.IsDirty, $"{editedOwner}");

            b.Toggle();
            Check("live input: turning it off from the other song turns it off everywhere and removes the stand-in from the engine",
                !a.IsOn && !b.IsOn && client.SlotOf(track) < 0 && !track.RecordArm && !buttonA.IsLit && !buttonB.IsLit);
            Check("live input: the FX window's hint with its Turn on button shows only while live input is off",
                hintOn == System.Windows.Visibility.Collapsed && ((System.Windows.FrameworkElement)LiveInputHint.Build(a)).Visibility == System.Windows.Visibility.Visible);

            // The right-click menu.
            var rows = LiveInputMenu.Build(a).Items.OfType<System.Windows.Controls.MenuItem>().ToList();
            var headers = rows.Select(r => (string)r.Header).ToList();
            Check("live input: the menu has the chain, the option, the audio inputs of a track's picker, the MIDI devices (no title rows) and the device settings",
                headers.SequenceEqual(new[] { LiveInputMenu.OpenFxHeader, LiveInputMenu.AutoOpenHeader }.Concat(AudioInputs.Audio).Append("MIDI: Any available device").Append("MIDI: Piano").Append(LiveInputMenu.DeviceHeader))
                && rows.All(r => r.IsEnabled) && rows[1].IsChecked && rows[2].IsChecked && !rows[3].IsChecked && !rows[5].IsChecked, string.Join(" | ", headers));
            rows[0].RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Check("live input: Open live FX chain opens the window without turning it on, and says it is off", hostA.FxShown == 2 && !a.IsOn && hostA.Status.Contains("off"));
            rows[3].RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            rows[1].IsChecked = false;
            rows[1].RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            var kept = System.Text.Json.JsonSerializer.Deserialize<PluginSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!.LiveInput;
            Check("live input: the input choice and the option are saved and survive a settings round trip",
                kept.Input == AudioInputs.Input2 && !kept.OpenFxOnTurnOn && hostA.Saves == 2 && LiveInputMenu.Build(a).Items.OfType<System.Windows.Controls.MenuItem>().ElementAt(3).IsChecked,
                $"{kept.Input} {kept.OpenFxOnTurnOn} saves {hostA.Saves}");
            a.Toggle();
            Check("live input: on with the option off opens no window, and monitors the chosen input", a.IsOn && hostA.FxShown == 2 && track.AudioInput == AudioInputs.Input2);
            a.Toggle();

            // No usable input: the press opens the menu instead and the status bar says why.
            var menus = 0;
            a.MenuRequested += () => menus++;
            settings.Driver = AudioDrivers.Asio; settings.AsioInputsEnabled = false;
            a.Toggle();
            Check("live input: without a usable input a press asks for the menu and says why", !a.IsOn && menus == 1 && hostA.Status.Contains("no usable input"));
            settings.Driver = AudioDrivers.WasapiShared; settings.AsioInputsEnabled = true;

            // The chain: a change goes to the settings (never to the song) and survives a round trip.
            a.Toggle();
            track.Rig.Plugins.Add(new TabForge.Plugins.PluginSlot { Name = "Amp", Path = @"C:\NoSuch\amp.vst3", Format = "VST3" });
            var savesBefore = hostA.Saves;
            ((IFxChainHost)a).ChainChanged(track);
            var back = System.Text.Json.JsonSerializer.Deserialize<PluginSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
            Check("live input: a chain change is saved to the app settings and survives a settings round trip; the song stays clean",
                hostA.Saves == savesBefore + 1 && back.LiveInput.Chain.Plugins.Count == 1 && back.LiveInput.Chain.Plugins[0].Name == "Amp" && !songA.IsDirty && hostA.Doc.Undo.UndoCount == undoBefore);

            AudioRouting.Apply(songA, null, client, settings);
            Check("live input: a plug-in of the live chain the engine will not load (not approved here) is marked unavailable like a track's", track.Rig.Plugins[0].Unavailable);
            var savesAtClose = hostA.Saves;
            a.SaveStatesOnClose(lastWindow: false);
            Check("live input: a window closing while another is open reads no states and saves nothing (live input stays on)", hostA.Saves == savesAtClose && a.IsOn);
            // A song's "disabled for this song" list is that song's only: the live chain keeps its key and its plug-in stays available.
            TabForge.Plugins.PluginTrust.Approve(settings, track.Rig.Plugins[0].Path);
            hostA.SyncAudioEngine();
            AudioRouting.Apply(songA, null, client, settings);
            var loadsBeforeSkip = client.ChainLoadsSentForTest;
            var availableBefore = !track.Rig.Plugins[0].Unavailable;
            AudioRouting.Apply(hostB.Doc.Project, null, client, settings, 100, hostB.Doc, null, new[] { track.Rig.Plugins[0].Path });
            Check("live input: a song that skips a plug-in of the live chain neither reloads the live chain nor marks that plug-in unavailable",
                client.ChainLoadsSentForTest == loadsBeforeSkip && availableBefore && !track.Rig.Plugins[0].Unavailable, $"loads {loadsBeforeSkip} -> {client.ChainLoadsSentForTest}, available before {availableBefore}, after {!track.Rig.Plugins[0].Unavailable}");
            var usedOn = client.LiveInput.Uses(@"c:\nosuch\AMP.vst3") && !client.LiveInput.Uses(@"C:\NoSuch\other.vst3");
            Check("live input: on, the arm says it uses a plug-in of its chain (a crash of it re-syncs); a new, off arm does not", usedOn && !new LiveInputArm().Uses(@"C:\NoSuch\amp.vst3"));

            // A new session: off at start; the restore question (Ask really asks; Always and Never do not).
            (int Plugins, int Asked, string Setting, bool On) NewSession(string restore, (bool, bool) answer, bool twice = false)
            {
                var saved = System.Text.Json.JsonSerializer.Deserialize<PluginSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
                saved.LiveInput.Restore = restore;
                var engine = LiveInputFakeEngine();
                try
                {
                    var host = new LiveInputTestHost(engine, saved, DocumentSession.Blank()) { Answer = answer };
                    using var session = new LiveInputController(host);
                    var startsOff = !session.IsOn;
                    session.TurnOn();
                    if (twice) { engine.LiveInput.Disarm(); session.TurnOn(); }
                    return (engine.LiveInput.Track.Rig.Plugins.Count, host.Asked, saved.LiveInput.Restore, startsOff && session.IsOn);
                }
                finally { engine.Stop(); }
            }
            var opened = System.Text.Json.JsonSerializer.Deserialize<PluginSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
            var openEngine = LiveInputFakeEngine();
            var openHost = new LiveInputTestHost(openEngine, opened, DocumentSession.Blank()) { Answer = (true, false) };
            using (var session = new LiveInputController(openHost)) session.OpenFx();
            Check("live input: opening the chain while off asks the restore question first, so an edit cannot overwrite the previous chain unasked",
                openHost.Asked == 1 && openEngine.LiveInput.Track.Rig.Plugins.Count == 1 && !openEngine.LiveInput.IsOn && openHost.FxShown == 1, $"asked {openHost.Asked}, plug-ins {openEngine.LiveInput.Track.Rig.Plugins.Count}");
            openEngine.Stop();
            var gone = LiveInputArmedThenClosed(client, settings);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check("live input: after its window's controller is disposed the program-wide arm holds nothing of that window, and live input stays on", !gone.IsAlive && client.LiveInput.IsOn);

            var again = System.Text.Json.JsonSerializer.Deserialize<PluginSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
            again.LiveInput.Restore = LiveInputSettings.RestoreAsk;
            var againEngine = LiveInputFakeEngine();
            var againHost = new LiveInputTestHost(againEngine, again, DocumentSession.Blank()) { Answer = (false, false) };
            using (var session = new LiveInputController(againHost))
            {
                session.TurnOn(); againEngine.LiveInput.Disarm(); session.TurnOn();
                var askedOnce = againHost.Asked;
                again.LiveInput = new LiveInputSettings { Chain = again.LiveInput.Chain, Restore = LiveInputSettings.RestoreAsk };   // as a Preferences apply replaces the object
                againEngine.LiveInput.Disarm(); session.TurnOn();
                var askedAfterReplace = againHost.Asked;
                again.LiveInput.Restore = LiveInputSettings.RestoreNever;
                againEngine.LiveInput.Disarm(); session.TurnOn();
                again.LiveInput.Restore = LiveInputSettings.RestoreAsk;
                againEngine.LiveInput.Disarm(); session.TurnOn();
                Check("live input: Ask asks once per run, not again when the settings object is replaced with the same choice, and again after the choice went to Never and back to Ask",
                    askedOnce == 1 && askedAfterReplace == 1 && againHost.Asked == 2, $"{askedOnce} {askedAfterReplace} {againHost.Asked}");
            }
            againEngine.Stop();

            var askYes = NewSession(LiveInputSettings.RestoreAsk, (true, false));
            var askNoRemember = NewSession(LiveInputSettings.RestoreAsk, (false, true), twice: true);
            var askYesRemember = NewSession(LiveInputSettings.RestoreAsk, (true, true));
            var always = NewSession(LiveInputSettings.RestoreAlways, (false, false));
            var never = NewSession(LiveInputSettings.RestoreNever, (true, false));
            Check("live input: Ask asks once; Yes restores the saved chain and keeps asking next time", askYes == (1, 1, LiveInputSettings.RestoreAsk, true), $"{askYes}");
            Check("live input: No with Remember starts empty, stores Never and does not ask again in the session", askNoRemember == (0, 1, LiveInputSettings.RestoreNever, true), $"{askNoRemember}");
            Check("live input: Yes with Remember stores Always", askYesRemember == (1, 1, LiveInputSettings.RestoreAlways, true), $"{askYesRemember}");
            Check("live input: Always restores and Never starts empty, both without asking", always == (1, 0, LiveInputSettings.RestoreAlways, true) && never == (0, 0, LiveInputSettings.RestoreNever, true), $"{always} {never}");

            TestLiveInputMidi();

            Check("live input: the command is in the catalogue without a default key, and the Preferences row defaults to Ask",
                HotkeyCatalog.All.Any(c => c.Id == LiveInputFeatureModule.CommandId) && !HotkeyCatalog.BuildMap(new AppSettings().Hotkeys).Values.Contains(LiveInputFeatureModule.CommandId)
                && SettingsCatalog.LiveInputRows(new AppSettings()).Count == 1 && new LiveInputSettings().Restore == LiveInputSettings.RestoreAsk);
        }
        finally { client.SentForTest = null; client.Stop(); }
    }

    /// <summary>Live input with a MIDI keyboard: a fake device set, a fake engine; the messages name the stand-in's slot.</summary>
    private static void TestLiveInputMidi()
    {
        var set = new FakeMidiDeviceSet { Present = new() { "Piano" } };
        var hub = new MidiInputHub(set);
        var engine = LiveInputFakeEngine();
        var written = new List<TimedMidi>();
        try
        {
            var track = engine.LiveInput.Track;
            var midi = engine.LiveInput.Midi = new LiveInputMidi(engine, track, hub);
            engine.WrittenForTest = written.Add;
            var settings = new PluginSettings { PlayAllThroughEngine = false };
            settings.LiveInput.OpenFxOnTurnOn = false;
            var host = new LiveInputTestHost(engine, settings, DocumentSession.Blank());
            using var c = new LiveInputController(host);
            List<string> MidiRows() => LiveInputMenu.Build(c).Items.OfType<System.Windows.Controls.MenuItem>().Select(r => (string)r.Header).Where(h => h.StartsWith("MIDI: ")).ToList();
            bool IsReset(int from, int slot, int channels = 1) => written.Count == from + 1 + 5 * channels && written.Skip(from).All(m => m.Slot == slot) && written[from].Flags == TimedMidi.PanicFlag && written[from + 1].Data1 == 64 && written[from + 2].Data1 == 123;

            bool Ticked(string header) => LiveInputMenu.Build(c).Items.OfType<System.Windows.Controls.MenuItem>().Single(r => (string)r.Header == header).IsChecked;

            c.SetMidiInput("");
            Check("live input MIDI: ticking a MIDI row beside the audio input saves both and opens nothing while off",
                settings.LiveInput.Input == AudioInputs.Input1 && settings.LiveInput.MidiDevice == "" && host.Saves == 1 && c.HasUsableInput && !midi.IsOn && hub.HeldCount == 0);
            c.Toggle();
            var slot = engine.SlotOf(track);
            Check("live input MIDI: on with both opens one listener and the stand-in monitors the audio input in the same slot; no instrument is said",
                c.IsOn && midi.IsListening && hub.HeldCount == 1 && slot >= 0 && track.AudioInput == AudioInputs.Input1 && track.RecordArm
                && host.Status == "Live input: add an instrument plug-in to hear the MIDI keyboard", host.Status);

            midi.Play(0x93, 60, 100); midi.Play(0x83, 60, 0);
            set.Fire("Piano", 0x90, 62);   // the driver thread's path: queued for the UI thread
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Check("live input MIDI: a note on and off reach the engine at the stand-in's slot and no song track's",
                written.Count == 3 && written.All(m => m.Slot == slot) && written[0].Status == 0x93 && written[1].Status == 0x83 && written[2].Data1 == 62
                && host.Doc.Project.Tracks.All(t => engine.SlotOf(t) != slot), $"{written.Count}");

            c.SetInput(AudioInputs.Input2);
            Check("live input MIDI: another audio row unticks the first and leaves the MIDI choice",
                !Ticked(AudioInputs.Input1) && Ticked(AudioInputs.Input2) && Ticked("MIDI: Any available device") && track.AudioInput == AudioInputs.Input2 && midi.IsListening && hub.HeldCount == 1);

            var before = MidiRows();
            set.Present.Add("Pads");
            Check("live input MIDI: the menu lists the devices connected at each opening",
                before.SequenceEqual(new[] { "MIDI: Any available device", "MIDI: Piano" }) && MidiRows().SequenceEqual(new[] { "MIDI: Any available device", "MIDI: Piano", "MIDI: Pads" }));

            written.Clear();
            var stale = midi.Epoch;
            c.SetMidiInput("Piano");
            var switched = IsReset(0, slot, 2) && written.Count(m => m.Status == 0xB3 && m.Data1 == 123) == 1 && written.Count(m => m.Status == 0xB0 && m.Data1 == 123 && m.Flags == 0) == 1;
            midi.Play(0x90, 61, 100, stale);
            Check("live input MIDI: a device switch resets every channel played (1 and 4) after the slot's all notes off, and a message queued before it is dropped",
                switched && written.Count == 11, $"{written.Count}");
            midi.Play(0x90, 60, 100);
            set.Present.Remove("Piano"); written.Clear();
            midi.Poll();
            Check("live input MIDI: the device disappearing lets the notes go, closes the listener and is said",
                IsReset(0, slot) && !midi.IsListening && midi.IsOn && hub.HeldCount == 0 && host.Status.Contains("Piano disconnected") && MidiRows().Contains("MIDI: Piano (not connected)"), host.Status);
            set.Present.Add("Piano");
            midi.Poll();
            Check("live input MIDI: the device appearing while on gets a listener", midi.IsListening && midi.Device == "Piano" && hub.HeldCount == 1 && host.Status.Contains("Piano connected") && c.IsOn, host.Status);

            midi.Play(0x90, 60, 100); written.Clear();
            c.SetMidiInput("Piano");
            Check("live input MIDI: the ticked MIDI row again turns MIDI off: the notes are let go and the listener closes; the audio input stays",
                IsReset(0, slot) && !midi.IsOn && hub.HeldCount == 0 && settings.LiveInput.MidiDevice == null && c.IsOn && track.AudioInput == AudioInputs.Input2 && c.HasUsableInput);
            c.SetInput(AudioInputs.Input2);
            Check("live input MIDI: with neither ticked nothing is usable and live input turns off, saying why",
                settings.LiveInput.Input == "" && !c.HasUsableInput && !Ticked(AudioInputs.Input2) && !c.IsOn && host.Status == "Live input off: no input chosen", host.Status);

            c.SetMidiInput("");
            c.TurnOn();
            slot = engine.SlotOf(track);
            midi.Play(0x90, 60, 100); written.Clear();
            set.Present.Remove("Pads");
            midi.Poll();
            Check("live input MIDI: with any device, one of two unplugged lets the notes go and the other is still heard", IsReset(0, slot) && midi.IsListening && c.IsOn);
            midi.Play(0x90, 60, 100); written.Clear();
            var only = midi.IsListening && midi.Device == null && track.AudioInput == "" && c.HasUsableInput && slot >= 0;
            c.TurnOff();
            Check("live input MIDI: MIDI alone keeps the stand-in's slot without an audio input; off sends the reset to the slot and closes the listener",
                only && IsReset(0, slot) && !midi.IsOn && hub.HeldCount == 0 && !c.IsOn);

            c.SetMidiInput("Gone");
            c.TurnOn();
            Check("live input MIDI: a saved device that is not connected still turns on, waits and is said",
                c.IsOn && midi.IsOn && !midi.IsListening && host.Status.Contains("Gone is not connected"), host.Status);
            midi.Play(0x90, 60, 100); written.Clear();
            engine.LiveInput.Disarm(); engine.LiveInput.Release(); host.SyncAudioEngine();
            var gone = engine.SlotOf(track) < 0;
            midi.Stop();
            Check("live input MIDI: the reset asks for the stand-in's slot when it is sent (nothing to a slot the stand-in has left)", gone && written.Count == 0, $"{gone} {written.Count}");
        }
        finally { engine.WrittenForTest = null; engine.LiveInput.Midi?.Stop(); engine.Stop(); }
    }
}
