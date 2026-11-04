using System;
using System.Collections.Generic;
using System.Text;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the GP5 automation baseline for the mix-table scanner (volume/pan/effects, tempo and transitions, flags and bounds) on synthetic byte buffers.
// Does not own: the file import itself, the Score matching (Scan) or any sample file.
// Tests: GuitarProMixTableScanner (TryParse + TempoRampFinder only, no sample files).
// Env: none (every buffer is built in the test; no TABFORGE_SAMPLES_DIR, no temp files, no app launch).
public sealed class GuitarProMixTableScannerBaselineTests
{
    private static byte[] Gp4Mix(int v0, int v1, int v2, int v3, int v4, int v5, int tempo, int[] durations, byte flags)
    {
        var bytes = new List<byte>(16);
        foreach (var v in new[] { v0, v1, v2, v3, v4, v5 })
            bytes.Add(unchecked((byte)(sbyte)v));
        bytes.AddRange(BitConverter.GetBytes(tempo));
        foreach (var d in durations)
            bytes.Add(unchecked((byte)(sbyte)d));
        bytes.Add(flags);
        return bytes.ToArray();
    }

    private static byte[] Gp5Mix(int v0, int v1, int v2, int v3, int v4, int v5, byte[] nameContent, int tempo, int[] durations, byte? hideByte, byte flags)
    {
        var bytes = new List<byte>(32);
        foreach (var v in new[] { v0, v1, v2, v3, v4, v5 })
            bytes.Add(unchecked((byte)(sbyte)v));
        bytes.AddRange(BitConverter.GetBytes(nameContent.Length));
        bytes.AddRange(nameContent);
        bytes.AddRange(BitConverter.GetBytes(tempo));
        foreach (var d in durations)
            bytes.Add(unchecked((byte)(sbyte)d));
        if (hideByte.HasValue)
            bytes.Add(hideByte.Value);
        bytes.Add(flags);
        return bytes.ToArray();
    }

    private static byte[] FileWithVersion(string versionText, params byte[][] mixes)
    {
        var versionBytes = Encoding.ASCII.GetBytes(versionText);
        var bytes = new List<byte>(128);
        bytes.Add((byte)versionBytes.Length);
        bytes.AddRange(versionBytes);
        while (bytes.Count < 32)
            bytes.Add(0x80);
        foreach (var mix in mixes)
        {
            bytes.AddRange(mix);
            for (var i = 0; i < 16; i++)
                bytes.Add(0x80);
        }

        return bytes.ToArray();
    }

    // ---------- volume / pan / effects ----------

    [Fact]
    public void Gp4_VolumeOnly_KeepsVolumeAndTransition()
    {
        var data = Gp4Mix(8, -1, -1, -1, -1, -1, -1, new[] { 16 }, 0);
        Assert.True(GuitarProMixTableScanner.TryParse(data, 0, gp5: false, hideTempoByte: false, out var mix, out var end));
        Assert.Equal(8, mix.Volume);
        Assert.Equal(-1, mix.Balance);
        Assert.Equal(-1, mix.Chorus);
        Assert.Equal(-1, mix.Tempo);
        Assert.Equal(16, mix.TransitionBeats);
        Assert.Equal(0, mix.TempoTransitionBeats);
        Assert.False(mix.AllTracks);
        Assert.Equal(data.Length, end);
    }

    [Fact]
    public void Gp4_BalanceOnly_KeepsPanValue()
    {
        var data = Gp4Mix(-1, 4, -1, -1, -1, -1, -1, new[] { 10 }, 0);
        Assert.True(GuitarProMixTableScanner.TryParse(data, 0, gp5: false, hideTempoByte: false, out var mix, out _));
        Assert.Equal(-1, mix.Volume);
        Assert.Equal(4, mix.Balance);
        Assert.Equal(10, mix.TransitionBeats);
        Assert.False(mix.AllTracks);
    }

    [Fact]
    public void Gp4_FullEffects_PreservesAllSixValues()
    {
        var data = Gp4Mix(8, 4, 6, 7, 3, 5, -1, new[] { 16, 15, 14, 13, 12, 11 }, 0);
        Assert.True(GuitarProMixTableScanner.TryParse(data, 0, gp5: false, hideTempoByte: false, out var mix, out var end));
        Assert.Equal(8, mix.Volume);
        Assert.Equal(4, mix.Balance);
        Assert.Equal(6, mix.Chorus);
        Assert.Equal(7, mix.Reverb);
        Assert.Equal(3, mix.Phaser);
        Assert.Equal(5, mix.Tremolo);
        Assert.Equal(16, mix.TransitionBeats);
        Assert.Equal(6 + 4 + 6 + 1, end);
    }

