using System;
using System.Collections.Generic;
using Avalonia;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The theme brush colours one control reads when it renders, looked up through the tree once and kept until the theme variant or a resource
/// changes (a Publish replaces the resources), so a repaint does no resource-tree walk.
/// Owns: the per-control name-to-colour table and its reset. Does not own: the palette (AvaloniaPalette.BrushColour).
/// Tests: TabForge.Avalonia.Tests.ThemeColourCacheTests.
/// Threads: UI only.
/// </summary>
internal sealed class ThemeColourCache
{
    private readonly StyledElement _owner;
    private readonly Dictionary<string, UiColor> _colours = new(StringComparer.Ordinal);   // ceiling: the handful of brush names the control paints
    private int _version = AvaloniaPalette.PublishVersion;   // the publish the colours were read at: a published colour changes in place, with no resource notification

    public ThemeColourCache(StyledElement owner)
    {
        _owner = owner;
        owner.ActualThemeVariantChanged += (_, _) => _colours.Clear();
        owner.ResourcesChanged += (_, _) => _colours.Clear();
        owner.AttachedToLogicalTree += (_, _) => _colours.Clear();
    }

    /// <summary>The colour of theme brush <paramref name="name"/> for the owner's variant.</summary>
    public UiColor Get(string name)
    {
        if (_version != AvaloniaPalette.PublishVersion)
        {
            _colours.Clear();
            _version = AvaloniaPalette.PublishVersion;
        }
        if (_colours.TryGetValue(name, out var colour)) return colour;
        colour = _owner.BrushColour(name);
        _colours[name] = colour;
        return colour;
    }
}
