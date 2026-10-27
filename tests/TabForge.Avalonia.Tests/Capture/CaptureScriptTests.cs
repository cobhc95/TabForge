using System;
using TabForge.Capture;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the capture script parser and command line checks: verb and option reading, nested step groups, the errors and the fixed defaults.
/// Does not own: running steps (CaptureSessionTests).
/// Tests: this class.
/// </summary>
public class CaptureScriptTests
{
    [Fact]
    public void FirstPropertyIsTheVerbAndTheRestAreOptions()
    {
        var script = CaptureScript.Parse("""[ {"shot":"a-b","target":"window:Mixer","region":[1,2,3,4],"scale":1.5}, {"bar":12}, {"playhead":[13,0.4]} ]""");
        Assert.Equal(3, script.Steps.Count);
        var shot = script.Steps[0];
        Assert.Equal("shot", shot.Verb);
        Assert.Equal("a-b", shot.Text);
        Assert.Equal("window:Mixer", shot.Option("target"));
        Assert.Equal(new double[] { 1, 2, 3, 4 }, shot.NumbersOption("region"));
        Assert.Equal(1.5, shot.Option("scale", 2.0));
        Assert.Equal(2.0, script.Steps[1].Option("scale", 2.0));
        Assert.Equal(12, script.Steps[1].Int);
        Assert.Equal(new[] { 13, 0.4 }, script.Steps[2].Numbers);
    }

    [Fact]
    public void ReadsCommentsTrailingCommasAndNestedGroups()
    {
        var script = CaptureScript.Parse("""
            [ // a comment
              {"frames":"x","sequence":[[{"bar":1},{"bar":2}],[{"bar":3}]]},
            ]
            """);
        var groups = script.Steps[0].Groups("sequence")!;
        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups[0].Count);
        Assert.Equal(3, groups[1][0].Int);
        Assert.Null(script.Steps[0].Groups("none"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[1]")]
    [InlineData("[{}]")]
    public void RejectsWhatIsNotAnArrayOfVerbObjects(string json) => Assert.Throws<FormatException>(() => CaptureScript.Parse(json));

    [Fact]
    public void WrongValueTypesNameTheStep()
    {
        var step = CaptureScript.Parse("""[{"bar":"x"}]""").Steps[0];
        var error = Assert.Throws<FormatException>(() => step.Int);
        Assert.Contains("bar", error.Message);
    }

    [Fact]
    public void CommandLineNeedsScriptAndOutputFolder()
    {
        Assert.Null(CaptureArguments.TryParse(new[] { "song.gp" }));
        var parsed = CaptureArguments.TryParse(new[] { "--capture", "s.json", "--out", "o" })!;
        Assert.EndsWith("s.json", parsed.ScriptPath);
        Assert.EndsWith("o", parsed.OutputFolder);
        Assert.Throws<FormatException>(() => CaptureArguments.TryParse(new[] { "--capture", "s.json" }));
        Assert.Throws<FormatException>(() => CaptureArguments.TryParse(new[] { "--capture" }));
    }

    [Fact]
    public void DeterministicDefaultsAreFixed()
    {
        Assert.Equal(1600, CaptureScript.WindowDipsAcross);
        Assert.Equal(1000, CaptureScript.WindowDipsDown);
        Assert.Equal(2.0, CaptureScript.RenderScale);
        Assert.Equal("dark", CaptureScript.DefaultTheme);
    }
}
