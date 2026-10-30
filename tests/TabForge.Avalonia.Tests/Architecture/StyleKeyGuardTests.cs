using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the StyleKey guard: every custom control that derives from a stock Avalonia templated
//   control must reuse the stock theme (StyleKeyOverride), or the real app renders it unthemed
//   (ArticulationPresetSelector was invisible for this reason; headless tests have no theme).
// Does not own: the fixes (each control's own file) or the Window/UserControl pattern (below).
// Tests: StyleKeyGuardTests (this file).
public sealed class StyleKeyGuardTests
{
    // Windows and UserControls are excluded by design: the repo's windows (MainWindow,
    // PreferencesWindow) carry no StyleKeyOverride and the app styles target Window via OfType;
    // UserControls define their own XAML content. Only stock-templated subclasses need the override.
    // Plain Control/Panel/Border/Canvas custom-drawn controls paint themselves and need none.
    private static readonly Dictionary<string, string> AllowedWithoutOverride = new(StringComparer.Ordinal)
    {
    };

    [Fact]
    public void TemplatedSubclassesReuseStockTheme()
    {
        var assembly = typeof(global::TabForge.Avalonia.Platform.AvaloniaLooks).Assembly;
        var controlType = typeof(global::Avalonia.Controls.Control);
        var templatedType = typeof(global::Avalonia.Controls.Primitives.TemplatedControl);
        var windowType = typeof(global::Avalonia.Controls.Window);
        var userControlType = typeof(global::Avalonia.Controls.UserControl);

        var failures = new List<string>();
        foreach (var type in assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract))
        {
            if (!controlType.IsAssignableFrom(type))
                continue;
            if (windowType.IsAssignableFrom(type))
                continue;
            if (userControlType.IsAssignableFrom(type))
                continue;
            if (!templatedType.IsAssignableFrom(type))
                continue;
            if (HasTabForgeStyleKeyOverride(type, assembly))
                continue;
            if (AllowedWithoutOverride.TryGetValue(type.FullName ?? type.Name, out _))
                continue;
            failures.Add(type.FullName ?? type.Name);
        }

        failures.Sort(StringComparer.Ordinal);
        Assert.True(failures.Count == 0,
            "Custom templated controls lack StyleKeyOverride and render unthemed (add 'protected override Type StyleKeyOverride => typeof(Base);' or allow-list with reason):\n"
            + string.Join("\n", failures));
    }

    private static bool HasTabForgeStyleKeyOverride(Type type, Assembly tabForgeAssembly)
    {
        for (var current = type; current is not null && current.Assembly == tabForgeAssembly; current = current.BaseType)
        {
            var prop = current.GetProperty("StyleKeyOverride",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (prop is not null)
                return true;
        }

        return false;
    }
}
