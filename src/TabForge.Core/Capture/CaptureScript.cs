using System.Text.Json;

namespace TabForge.Capture;

// Owns: reading a capture script (a JSON array of step objects) into steps, and the deterministic defaults every script starts from.
// Does not own: running the steps (the capture host), the image output (the host's renderer).
// Tests: TabForge.Avalonia.Tests.CaptureScriptTests.

/// <summary>A parsed capture script: its steps in order.</summary>
public sealed class CaptureScript
{
    /// <summary>The window size every run starts at, in DIPs.</summary>
    public const int WindowDipsAcross = 1600;
    public const int WindowDipsDown = 1000;

    /// <summary>The render scale of every PNG: 2 gives crisp 3200 x 2000 images from the 1600 x 1000 window.</summary>
    public const double RenderScale = 2.0;

    /// <summary>The theme name every run starts with.</summary>
    public const string DefaultTheme = "dark";

    public CaptureScript(IReadOnlyList<CaptureStep> steps) => Steps = steps;

    public IReadOnlyList<CaptureStep> Steps { get; }

    /// <summary>Parses the script text. Each array item must be an object whose first property is the verb; anything else throws a FormatException that names the item.</summary>
    public static CaptureScript Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return new CaptureScript(ParseSteps(document.RootElement));
    }

    /// <summary>The steps of a JSON array (the script itself, or a group nested in a step option).</summary>
    public static IReadOnlyList<CaptureStep> ParseSteps(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array) throw new FormatException("A capture script is a JSON array of steps.");
        var steps = new List<CaptureStep>();
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            index++;
            if (item.ValueKind != JsonValueKind.Object) throw new FormatException($"Step {index} is not an object.");
            string? verb = null;
            JsonElement value = default;
            var options = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in item.EnumerateObject())
            {
                if (verb is null) { verb = property.Name; value = property.Value.Clone(); }
                else options[property.Name] = property.Value.Clone();
            }
            steps.Add(new CaptureStep(verb ?? throw new FormatException($"Step {index} is empty."), value, options));
        }
        return steps;
    }
}
