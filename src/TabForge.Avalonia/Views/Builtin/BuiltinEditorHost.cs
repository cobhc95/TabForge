using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the built-in effect editor embedded in the FX chain window's editor area (where a plug-in's editor shows): it makes the editor of the focused slot, lets it fill the width
//   of the area (its groups wrap) and the height (its graph stretches), and scrolls only when the area is too short, and drops it again (its polling stops when it leaves the window).
// Does not own: which slot shows (FxChainWindow.Editor.cs), the editors (BuiltinEditors, BuiltinEffectPanel, IrLoaderEditor), the pop-out window (BuiltinEditorWindow).
// Tests: BuiltinEditorWindowTests, FxChainBuiltinEmbedTests.
public sealed class BuiltinEditorHost : ScrollViewer
{
    /// <summary>A subclass takes the ScrollViewer's template, so the host scrolls (its own type has no theme of its own).</summary>
    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    /// <summary>The slot whose editor shows, or null.</summary>
    public PluginSlot? Slot { get; private set; }

    /// <summary>The embedded editor, or null.</summary>
    public Control? Editor => Content as Control;

    public BuiltinEditorHost()
    {
        Name = "BuiltinEditorHost";
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        IsVisible = false;
        SizeChanged += (_, e) => { if (Editor is { } editor) editor.MinHeight = e.NewSize.Height; };
    }

    /// <summary>Shows the editor of <paramref name="slot"/> (kept when it already shows); false when the slot has none.</summary>
    public bool Show(IFxChainHost host, TrackModel track, PluginSlot slot)
    {
        if (ReferenceEquals(Slot, slot) && Content is not null) { IsVisible = true; return true; }
        Clear();
        if (BuiltinEditors.CreateEditor(host, track, slot) is not { } editor) { IsVisible = false; return false; }
        editor.MinWidth = 0;   // responsive: the editor takes the width of the area, its groups wrap; only a too-short area scrolls
        editor.MinHeight = Bounds.Height;
        editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        editor.VerticalAlignment = VerticalAlignment.Stretch;
        Slot = slot;
        Content = editor;
        IsVisible = true;
        return true;
    }

    /// <summary>Drops the editor and hides the host.</summary>
    public void Clear()
    {
        Content = null;
        Slot = null;
        IsVisible = false;
    }
}
