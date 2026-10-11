using System.IO;
using TabForge.Documents;
using TabForge.Services;

namespace TabForge.Controllers;

// Owns: the order of a drop of song files. The first file replaces the selected tab unless the drop opens in a new tab; the rest open in new tabs.
// A file the user refuses (the replace prompt answered keep or cancel) stops the rest and cancels the imports still queued.
// Does not own: opening a score (the window's OpenScore) or the status bar.
// Tests: TestTabUi (the setting's default); the drop order itself has no test yet.
public static class DroppedSongsFlow
{
    public static void Open(string[] songs, bool dropOpensInNewTab, Func<string, bool, Action<OpenedScore, bool>, ScoreImportJob?> openScore,
        ScoreImportQueue queue, Action<string> showStatus)
    {
        var batch = new List<ScoreImportJob>();
        var stopped = false;
        var replaceCurrent = !dropOpensInNewTab;
        foreach (var file in songs)
        {
            if (stopped) break;
            var job = openScore(file, replaceCurrent, (opened, loaded) =>
            {
                if (!loaded)
                {
                    stopped = true;
                    foreach (var other in batch) queue.Cancel(other);
                    return;
                }
                var text = opened.ImportedFromGuitarPro ? $"Imported {Path.GetFileName(file)}" : $"Opened {Path.GetFileName(file)}";
                if (opened.Notice is { } notice) text += $" — {notice}";
                showStatus(text);
            });
            if (job is not null) batch.Add(job);
            replaceCurrent = false;
        }
    }
}
