using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TabForge.Avalonia.Tests;

// Owns: the corpus counting probe's output: attacks.csv (one row per bar with a delta or a timing cause), causes.csv (the rollup per cause) and the printed summary.
// Does not own: counting (CorpusProbeWritten / CorpusProbeActual).
// Tests: CorpusProbeTests.
internal sealed class CorpusProbeReport : IDisposable
{
    private sealed record Row(string File, ProbeBarKey Key, ProbeBarTally Tally);

    private sealed class CauseTotal
    {
        public int Count;
        public HashSet<string> Files { get; } = new();
        public string Example = "";
    }

    private readonly StreamWriter _attacks;
    private readonly string _folder;
    private readonly List<Row> _deltas = new();
    private readonly SortedDictionary<string, CauseTotal> _causes = new(StringComparer.Ordinal);
    private readonly List<string> _failures = new();

    public int Files { get; private set; }
    public long Expected { get; private set; }
    public long Actual { get; private set; }

    public CorpusProbeReport(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(folder);
        _attacks = new StreamWriter(Path.Combine(folder, "attacks.csv"), false, new UTF8Encoding(false));
        _attacks.WriteLine("file,track,srcBar,occ,voice,exp,act,delta,causes,onsetErrMs,durErrMs");
    }

    public void AddFailure(string file, string reason) => _failures.Add($"{file}: {reason}");

    /// <summary>File-level causes the bars cannot show (duplicates the importer merged, a playback order cut at its visit limit).</summary>
    public void AddFileCause(string file, string cause, int amount)
    {
        if (amount > 0) Total(cause, file, "(whole file)", amount);
    }

    public void AddFile(string file, IReadOnlyDictionary<ProbeBarKey, ProbeBarTally> tallies)
    {
        Files++;
        foreach (var (key, tally) in tallies.OrderBy(p => p.Key.Track).ThenBy(p => p.Key.SrcBar).ThenBy(p => p.Key.Occurrence).ThenBy(p => p.Key.Voice))
        {
            Expected += tally.Expected;
            Actual += tally.Actual;
            foreach (var (cause, amount) in tally.Causes) Total(cause, file, Where(key), amount);
            if (tally.Delta == 0 && !tally.Causes.Keys.Any(c => c.EndsWith("_UNEXPLAINED"))) continue;   // rows are kept for deltas and for timing nobody explains
            _deltas.Add(new Row(file, key, tally));
            _attacks.WriteLine(string.Join(',', Quote(file), key.Track + 1, key.SrcBar + 1, key.Occurrence, key.Voice + 1, tally.Expected, tally.Actual, tally.Delta,
                Quote(tally.CauseList), tally.OnsetErrMs.ToString("0.0", CultureInfo.InvariantCulture), tally.DurationErrMs.ToString("0.0", CultureInfo.InvariantCulture)));
        }
    }

    private void Total(string cause, string file, string where, int amount)
    {
        if (!_causes.TryGetValue(cause, out var total)) _causes[cause] = total = new CauseTotal { Example = $"{file} {where}" };
        total.Count += amount;
        total.Files.Add(file);
    }

    private static string Where(ProbeBarKey key) => $"track {key.Track + 1} bar {key.SrcBar + 1} pass {key.Occurrence} voice {key.Voice + 1}";

    private static string Quote(string text) => '"' + text.Replace("\"", "\"\"") + '"';

    /// <summary>Writes causes.csv and returns the printable summary.</summary>
    public string Finish()
    {
        _attacks.Flush();
        File.WriteAllLines(Path.Combine(_folder, "causes.csv"),
            new[] { "cause,count,files,example" }.Concat(_causes.OrderByDescending(p => p.Value.Count)
                .Select(p => string.Join(',', p.Key, p.Value.Count, p.Value.Files.Count, Quote(p.Value.Example)))));
        var unexplained = _deltas.Where(r => r.Tally.Causes.ContainsKey(ProbeCause.CountUnexplained)).ToList();
        var text = new StringBuilder();
        text.AppendLine($"files {Files} (failed {_failures.Count}), expected attacks {Expected}, actual {Actual}, net {Actual - Expected}, sum|delta| {_deltas.Sum(r => Math.Abs(r.Tally.Delta))}");
        text.AppendLine($"bars with a count delta {_deltas.Count(r => r.Tally.Delta != 0)}, with no cause flag {unexplained.Count} ({unexplained.Sum(r => Math.Abs(r.Tally.Delta))} attacks)");
        text.AppendLine("causes (count, files, example):");
        foreach (var (cause, total) in _causes.OrderByDescending(p => p.Value.Count))
            text.AppendLine($"  {cause,-20} {total.Count,8} {total.Files.Count,5}  {total.Example}");
        text.AppendLine("worst bars:");
        foreach (var row in _deltas.OrderByDescending(r => Math.Abs(r.Tally.Delta)).ThenByDescending(r => Math.Abs(r.Tally.OnsetErrMs)).Take(20))
            text.AppendLine($"  {row.Tally.Delta,4} (exp {row.Tally.Expected} act {row.Tally.Actual}) {row.File} {Where(row.Key)} [{row.Tally.CauseList}]");
        foreach (var failure in _failures.Take(20)) text.AppendLine("  FAILED " + failure);
        return text.ToString();
    }

    public void Dispose() => _attacks.Dispose();
}
