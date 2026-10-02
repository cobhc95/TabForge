using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AlphaTab;
using AlphaTab.Importer;
using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Services;

// Owns: the per-import work and size budget counters.
// Does not own: the import conversion and the worker process.
// Tests: TestGuitarProImportContainment.
/// <summary>Running totals of one import against TabForge's own limits (bars, beats, notes, bend points); also the way the converters reach the import's <see cref="ImportContext"/>.</summary>
internal sealed class ImportBudget
{
    private readonly ImportContext _context;
    private long _measures;
    private long _cells;
    private int _cellCalls;
    private long _notes;
    private long _curvePoints;

    public ImportBudget(ImportContext context) => _context = context;
    /// <summary>The import this budget counts for (its limits, conversion state and reported values).</summary>
    public ImportContext Context => _context;

    public void AddMeasures(int count)
    {
        _context.Check();   // per converted track: cancel, time and memory budget of a background import
        _measures += count;
        if (_measures > InputLimits.MaxTotalMeasures)
            throw new InvalidDataException("The score file contains too many measures overall.");
    }

    public void AddCells(int count)
    {
        if ((++_cellCalls & 63) == 0) _context.Check();   // every 64 bars converted
        _cells += count;
        if (_cells > InputLimits.MaxTotalCells)
            throw new InvalidDataException("The score file contains too many beats overall.");
    }

    public void AddNotes(int count, ref int measureNotes)
    {
        _notes += count;
        measureNotes += count;
        if (_notes > InputLimits.MaxTotalNotes || measureNotes > InputLimits.MaxNotesPerMeasure)
            throw new InvalidDataException("The score file contains too many notes.");
    }

    public void AddCurvePoints(int count)
    {
        _curvePoints += count;
        if (_curvePoints > InputLimits.MaxTotalCurvePoints)
            throw new InvalidDataException("The score file contains too many bend points.");
    }
}

// ---------- master bars: repeats, endings, sections, tempo map ----------
