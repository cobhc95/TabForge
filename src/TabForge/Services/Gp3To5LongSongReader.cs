// SPDX-License-Identifier: MPL-2.0
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// This file is the only alphaTab-derived source in TabForge: Run() follows the step order of Gp3To5Importer.readScore
// from alphaTab 1.8.4 (https://github.com/CoderLine/alphaTab, MPL-2.0, Copyright (c) 2025, Daniel Kuschny and
// Contributors), with one change:
// the fixed 1,000-bar safety threshold is replaced by the caller's limit. Everything else is alphaTab's own code,
// called unchanged in the unmodified AlphaTab.dll. The rest of TabForge stays under its MIT licence.

using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using AlphaTab;
using AlphaTab.Importer;
using AlphaTab.Io;
using AlphaTab.Model;

namespace TabForge.Services;

/// <summary>
/// Reads a Guitar Pro 3-5 file with more than 1,000 bars. alphaTab 1.8.4's reader refuses those with a hard-coded
/// <c>const</c> ("'bar count' ... internal safety threshold of 1000") that no setting or reflection can change, so this
/// replays its <c>ReadScore</c> step by step against the same importer instance and checks the bar count against
/// TabForge's own limit instead. Only the import path uses it, and only after alphaTab refused a file for its bar count.
/// </summary>
internal static class Gp3To5LongSongReader
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>The reflection surface of the alphaTab build this was written against, or null when it differs.</summary>
    private static readonly Lazy<Surface?> Members = new(Surface.TryCreate);

    /// <summary>True when the installed alphaTab is the build whose ReadScore this file replays.</summary>
    internal static bool IsAvailable => Members.Value is not null;

    /// <summary>True when <paramref name="error"/> is alphaTab's Guitar Pro 3-5 "bar count" safety threshold.</summary>
    internal static bool IsBarCountRefusal(Exception error) =>
        error.GetBaseException().Message.Contains("'bar count'", StringComparison.Ordinal);

    /// <summary>
    /// Reads <paramref name="data"/> (a Guitar Pro 3-5 file) like alphaTab's ScoreLoader, allowing up to
    /// <paramref name="maxBars"/> bars. Throws <see cref="InvalidDataException"/> above that, and alphaTab's own errors otherwise.
    /// </summary>
    internal static Score Read(byte[] data, Settings settings, int maxBars)
    {
        var members = Members.Value ?? throw new NotSupportedException("This alphaTab version is not the one the long-song reader replays.");
        var importer = members.Create(data, settings, out var readable);
        try { return members.Run(importer, readable, settings, maxBars); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    /// <summary>Diagnostics: runs the reader and returns the score as far as it got before failing (null if not even started).</summary>
    internal static Score? ReadPartial(byte[] data, int maxBars)
    {
        if (Members.Value is not { } members) return null;
        var settings = new Settings();
        var importer = members.Create(data, settings, out var readable);
        try { members.Run(importer, readable, settings, maxBars); }
        catch (Exception) { /* expected: the caller is locating this failure */ }
        return members.ScoreField.GetValue(importer) as Score;
    }

#pragma warning disable CS8618 // every member is set by TryCreate, which returns null when one is missing
    private sealed class Surface
    {
        public Type Importer;
        public ConstructorInfo ThrowingReadable;
        public FieldInfo DirectionLookup, ScoreField, VersionNumber, GlobalTripletFeel, InitialTempo, BarCount, TrackCount, Lyrics, LyricsTrack;
        public MethodInfo ReadVersion, ReadScoreInformation, ReadLyrics, ReadPageSetup, ReadPlaybackInfos, ReadDirection,
            EnsureLoopBoundary, ReadMasterBars, ReadTracks, ReadBars, GpReadBool, GpReadStringIntByte, Consolidate;
        public double MaxTrackCount;
#pragma warning restore CS8618

        public static Surface? TryCreate()
        {
            try
            {
                var asm = typeof(ScoreLoader).Assembly;
                var version = asm.GetName().Version;
                if (version is null || version.Major != 1 || version.Minor != 8 || version.Build != 4) return null;
                var t = asm.GetType("AlphaTab.Importer.Gp3To5Importer", true)!;
                var helpers = asm.GetType("AlphaTab.Importer.GpBinaryHelpers", true)!;
                var modelUtils = asm.GetType("AlphaTab.Model.ModelUtils", true)!;
                var throwing = asm.GetType("AlphaTab.Io.ThrowingReadable", true)!;
                FieldInfo F(string name) => t.GetField(name, Any) ?? throw new MissingFieldException(t.FullName, name);
                MethodInfo M(Type owner, string name, params Type[] args) =>
                    owner.GetMethod(name, Any, null, args, null) ?? throw new MissingMethodException(owner.FullName, name);
                var maxTracks = F("_maxTrackCount");
                return new Surface
                {
                    Importer = t,
                    ThrowingReadable = throwing.GetConstructor(Any, null, new[] { typeof(IReadable) }, null) ?? throw new MissingMethodException(throwing.FullName, ".ctor"),
                    DirectionLookup = F("_directionLookup"), ScoreField = F("_score"), VersionNumber = F("_versionNumber"),
                    GlobalTripletFeel = F("_globalTripletFeel"), InitialTempo = F("_initialTempo"), BarCount = F("_barCount"),
                    TrackCount = F("_trackCount"), Lyrics = F("_lyrics"), LyricsTrack = F("_lyricsTrack"),
                    ReadVersion = M(t, "ReadVersion"), ReadScoreInformation = M(t, "ReadScoreInformation"), ReadLyrics = M(t, "ReadLyrics"),
                    ReadPageSetup = M(t, "ReadPageSetup"), ReadPlaybackInfos = M(t, "ReadPlaybackInfos"),
                    ReadDirection = M(t, "_readDirection", typeof(Direction)),
                    EnsureLoopBoundary = M(t, "_ensureLoopBoundary", typeof(double), typeof(double), typeof(string)),
                    ReadMasterBars = M(t, "ReadMasterBars"), ReadTracks = M(t, "ReadTracks"), ReadBars = M(t, "ReadBars"),
                    GpReadBool = M(helpers, "GpReadBool", typeof(IReadable)),
                    GpReadStringIntByte = M(helpers, "GpReadStringIntByte", typeof(IReadable), typeof(string), typeof(double)),
                    Consolidate = M(modelUtils, "Consolidate", typeof(Score)),
                    MaxTrackCount = maxTracks.IsLiteral && maxTracks.GetRawConstantValue() is double max ? max : throw new MissingFieldException(t.FullName, "_maxTrackCount"),
                };
            }
            catch (Exception) { return null; }
        }

        /// <summary>A fresh importer initialised like ScoreLoader.LoadScoreFromBytes does (reads past the end throw).</summary>
        public object Create(byte[] data, Settings settings, out IReadable readable)
        {
            readable = (IReadable)ThrowingReadable.Invoke(new object[] { ByteBuffer.FromBuffer(data) });
            var importer = (ScoreImporter)Activator.CreateInstance(Importer, true)!;
            importer.Init(readable, settings);
            return importer;
        }

        // Gp3To5Importer.readScore of alphaTab 1.8.4, step for step; only the bar-count limit differs.
        public Score Run(object importer, IReadable data, Settings settings, int maxBars)
        {
            void Call(MethodInfo method, params object[] args) => method.Invoke(importer, args);
            (DirectionLookup.GetValue(importer) as AlphaTab.Collections.IMap)?.Clear();
            Call(ReadVersion);
            var score = new Score();
            ScoreField.SetValue(importer, score);
            Call(ReadScoreInformation);
            var version = (double)VersionNumber.GetValue(importer)!;
            if (version < 500)
                GlobalTripletFeel.SetValue(importer, (bool)GpReadBool.Invoke(null, new object[] { data })! ? TripletFeel.Triplet8th : TripletFeel.NoTripletFeel);
            if (version >= 400) Call(ReadLyrics);
            if (version >= 510) data.Skip(19);
            var initialTempo = Automation.BuildTempoAutomation(false, 0, 0, 0, true);
            InitialTempo.SetValue(importer, initialTempo);
            if (version >= 500)
            {
                Call(ReadPageSetup);
                initialTempo.Text = (string)GpReadStringIntByte.Invoke(null, new object[] { data, settings.Importer.Encoding, settings.Importer.MaxDecodingBufferSize })!;
            }
            initialTempo.Value = IOHelper.ReadInt32LE(data);
            if (version >= 510) GpReadBool.Invoke(null, new object[] { data });
            IOHelper.ReadInt32LE(data);
            if (version >= 400) data.ReadByte();
            Call(ReadPlaybackInfos);
            if (version >= 500)
            {
                foreach (var direction in new[]
                {
                    Direction.TargetCoda, Direction.TargetDoubleCoda, Direction.TargetSegno, Direction.TargetSegnoSegno, Direction.TargetFine,
                    Direction.JumpDaCapo, Direction.JumpDaCapoAlCoda, Direction.JumpDaCapoAlDoubleCoda, Direction.JumpDaCapoAlFine,
                    Direction.JumpDalSegno, Direction.JumpDalSegnoAlCoda, Direction.JumpDalSegnoAlDoubleCoda, Direction.JumpDalSegnoAlFine,
                    Direction.JumpDalSegnoSegno, Direction.JumpDalSegnoSegnoAlCoda, Direction.JumpDalSegnoSegnoAlDoubleCoda,
                    Direction.JumpDalSegnoSegnoAlFine, Direction.JumpDaCoda, Direction.JumpDaDoubleCoda,
                })
                    Call(ReadDirection, direction);
                data.Skip(4);
            }
            double barCount = IOHelper.ReadInt32LE(data);
            BarCount.SetValue(importer, barCount);
            // TabForge's limit (InputLimits.MaxMeasuresPerTrack) instead of alphaTab's fixed 1,000.
            if (barCount > maxBars) throw new InvalidDataException("The Guitar Pro file contains too many measures.");
            double trackCount = IOHelper.ReadInt32LE(data);
            TrackCount.SetValue(importer, trackCount);
            Call(EnsureLoopBoundary, trackCount, MaxTrackCount, "track count");
            Call(ReadMasterBars);
            Call(ReadTracks);
            Call(ReadBars);
            if (score.MasterBars.Count > 0)
            {
                var automation = Automation.BuildTempoAutomation(false, 0, score.Tempo, 2, true);
                automation.Text = score.TempoLabel;
                score.MasterBars[0].TempoAutomations.Add(automation);
            }
            Consolidate.Invoke(null, new object[] { score });
            score.Finish(settings);
            var lyricsTrack = (double)LyricsTrack.GetValue(importer)!;
            if (Lyrics.GetValue(importer) is IList<Lyrics> lyrics && lyricsTrack >= 0)
                score.Tracks[(int)lyricsTrack].ApplyLyrics(lyrics);
            return score;
        }
    }
}
