using System;
using System.Collections.Generic;
using System.Linq;

namespace TabForge.Plugins;

// Owns: putting a loaded FX preset on a chain, either replacing everything or keeping the current plug-ins and adding the preset after them,
//   with the loaded containers kept and every id (and a duplicated container name) made unique.
// Does not own: reading a preset file (PluginLibrary, ChainFileApply), the prompt (Views/Fx/FxPresetLoadPromptWindow) or the undo step (FxChainWindow).
// Tests: FxPresetLoadTests.

/// <summary>What a load of an FX preset does with the plug-ins already in the chain.</summary>
public enum FxPresetLoadMode { Replace, Add }

/// <summary>The chain after a merge: its plug-ins in order and its containers (null when none).</summary>
public readonly record struct FxChainMergeResult(List<PluginSlot> Plugins, List<ChainContainer>? Containers);

/// <summary>The one rule for replacing and adding a loaded FX preset; pure, so it can be tested without a window.</summary>
public static class FxChainMerge
{
    /// <summary>
    /// The chain <paramref name="existing"/> becomes after <paramref name="loaded"/> is loaded into it. Replace returns only the loaded
    /// plug-ins; Add keeps the existing plug-ins (same objects, settings, bypass and Active on rules untouched) and puts the loaded ones after
    /// them. Both keep the loaded containers, their settings and bypass states, and give a loaded plug-in or container a new id when its id is
    /// already used; a container name that is already used gets a number after it.
    /// </summary>
    public static FxChainMergeResult Merge(RigPreset existing, IReadOnlyList<PluginSlot> loaded, FxPresetLoadMode mode)
    {
        var incoming = loaded.ToList();
        var packed = incoming.Count > 0 ? incoming[0].ChainContainers : null;
        if (mode == FxPresetLoadMode.Replace)
        {
            var tree = ChainTree.Tree(incoming, packed);
            MakeUnique(tree, new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            var only = new RigPreset { Plugins = incoming };
            ChainTree.Write(only, tree);
            return new(only.Plugins, only.Containers);
        }

        var keptTree = ChainTree.Tree(existing);
        var pluginIds = new HashSet<string>(existing.Plugins.Select(p => p.Id), StringComparer.Ordinal);
        var containerIds = new HashSet<string>(existing.Containers?.Select(c => c.Id) ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        var containerNames = new HashSet<string>(existing.Containers?.Select(c => c.Name) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var addedTree = ChainTree.Tree(incoming, packed);
        MakeUnique(addedTree, pluginIds, containerIds, containerNames);
        keptTree.AddRange(addedTree);
        var merged = new RigPreset { Plugins = existing.Plugins.ToList(), Containers = existing.Containers?.ToList() };
        ChainTree.Write(merged, keptTree);
        return new(merged.Plugins, merged.Containers);
    }

    // New ids where an id is empty or already used; new container names only where a name is already used.
    private static void MakeUnique(List<ChainItem> tree, HashSet<string> pluginIds, HashSet<string> containerIds, HashSet<string> containerNames)
    {
        foreach (var item in tree)
        {
            if (item.Plugin is { } plugin)
            {
                if (string.IsNullOrEmpty(plugin.Id) || !pluginIds.Add(plugin.Id)) plugin.Id = Guid.NewGuid().ToString("N");
                pluginIds.Add(plugin.Id);
                continue;
            }
            var container = item.Container!;
            if (string.IsNullOrEmpty(container.Id) || !containerIds.Add(container.Id)) container.Id = Guid.NewGuid().ToString("N");
            containerIds.Add(container.Id);
            container.Name = UniqueName(container.Name, containerNames);
            MakeUnique(item.Children, pluginIds, containerIds, containerNames);
        }
    }

    private static string UniqueName(string name, HashSet<string> names)
    {
        var root = string.IsNullOrWhiteSpace(name) ? "Container" : name;
        var candidate = root;
        for (var number = 2; !names.Add(candidate); number++) candidate = $"{root} ({number})";
        return candidate;
    }
}
