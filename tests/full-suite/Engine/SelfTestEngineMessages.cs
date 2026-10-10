using System.IO;
using TabForge.Audio.Contracts;

namespace TabForge;

/// <summary>
/// The engine command pairs (EngineMessages.cs, EngineMessageLists.cs): every pair reads back what it wrote, and its bytes are the bytes the
/// hand-written positional code wrote before the pairs existed (the legacy writers below are that code, kept as the reference).
/// Part of <see cref="SelfTest"/>.
/// </summary>
public static partial class SelfTest
{
    private sealed record MessageCase(string Name, Action<BinaryWriter> Legacy, Action<BinaryWriter> Pair, Func<BinaryReader, bool> ReadBack, string Hex);

    private static readonly TransportBar[] SampleBars = { new(0, 0, 120, 4, 4), new(2, 4, 90, 3, 4) };
    private static readonly List<ClipSpec> SampleClips = new() { new("a.wav", 1, 2, 3, -6, 1, 1.5, 0.1, 0.2) };
    private static readonly MidiProcSpec[] SampleProcs = { new("Arp", true, "{\"rate\":2}") };

    private static List<MessageCase> EngineMessageCases() => new()
    {
        new("Ping", w => w.Write(7), w => new PingMessage(7).Write(w), r => PingMessage.Read(r) == new PingMessage(7), "07000000"),
        new("TestHang", w => w.Write(3), w => new TestHangMessage(3).Write(w), r => TestHangMessage.Read(r) == new TestHangMessage(3), "03000000"),
        new("SetMasterTap", w => w.Write(true), w => new SetMasterTapMessage(true).Write(w), r => SetMasterTapMessage.Read(r) == new SetMasterTapMessage(true), "01"),
        new("SetLiveLimiter", w => w.Write(true), w => new SetLiveLimiterMessage(true).Write(w), r => SetLiveLimiterMessage.Read(r) == new SetLiveLimiterMessage(true), "01"),
        new("SetWindowsPathOffset", w => w.Write(-3.5f), w => new SetWindowsPathOffsetMessage(-3.5f).Write(w), r => SetWindowsPathOffsetMessage.Read(r) == new SetWindowsPathOffsetMessage(-3.5f), "000060C0"),
        new("RemoveTrack", w => w.Write(9), w => new RemoveTrackMessage(9).Write(w), r => RemoveTrackMessage.Read(r) == new RemoveTrackMessage(9), "09000000"),
        new("GetStates", w => { w.Write(2); w.Write(5); }, w => new GetStatesMessage(2, 5).Write(w), r => GetStatesMessage.Read(r) == new GetStatesMessage(2, 5), "0200000005000000"),
        new("CloseEditor", w => { w.Write(2); w.Write(1); }, w => new CloseEditorMessage(2, 1).Write(w), r => CloseEditorMessage.Read(r) == new CloseEditorMessage(2, 1), "0200000001000000"),
        new("GetPrograms", w => { w.Write(2); w.Write(1); }, w => new GetProgramsMessage(2, 1).Write(w), r => GetProgramsMessage.Read(r) == new GetProgramsMessage(2, 1), "0200000001000000"),
        new("SetProgram", w => { w.Write(2); w.Write(1); w.Write(40); }, w => new SetProgramMessage(2, 1, 40).Write(w), r => SetProgramMessage.Read(r) == new SetProgramMessage(2, 1, 40), "020000000100000028000000"),
        new("SetState", w => { w.Write(2); w.Write(1); w.WriteString("QUJD"); }, w => new SetStateMessage(2, 1, "QUJD").Write(w), r => SetStateMessage.Read(r) == new SetStateMessage(2, 1, "QUJD"), "02000000010000000451554A44"),
        new("OpenEditor", w => { w.Write(2); w.Write(1); w.Write(0x1234L); w.Write(true); w.Write(false); w.Write(true); },
            w => new OpenEditorMessage(2, 1, 0x1234L, true, false, true).Write(w), r => OpenEditorMessage.Read(r) == new OpenEditorMessage(2, 1, 0x1234L, true, false, true), "0200000001000000341200000000000001" + "0001"),
        new("SetTrackMix", w => { w.Write(2); w.Write(100); w.Write(64); }, w => new SetTrackMixMessage(2, 100, 64).Write(w), r => SetTrackMixMessage.Read(r) == new SetTrackMixMessage(2, 100, 64), "020000006400000040000000"),
        new("SetPluginGain", w => { w.Write(2); w.Write(1); w.Write(-6.5); }, w => new SetPluginGainMessage(2, 1, -6.5).Write(w), r => SetPluginGainMessage.Read(r) == new SetPluginGainMessage(2, 1, -6.5), "02000000010000000000000000001AC0"),
        new("SetPluginBypass", w => { w.Write(2); w.Write(1); w.Write(true); }, w => new SetPluginBypassMessage(2, 1, true).Write(w), r => SetPluginBypassMessage.Read(r) == new SetPluginBypassMessage(2, 1, true), "020000000100000001"),
        new("SetSynth", w => { w.Write(2); w.Write(true); }, w => new SetSynthMessage(2, true).Write(w), r => SetSynthMessage.Read(r) == new SetSynthMessage(2, true), "0200000001"),
        new("SetMidiRoute", w => { w.Write(2); w.Write(1); w.Write(-1); w.Write(0xFFFF); }, w => new SetMidiRouteMessage(2, 1, -1, 0xFFFF).Write(w), r => SetMidiRouteMessage.Read(r) == new SetMidiRouteMessage(2, 1, -1, 0xFFFF), "0200000001000000FFFFFFFFFFFF0000"),
        new("SetPluginWiring", w => { w.Write(2); w.Write(1); w.Write(3); }, w => new SetPluginWiringMessage(2, 1, 3).Write(w), r => SetPluginWiringMessage.Read(r) == new SetPluginWiringMessage(2, 1, 3), "020000000100000003000000"),
        new("SetMidiLogWatch", w => { w.Write(2); w.Write(true); }, w => new SetMidiLogWatchMessage(2, true).Write(w), r => SetMidiLogWatchMessage.Read(r) == new SetMidiLogWatchMessage(2, true), "0200000001"),
        new("SetArm", w => { w.Write(2); w.Write(true); w.Write(1); w.Write(false); }, w => new SetArmMessage(2, true, 1, false).Write(w), r => SetArmMessage.Read(r) == new SetArmMessage(2, true, 1, false), "02000000010100000000"),
        new("SetPosition", w => { w.Write(true); w.Write(12.5); w.Write(99L); w.Write(3); }, w => new SetPositionMessage(true, 12.5, 99L, 3).Write(w),
            r => SetPositionMessage.Read(r) == new SetPositionMessage(true, 12.5, 99L, 3), "010000000000002940630000000000000003000000"),
        new("SetTransport (with bar map)", w => { w.Write(120.0); w.Write(true); TransportMap.Write(w, SampleBars); w.Write(3); }, w => new SetTransportMessage(120.0, true, SampleBars, 3).Write(w),
            r => SetTransportMessage.Read(r) is { Tempo: 120.0, Playing: true, Bars: { Length: 2 } b, Owner: 3 } && b[1] == SampleBars[1], "0000000000005E400102000000000000000000000000000000000000000000000000005E40040004000000000000000040000000000000104000000000008056400300040003000000"),
        new("SetTransport (tempo only)", w => { w.Write(120.0); w.Write(true); }, w => SetTransportMessage.WriteTempoOnly(w, 120.0, true),
            r => SetTransportMessage.Read(r) is { Tempo: 120.0, Playing: true, Bars: null, Owner: 0 }, "0000000000005E4001"),
        new("SetClips", w => { w.Write(2); w.Write(SampleClips); w.Write(3); }, w => new SetClipsMessage(2, SampleClips, 3).Write(w),
            r => SetClipsMessage.Read(r) is { Slot: 2, Owner: 3, Clips: { Count: 1 } c } && c[0] == SampleClips[0], "020000000100000005612E776176000000000000F03F0000000000000040000000000000084000000000000018C0000000000000F03F000000000000F83F9A9999999999B93F9A9999999999C93F03000000"),
        new("Record (start)", w => { w.Write(true); w.WriteString("rec"); w.Write(1); w.Write(4); w.WriteString("Gtr"); w.Write(12.5); w.Write(2); },
            w => new RecordMessage(true, "rec", new[] { (4, "Gtr") }, 12.5, 2).Write(w),
            r => RecordMessage.Read(r) is { Start: true, Folder: "rec", OffsetMs: 12.5, Owner: 2, Tracks: { Count: 1 } t } && t[0] == (4, "Gtr"), "0103726563010000000400000003477472000000000000294002000000"),
        new("Record (stop)", w => { w.Write(false); w.WriteString(""); w.Write(0); }, RecordMessage.WriteStop,
            r => RecordMessage.Read(r) is { Start: false, Folder: "", OffsetMs: 0, Owner: 0, Tracks.Count: 0 }, "000000000000"),
        new("PanicSlots", w => { w.Write(2); w.Write(3); w.Write(5); }, w => new PanicSlotsMessage(new[] { 3, 5 }).Write(w),
            r => PanicSlotsMessage.Read(r).Slots.SequenceEqual(new[] { 3, 5 }), "020000000300000005000000"),
        new("SetAutoPitch", w => { w.Write(2); w.Write(1); w.Write(0); w.Write(-12); }, w => new SetAutoPitchMessage(2, new[] { (0, -12) }).Write(w),
            r => SetAutoPitchMessage.Read(r) is { Slot: 2, Transposes: { Count: 1 } t } && t[0] == (0, -12), "020000000100000000000000F4FFFFFF"),
        new("MeasurePitch", w => { w.Write(2); w.Write(1); w.Write(8); w.Write(0); w.Write(2); w.Write(60); w.Write(64); }, w => new MeasurePitchMessage(2, 1, 8, 0, new[] { 60, 64 }).Write(w),
            r => MeasurePitchMessage.Read(r) is { Slot: 2, Index: 1, RequestId: 8, Channel: 0, Notes: { Count: 2 } n } && n[0] == 60 && n[1] == 64, "02000000010000000800000000000000020000003C00000040000000"),
        new("SetMidiProcessors", w => { w.Write(2); w.Write(1); w.Write(0); w.Write(SampleProcs); }, w => new SetMidiProcessorsMessage(2, new[] { (0, (IReadOnlyList<MidiProcSpec>)SampleProcs) }).Write(w),
            r => SetMidiProcessorsMessage.Read(r, 100, out var valid) is { Slot: 2, Lists: { Count: 1 } l } && valid && l[0].Index == 0 && l[0].Specs[0] == SampleProcs[0], "0200000001000000000000000100000003417270010A7B2272617465223A327D"),
        new("SetGraph", w => { w.Write(1); w.Write(2); w.Write(260); w.Write(1); w.Write(3); w.Write(0); w.Write(1); w.Write(1); w.Write(1); w.Write(4); w.Write(0); },
            w => new SetGraphMessage(new[] { (2, 260) }, new[] { (3, 0, 1) }, new[] { (1, 4, 0) }).Write(w),
            r => SetGraphMessage.Read(r, 1000) is { Dests: { Count: 1 } d, Sidechains: { Count: 1 } s, Forwards: { Count: 1 } f } && d[0] == (2, 260) && s[0] == (3, 0, 1) && f[0] == (1, 4, 0), "0100000002000000040100000100000003000000000000000100000001000000010000000400000000000000"),
    };