    [Fact]
    public void Gp4_AbsentEffect_SkipsItsDurationByte()
    {
        // Volume 8 over 9 beats and chorus 5 over 7 beats; balance absent so its duration is not stored.
        var data = Gp4Mix(8, -1, 5, -1, -1, -1, -1, new[] { 9, 7 }, 0);
        Assert.True(GuitarProMixTableScanner.TryParse(data, 0, gp5: false, hideTempoByte: false, out var mix, out _));
        Assert.Equal(8, mix.Volume);
        Assert.Equal(-1, mix.Balance);
        Assert.Equal(5, mix.Chorus);
        Assert.Equal(9, mix.TransitionBeats);
    }

    [Fact]
    public void Gp5_VolumePanEffects_WithTempoName_Parses()
    {
        var data = Gp5Mix(8, 4, -1, 6, -1, -1, new byte[] { 0x00 }, -1, new[] { 12, 11, 10 }, null, 0);
        Assert.True(GuitarProMixTableScanner.TryParse(data, 0, gp5: true, hideTempoByte: false, out var mix, out _));
        Assert.Equal(8, mix.Volume);
        Assert.Equal(4, mix.Balance);
        Assert.Equal(-1, mix.Chorus);
        Assert.Equal(6, mix.Reverb);
        Assert.Equal(12, mix.TransitionBeats);
    }

    // ---------- tempo and transitions ----------

    [Fact]
    public void Gp4_TempoChange_SeparatesVolumeAndTempoTransitions()
    {
        var data = Gp4Mix(8, -1, -1, -1, -1, -1, 120, new[] { 16, 4 }, 0);
        Assert.True(GuitarProMixTableScanner.TryParse(data, 0, gp5: false, hideTempoByte: false, out var mix, out _));
        Assert.Equal(120, mix.Tempo);
        Assert.Equal(16, mix.TransitionBeats);
        Assert.Equal(4, mix.TempoTransitionBeats);
    }

    [Fact]
    public void Gp4_TempoOnly_HasZeroVolumeTransition()
    {
        var data = Gp4Mix(-1, -1, -1, -1, -1, -1, 90, new[] { 5 }, 0);
        Assert.True(GuitarProMixTableScanner.TryParse(data, 0, gp5: false, hideTempoByte: false, out var mix, out _));
        Assert.Equal(90, mix.Tempo);
        Assert.Equal(0, mix.TransitionBeats);
        Assert.Equal(5, mix.TempoTransitionBeats);
        Assert.False(mix.AllTracks);
    }

    [Fact]
    public void Gp4_FirstPresent_ControlsTransitionBeats()
    {
        var data = Gp4Mix(-1, 4, -1, -1, -1, -1, -1, new[] { 11 }, 0);
        Assert.True(GuitarProMixTableScanner.TryParse(data, 0, gp5: false, hideTempoByte: false, out var mix, out _));
        Assert.Equal(11, mix.TransitionBeats);
    }

    [Fact]
    public void Gp5_HideTempoByte_ConsumesExtraByte()
    {
        var data = Gp5Mix(8, -1, -1, -1, -1, -1, new byte[] { 0x00 }, 100, new[] { 16, 8 }, (byte)1, (byte)1);
        Assert.True(GuitarProMixTableScanner.TryParse(data, 0, gp5: true, hideTempoByte: true, out var mix, out var end));
        Assert.Equal(100, mix.Tempo);
        Assert.Equal(16, mix.TransitionBeats);
        Assert.Equal(8, mix.TempoTransitionBeats);
        Assert.True(mix.AllTracks);
        Assert.Equal(data.Length, end);

        // The same bytes without the hide step treat the hide byte as the flags byte.
        Assert.True(GuitarProMixTableScanner.TryParse(data, 0, gp5: true, hideTempoByte: false, out var noHide, out var noHideEnd));
        Assert.Equal(data.Length - 1, noHideEnd);
        Assert.True(noHide.AllTracks);
    }

