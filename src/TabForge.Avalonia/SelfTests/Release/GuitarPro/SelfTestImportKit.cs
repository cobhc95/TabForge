using System;
using System.Linq;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

// Owns: the small helpers the import checks share.
// Does not own: the checks themselves (the other files of this folder).
// Tests: TestGuitarProImportWorker, TestImportPlausibility.
public static partial class SelfTest
{
    /// <summary>Same song content; track ids are fresh per import, so they are taken from <paramref name="expected"/> first.</summary>
    private static bool SameImportedSong(SongProject actual, SongProject expected)
    {
        if (actual.Tracks.Count != expected.Tracks.Count) return false;
        for (var i = 0; i < actual.Tracks.Count; i++) actual.Tracks[i].Id = expected.Tracks[i].Id;
        return ProjectService.ContentHash(actual).AsSpan().SequenceEqual(ProjectService.ContentHash(expected));
    }

    private static string ImportExtension(int version) => version >= 500 ? ".gp5" : version >= 400 ? ".gp4" : ".gp3";
}