    private static byte[] EngineMessageBytes(Action<BinaryWriter> write)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) write(w);
        return ms.ToArray();
    }

    /// <summary>Each command pair writes what the old positional code wrote, and reads back the same message (all fields, the whole frame consumed).</summary>
    private static void TestEngineMessagesRoundTrip()
    {
        foreach (var c in EngineMessageCases())
        {
            var bytes = EngineMessageBytes(c.Pair);
            using var ms = new MemoryStream(bytes);
            using var r = new BinaryReader(ms);
            var ok = c.ReadBack(r);
            Check($"engine message {c.Name}: reads back every field it wrote, and consumes the whole payload", ok && ms.Position == ms.Length);
        }
        // A short SetPosition (no owner tail) and an out-of-range owner both read as owner 0.
        using (var ms = new MemoryStream(EngineMessageBytes(w => { w.Write(true); w.Write(1.0); w.Write(2L); })))
            Check("engine message SetPosition: no owner tail reads as owner 0", SetPositionMessage.Read(new BinaryReader(ms)).Owner == 0);
        using (var ms = new MemoryStream(EngineMessageBytes(w => new SetPositionMessage(true, 1.0, 2L, SongOwners.Max + 1).Write(w))))
            Check("engine message SetPosition: an out-of-range owner reads as owner 0", SetPositionMessage.Read(new BinaryReader(ms)).Owner == 0);
        using (var ms = new MemoryStream(EngineMessageBytes(w => w.Write(1))))
            Check("engine message SetTrackMix: a truncated payload throws instead of reading garbage", Throws<EndOfStreamException>(() => SetTrackMixMessage.Read(new BinaryReader(ms))));
    }

    /// <summary>The bytes of every command are pinned (the wire format cannot change silently): the pair equals the legacy writer and a recorded hex string.</summary>
    private static void TestEngineMessagesGolden()
    {
        foreach (var c in EngineMessageCases())
        {
            var legacy = EngineMessageBytes(c.Legacy);
            var pair = EngineMessageBytes(c.Pair);
            Check($"engine message {c.Name}: bytes equal the legacy positional writer", legacy.SequenceEqual(pair));
            Check($"engine message {c.Name}: bytes are the pinned golden bytes", Convert.ToHexString(pair) == c.Hex, Convert.ToHexString(pair));
        }
    }

    private static bool Throws<T>(Action a) where T : Exception { try { a(); return false; } catch (T) { return true; } }
}
