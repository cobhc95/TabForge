using System;
using System.Collections.Generic;
using System.Text;
using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the raw-byte walk of the legacy repair on crafted file headers (no score reader involved).
// Does not own: the decoding (GpLegacyTextTests) or the full import (GpLegacyEncodingRepairTests).
public class GpLegacyWalkTests
{
    [Fact]
    public void MinimalGp5HeaderRepairsTitleAndTrackName()
    {
        var title = new byte[] { 0x42, 0x6A, 0xF6, 0x72, 0x6E };       // Björn in Latin-1
        var track = new byte[] { 0x4E, 0x65, 0x64, 0x65, 0x72, 0x67, 0xE5, 0x72, 0x64 }; // Nedergård in Latin-1
        var file = MinimalGp5(title, track);

        var project = new SongProject { Title = "Bj\uFFFDrn", Tracks = new List<TrackModel> { new() { Name = "Nederg\uFFFDrnd" } } };
        Assert.True(GpLegacyTextRepair.Apply(project, file));
        Assert.Equal("Björn", project.Title);
        Assert.Equal("Nedergård", project.Tracks[0].Name);
    }

    [Fact]
    public void TruncatedHeaderRepairsNothing()
    {
        var project = new SongProject { Title = "Bj\uFFFDrn", Tracks = new List<TrackModel> { new() { Name = "X" } } };
        Assert.False(GpLegacyTextRepair.Apply(project, new byte[] { 0x18 }));
        Assert.Equal("Bj\uFFFDrn", project.Title);
    }

    /// <summary>A minimal version-5.10 file: the version line, nine info strings, no notices, empty lyrics, the RSE block, page setup, tempo, key, channels, one bar, one track.</summary>
    private static byte[] MinimalGp5(byte[] titleLatin1, byte[] trackLatin1)
    {
        var o = new List<byte>();
        void Byte(int v) => o.Add((byte)v);
        void Int(int v) { o.AddRange(BitConverter.GetBytes(v)); }
        void IntString(byte[] latin1) { Int(latin1.Length + 1); Byte(latin1.Length); o.AddRange(latin1); }
        void Ascii(string s) => o.AddRange(Encoding.ASCII.GetBytes(s));
        void Zeros(int n) { for (var i = 0; i < n; i++) o.Add(0); }

        Byte(24); Ascii("FICHIER GUITAR PRO v5.10"); Zeros(6);
        IntString(titleLatin1);
        for (var i = 0; i < 8; i++) IntString(Array.Empty<byte>());
        Int(0);
        Int(1);
        for (var i = 0; i < 5; i++) { Int(0); Int(0); }
        Int(100); Int(0); Zeros(11);
        foreach (var v in new[] { 210, 297, 10, 10, 15, 10, 100 }) Int(v);
        o.AddRange(new byte[] { 0xFF, 0x01 });
        for (var i = 0; i < 10; i++) IntString(Array.Empty<byte>());
        IntString(Array.Empty<byte>());
        Int(120); Byte(0);
        Byte(0); Int(0);
        for (var i = 0; i < 64; i++) { Int(25); Byte(13); Byte(8); Byte(0); Byte(0); Byte(0); Byte(0); Zeros(2); }
        for (var i = 0; i < 19; i++) { o.Add(0xFF); o.Add(0xFF); }
        Int(0);
        Int(1); Int(1);
        Byte(0x01 | 0x02); Byte(4); Byte(4);
        Zeros(4); Byte(0); Byte(0); Byte(0);
        Byte(0x08);
        Byte((byte)trackLatin1.Length); o.AddRange(trackLatin1); Zeros(40 - trackLatin1.Length);
        Int(6);
        foreach (var t in new[] { 64, 59, 55, 50, 45, 40, 0 }) Int(t);
        Int(1); Int(1); Int(1); Int(24); Int(0);
        o.AddRange(new byte[] { 0xFF, 0x00, 0x00, 0x00 });
        o.Add(0); o.Add(0); Byte(0); Byte(0); Byte(0);
        Zeros(12); Zeros(12); Zeros(16);
        Zeros(4); IntString(Array.Empty<byte>()); IntString(Array.Empty<byte>());
        return o.ToArray();
    }
}
