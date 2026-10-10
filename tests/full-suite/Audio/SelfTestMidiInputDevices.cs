using TabForge.Audio;

namespace TabForge;

// Owns: the shared MIDI input's device selection checks: any versus named clients, duplicate names, rescan adding and dropping devices, no-change rescan, close with the last client.
//   A fake device set, never a real MIDI device.
// Does not own: the hub's client sharing (SelfTestKeyboardModeMidi.cs) or the NAudio handles.
// Tests: TestMidiInputDeviceFilter.
public static partial class SelfTest
{
    private sealed class FakeMidiDeviceSet : IMidiDeviceSet
    {
        public List<string> Present = new() { "Piano" };
        public List<string> Open_ = new();
        public int CloseCalls;
        public event Action<int, int, int, long>? Message;
        public event Action<string, int, int, int, long>? DeviceMessage;
        public bool IsOpen => Open_.Count > 0;
        public IReadOnlyList<string> OpenNames => Open_.ToArray();
        public IReadOnlyList<string> ListNames() => Present.ToArray();
        public int Open() { if (!IsOpen) Open_ = Present.ToList(); return Open_.Count; }
        public void Close() { CloseCalls++; Open_.Clear(); }
        public void Dispose() => Close();
        public bool Rescan()
        {
            if (!IsOpen) return false;
            if (Present.SequenceEqual(Open_)) return false;
            Open_ = Present.ToList();
            return true;
        }
        public void Fire(string device, int status, int d1) { DeviceMessage?.Invoke(device, status, d1, 100, 1); Message?.Invoke(status, d1, 100, 1); }
    }

    private static void TestMidiInputDeviceFilter()
    {
        var set = new FakeMidiDeviceSet { Present = new() { "Piano", "Pads", "Pads" } };
        var hub = new MidiInputHub(set);
        var any = hub.CreateClient(); var piano = hub.CreateClient(); var pads = hub.CreateClient();
        var heardAny = new List<int>(); var heardPiano = new List<int>(); var heardPads = new List<int>();
        any.Message += (_, d1, _, _) => heardAny.Add(d1);
        piano.Message += (_, d1, _, _) => heardPiano.Add(d1);
        pads.Message += (_, d1, _, _) => heardPads.Add(d1);
        piano.Device = "Piano"; pads.Device = "Pads";
        Check("devices: names listed before opening", hub.DeviceNames().SequenceEqual(new[] { "Piano", "Pads", "Pads" }) && hub.OpenDeviceNames.Count == 0);
        any.Open(); piano.Open(); pads.Open();
        Check("devices: open names are the devices opened", hub.OpenDeviceNames.SequenceEqual(new[] { "Piano", "Pads", "Pads" }));
        set.Fire("Piano", 0x90, 60); set.Fire("Pads", 0x90, 36); set.Fire("Pads", 0x90, 37);
        Check("devices: any hears every device", heardAny.SequenceEqual(new[] { 60, 36, 37 }));
        Check("devices: a named client hears only its device (both identical devices count)", heardPiano.SequenceEqual(new[] { 60 }) && heardPads.SequenceEqual(new[] { 36, 37 }));
        piano.Device = null; set.Fire("Pads", 0x90, 38);
        Check("devices: setting Device to null while open makes the client hear everything", heardPiano.SequenceEqual(new[] { 60, 38 }));
        piano.Device = "Piano";

        Check("devices: rescan with nothing changed returns false", !hub.Rescan());
        set.Present.Add("Synth");
        Check("devices: rescan opens a device connected since", hub.Rescan() && hub.OpenDeviceNames.Contains("Synth"));
        set.Fire("Synth", 0x90, 72);
        Check("devices: any hears the new device, a client on another device does not", heardAny.Last() == 72 && heardPiano.Last() == 38 && !heardPads.Contains(72));
        set.Present.Remove("Pads");
        Check("devices: rescan drops a device that went", hub.Rescan() && hub.OpenDeviceNames.Count == 3 && !hub.Rescan());

        var plain = new FakeMidiDevice();
        var plainHub = new MidiInputHub(plain);
        var pa = plainHub.CreateClient(); var pn = plainHub.CreateClient();
        pn.Device = "Piano";
        var gotA = 0; var gotN = 0;
        pa.Message += (_, _, _, _) => gotA++; pn.Message += (_, _, _, _) => gotN++;
        pa.Open(); pn.Open(); plain.Fire(0x90, 60, 100, 1);
        Check("devices: a plain source is one unnamed device: any hears it, a named client does not; rescan is false", gotA == 1 && gotN == 0 && !plainHub.Rescan() && plainHub.DeviceNames().Count == 0);

        any.Close(); piano.Close();
        Check("devices: the set stays open while a client holds it", set.IsOpen);
        pads.Close();
        Check("devices: closing the last client closes the set", !set.IsOpen && set.CloseCalls == 1 && hub.OpenDeviceNames.Count == 0 && !hub.Rescan());
    }
}