    [Fact]
    public void Gp5_WithoutHideByte_ParseWithHideFailsWhenTruncated()
    {
        var data = Gp5Mix(8, -1, -1, -1, -1, -1, new byte[] { 0x00 }, 100, new[] { 16, 8 }, null, (byte)1);
        Assert.True(GuitarProMixTableScanner.TryParse(data, 0, gp5: true, hideTempoByte: false, out _, out _));
        // Reading one more hide byte runs past the flags byte.
        Assert.False(GuitarProMixTableScanner.TryParse(data, 0, gp5: true, hideTempoByte: true, out _, out _));
    }

    [Fact]
    public void TempoRampFinder_FindsTransitionsInFileOrder()
    {
        var first = Gp4Mix(8, -1, -1, -1, -1, -1, 120, new[] { 16, 4 }, 0);
        var second = Gp4Mix(4, -1, -1, -1, -1, -1, 90, new[] { 12, 8 }, 0);
        var file = FileWithVersion("FICHIER GUITAR PRO v4.00", first, second);
        var finder = new GuitarProMixTableScanner.TempoRampFinder(file);
        Assert.Equal(4, finder.Next(120));
        Assert.Equal(8, finder.Next(90));
        Assert.Equal(0, finder.Next(120));
    }

    [Fact]
    public void TempoRampFinder_ReturnsZero_WhenTempoMissing()
    {
        var only = Gp4Mix(8, -1, -1, -1, -1, -1, 120, new[] { 16, 4 }, 0);
        var file = FileWithVersion("FICHIER GUITAR PRO v4.00", only);
        var finder = new GuitarProMixTableScanner.TempoRampFinder(file);
        Assert.Equal(0, finder.Next(90));
        Assert.Equal(4, finder.Next(120));
    }

    [Fact]
    public void TempoRampFinder_NonGpFile_ReturnsZero()
    {
        var empty = new GuitarProMixTableScanner.TempoRampFinder(Array.Empty<byte>());
        Assert.Equal(0, empty.Next(120));

        var v3 = FileWithVersion("FICHIER GUITAR PRO v3.00", Gp4Mix(8, -1, -1, -1, -1, -1, 120, new[] { 16, 4 }, 0));
        Assert.Equal(0, new GuitarProMixTableScanner.TempoRampFinder(v3).Next(120));
    }

    [Fact]
    public void TempoRampFinder_Gp5HideByte_StillFindsTempoTransition()
    {
        var mix = Gp5Mix(8, -1, -1, -1, -1, -1, new byte[] { 0x00 }, 100, new[] { 16, 8 }, (byte)0, (byte)0);
        var file = FileWithVersion("FICHIER GUITAR PRO v5.10", mix);
        Assert.Equal(8, new GuitarProMixTableScanner.TempoRampFinder(file).Next(100));

        var mix500 = Gp5Mix(8, -1, -1, -1, -1, -1, new byte[] { 0x00 }, 100, new[] { 16, 8 }, null, (byte)0);
        var file500 = FileWithVersion("FICHIER GUITAR PRO v5.00", mix500);
        Assert.Equal(8, new GuitarProMixTableScanner.TempoRampFinder(file500).Next(100));
    }

