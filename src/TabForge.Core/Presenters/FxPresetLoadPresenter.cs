using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Plugins;

namespace TabForge.Presenters;

// Owns: the words of the "Load an FX preset" question (title, text, the two choice cards, the remember box, the two-instrument note and the
//   buttons), the stored choice (Ask / Always replace / Always add) and the answer it gives.
// Does not own: the merge of the chains (FxChainMerge), showing the window (Views/Fx/FxPresetLoadPromptWindow) or the Settings row (SettingsCatalog).
// Tests: FxPresetLoadTests.

/// <summary>The answer of the FX preset load question: replace or add, and whether to remember the choice.</summary>
public readonly record struct FxPresetLoadAnswer(FxPresetLoadMode Mode, bool Remember);

/// <summary>The stored setting for the "Ask / Always replace / Always add" row on Settings &gt; Audio &amp; Plug-ins.</summary>
public static class FxPresetLoadChoice
{
    public const string Ask = "Ask", Replace = "Always replace", Add = "Always add";
    public static string[] Labels => new[] { Ask, Replace, Add };

    /// <summary>The stored text as one of the three labels; anything else is Ask.</summary>
    public static string Normalize(string? stored) =>
        string.Equals(stored, Replace, StringComparison.OrdinalIgnoreCase) ? Replace
        : string.Equals(stored, Add, StringComparison.OrdinalIgnoreCase) ? Add
        : Ask;

    public static string FromLabel(string? label) => Normalize(label);

    public static string ToLabel(string? stored) => Normalize(stored);
}

public static class FxPresetLoadPresenter
{
    public const string ReplaceTitle = "Replace the existing chain";
    public const string AddTitle = "Add to the existing chain";
    public const string RememberText = "Remember my choice (changeable in Settings)";
    public const string CancelText = "Cancel", LoadText = "Load";
    public const string TwoInstrumentsLine = "Two instruments will play together.";

    /// <summary>The window title and heading: Load "Warm".</summary>
    public static string Title(string name) => $"Load \"{name}\"";

    /// <summary>The question above the two choices.</summary>
    public static string Question(int count) => $"This chain already has {count} plug-ins. What should the loaded preset do?";

    public static string ReplaceHint(int count) => $"Removes the {count} current plug-ins, then loads the preset.";
    public static string AddHint(int count) => $"Keeps the {count} plug-ins and puts the preset's plug-ins after them.";

    /// <summary>The choice stored in Settings; null when the question is asked.</summary>
    public static FxPresetLoadMode? Remembered(string? stored) => FxPresetLoadChoice.Normalize(stored) switch
    {
        FxPresetLoadChoice.Replace => FxPresetLoadMode.Replace,
        FxPresetLoadChoice.Add => FxPresetLoadMode.Add,
        _ => null,
    };

    /// <summary>The value Remember stores for an answer.</summary>
    public static string Store(FxPresetLoadMode mode) => mode == FxPresetLoadMode.Add ? FxPresetLoadChoice.Add : FxPresetLoadChoice.Replace;

    /// <summary>An empty chain loads straight away (both answers are the same list): otherwise the stored choice, or null to ask.</summary>
    public static FxPresetLoadMode? WithoutAsking(int count, string? stored) =>
        count == 0 ? FxPresetLoadMode.Replace : Remembered(stored);

    /// <summary>The preset's name from a file path, without its extension.</summary>
    public static string NameOf(string path) => Path.GetFileNameWithoutExtension(path);

    /// <summary>The note under the options: both the current chain and the preset hold an instrument.</summary>
    public static bool ShowsTwoInstruments(RigPreset existing, IEnumerable<PluginSlot> loaded) =>
        FxChainPresenter.HasInstrument(existing.Plugins) && FxChainPresenter.HasInstrument(loaded);
}
