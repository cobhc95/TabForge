using TabForge.Audio.Contracts;

namespace TabForge.AudioEngine.Plugins;

// Owns: the optional parameter list of a plug-in (names, ranges, values), for hosts that show generic sliders when there is no editor.
// Does not own: any one format's parameter model (ClapPlugin; VST2 keeps its own in Vst2Plugin.ParameterState).
// Tests: ClapHostTests, ClapProtocolTests.

/// <summary>A plug-in that lists and sets its parameters. Main thread only.</summary>
public interface IPluginParameters
{
    /// <summary>The visible parameters with their current values (hidden ones are left out).</summary>
    IReadOnlyList<PluginParameterInfo> GetParameters();

    /// <summary>Sets a parameter to a plain value (clamped to its range); false for an unknown or read-only id. Takes effect on the next processed block.</summary>
    bool SetParameter(uint id, double value);
}