    // ---------- flags and bounds ----------

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void AllTracks_VolumePresent_UsesBit0(byte flags, bool expected)
    {
        var data = Gp4Mix(8, -1, -1, -1, -1, -1, -1, new[] { 16 }, flags);
        Assert.True(GuitarProMixTableScanner.TryParse(data, 0, gp5: false, hideTempoByte: false, out var mix, out _));
        Assert.Equal(expected, mix.AllTracks);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void AllTracks_BalanceOnly_UsesBit1(byte flags, bool expected)
    {
        var data = Gp4Mix(-1, 4, -1, -1, -1, -1, -1, new[] { 10 }, flags);
        Assert.True(GuitarProMixTableScanner.TryParse(data, 0, gp5: false, hideTempoByte: false, out var mix, out _));
        Assert.Equal(expected, mix.AllTracks);
    }

    [Fact]
    public void AllTracks_NeitherVolumeNorBalance_AlwaysFalse()
    {
        var data = Gp4Mix(-1, -1, 6, 6, -1, -1, -1, new[] { 9, 8 }, 3);
        Assert.True(GuitarProMixTableScanner.TryParse(data, 0, gp5: false, hideTempoByte: false, out var mix, out _));
        Assert.False(mix.AllTracks);
    }

    [Fact]
    public void Rejects_ValueBelowMinusOne()
    {
        var data = Gp4Mix(-2, -1, -1, -1, -1, -1, -1, new[] { 16 }, 0);
        Assert.False(GuitarProMixTableScanner.TryParse(data, 0, gp5: false, hideTempoByte: false, out _, out _));
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(1, true)]
    [InlineData(1000, true)]
    [InlineData(0, false)]
    [InlineData(1001, false)]
    public void TempoBounds_AreEnforced(int tempo, bool expected)
    {
        var durations = tempo < 0 ? new[] { 16 } : new[] { 16, 4 };
        // Volume-only buffer; the tempo duration is present only when the tempo is present.
        var data = Gp4Mix(8, -1, -1, -1, -1, -1, tempo, durations, 0);
        Assert.Equal(expected, GuitarProMixTableScanner.TryParse(data, 0, gp5: false, hideTempoByte: false, out _, out _));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void DurationBounds_AreEnforced(int duration, bool expected)
    {
        var data = Gp4Mix(8, -1, -1, -1, -1, -1, -1, new[] { duration }, 0);
        Assert.Equal(expected, GuitarProMixTableScanner.TryParse(data, 0, gp5: false, hideTempoByte: false, out _, out _));
    }

    [Fact]
    public void Rejects_NegativeDuration()
    {
        var data = Gp4Mix(8, -1, -1, -1, -1, -1, -1, new[] { -1 }, 0);
        Assert.False(GuitarProMixTableScanner.TryParse(data, 0, gp5: false, hideTempoByte: false, out _, out _));
    }

    [Fact]
    public void Rejects_TruncatedBuffers()
    {
        var full = Gp4Mix(8, -1, -1, -1, -1, -1, 120, new[] { 16, 4 }, 1);
        Assert.True(GuitarProMixTableScanner.TryParse(full, 0, gp5: false, hideTempoByte: false, out _, out _));
        // Cut inside the tempo int, inside the durations, and before the flags byte.
        Assert.False(GuitarProMixTableScanner.TryParse(full[..8], 0, gp5: false, hideTempoByte: false, out _, out _));
        Assert.False(GuitarProMixTableScanner.TryParse(full[..11], 0, gp5: false, hideTempoByte: false, out _, out _));
        Assert.False(GuitarProMixTableScanner.TryParse(full[..^1], 0, gp5: false, hideTempoByte: false, out _, out _));
    }

    [Fact]
    public void Rejects_BadGp5Size()
    {
        var good = Gp5Mix(8, -1, -1, -1, -1, -1, new byte[] { 0x00 }, -1, new[] { 16 }, null, 0);
        Assert.True(GuitarProMixTableScanner.TryParse(good, 0, gp5: true, hideTempoByte: false, out _, out _));

        // Size zero and size past the limit.
        Assert.False(GuitarProMixTableScanner.TryParse(Gp5Mix(8, -1, -1, -1, -1, -1, Array.Empty<byte>(), -1, new[] { 16 }, null, 0), 0, gp5: true, hideTempoByte: false, out _, out _));
        Assert.False(GuitarProMixTableScanner.TryParse(Gp5Mix(8, -1, -1, -1, -1, -1, new byte[65], -1, new[] { 16 }, null, 0), 0, gp5: true, hideTempoByte: false, out _, out _));
        // First content byte past size - 1.
        Assert.False(GuitarProMixTableScanner.TryParse(Gp5Mix(8, -1, -1, -1, -1, -1, new byte[] { 0x05 }, -1, new[] { 16 }, null, 0), 0, gp5: true, hideTempoByte: false, out _, out _));
        // Truncated name block.
        var truncated = good[..8];
        Assert.False(GuitarProMixTableScanner.TryParse(truncated, 0, gp5: true, hideTempoByte: false, out _, out _));
    }

    [Fact]
    public void TryParse_AtNonZeroOffset_Succeeds()
    {
        var mix = Gp4Mix(8, -1, -1, -1, -1, -1, -1, new[] { 16 }, 1);
        var data = new byte[32 + mix.Length];
        for (var i = 0; i < 32; i++)
            data[i] = 0x80;
        Array.Copy(mix, 0, data, 32, mix.Length);
        Assert.True(GuitarProMixTableScanner.TryParse(data, 32, gp5: false, hideTempoByte: false, out var parsed, out var end));
        Assert.Equal(8, parsed.Volume);
        Assert.True(parsed.AllTracks);
        Assert.Equal(32 + mix.Length, end);
    }
}
