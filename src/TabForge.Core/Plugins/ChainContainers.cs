using System.Text.Json.Serialization;

namespace TabForge.Plugins;

// Owns: containers of an FX chain: the saved form (runs over the flat plug-in list), the rules that keep it valid, the chain as a
//   tree and the edits on it (group, dissolve, move, remove), bypass inherited from a container, and the packed form that travels
//   with a bare plug-in list (chain files, imports, default chains).
// Does not own: the flat plug-in list and what the engine receives (RigPreset.Plugins, PluginTrust.BuildSpecs), the list UI (Views/Fx).
// Tests: FxChainContainersTests.

/// <summary>
/// A container of an FX chain (property names are the file format). The flat plug-in list stays the chain: a container is the run of
/// <see cref="Count"/> plug-ins from index <see cref="Start"/> (0: an empty container in front of that plug-in). Containers are
/// listed outer before inner, in chain order, each with its <see cref="Depth"/>. A reader that does not know containers reads the
/// same flat chain.
/// </summary>
public sealed class ChainContainer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Container";
    /// <summary>Off: every plug-in inside is bypassed for the engine; their own switches stay as they are.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Mix in percent, kept with the chain and written back on export; not applied to the sound.</summary>
    public int Mix { get; set; } = 100;
    public bool Collapsed { get; set; }
    /// <summary>The name of the preset the container came with.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Preset { get; set; }
    /// <summary>The container's own internal routing as its source file wrote it (opaque; plug-ins run in series here).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Routing { get; set; }
    public int Start { get; set; }
    public int Count { get; set; }
    /// <summary>0: at the top level; else inside the nearest container listed before it that is one level up.</summary>
    public int Depth { get; set; }
    /// <summary>True when the source file's pin blocks route something (empty pin blocks are the plain path, whatever the channel count).</summary>
    [JsonIgnore] public bool OwnRouting => Routing is { } r && (r.Contains("<IN_PINS\n>") != r.Contains("<IN_PINS") || r.Contains("<OUT_PINS\n>") != r.Contains("<OUT_PINS"));
}

/// <summary>One item of a chain as a tree: a plug-in, or a container with its items.</summary>
public sealed class ChainItem
{
    public PluginSlot? Plugin { get; init; }
    public ChainContainer? Container { get; init; }
    public List<ChainItem> Children { get; } = new();
    /// <summary>The plug-in or the container: what a list row stands for.</summary>
    public object Tag => (object?)Plugin ?? Container!;
}

public static class ChainTree
{
    public const int MaxDepth = 8, MaxContainers = 64, MaxRoutingChars = 16 * 1024;

    /// <summary>The containers that hold for a list of <paramref name="slots"/> plug-ins, in their order; the rest are dropped.</summary>
    public static List<ChainContainer> Valid(IReadOnlyList<ChainContainer>? containers, int slots)
    {
        var kept = new List<ChainContainer>(); var open = new List<ChainContainer>();
        foreach (var c in containers ?? Array.Empty<ChainContainer>())
        {
            if (c is null || kept.Count >= MaxContainers || c.Start < 0 || c.Count < 0 || (long)c.Start + c.Count > slots || c.Depth < 0 || c.Depth >= MaxDepth || c.Depth > open.Count) continue;
            if (c.Name is null || c.Name.Length > 200 || c.Id is null || c.Id.Length > 64 || c.Preset is { Length: > 200 } || c.Routing is { Length: > MaxRoutingChars }) continue;
            if (open.Skip(c.Depth).Any(o => o.Start + o.Count > c.Start)) continue;   // crosses an earlier container of its level or deeper
            if (c.Depth > 0 && open[c.Depth - 1] is var parent && (c.Start < parent.Start || c.Start + c.Count > parent.Start + parent.Count)) continue;
            c.Mix = Math.Clamp(c.Mix, 0, 100);
            open.RemoveRange(c.Depth, open.Count - c.Depth);
            open.Add(c); kept.Add(c);
        }
        return kept;
    }

    public static List<ChainItem> Tree(RigPreset rig) => Tree(rig.Plugins, rig.Containers);

    /// <summary>The chain as a tree, in order: what the list shows and an exporter writes.</summary>
    public static List<ChainItem> Tree(IReadOnlyList<PluginSlot> plugins, IReadOnlyList<ChainContainer>? containers)
    {
        var root = new List<ChainItem>(); var open = new List<ChainItem>();
        var valid = Valid(containers, plugins.Count); var next = 0;
        List<ChainItem> Top() => open.Count > 0 ? open[^1].Children : root;
        for (var i = 0; i <= plugins.Count; i++)
        {
            for (; next < valid.Count && valid[next].Start == i; next++)
            {
                open.RemoveRange(valid[next].Depth, open.Count - valid[next].Depth);
                var item = new ChainItem { Container = valid[next] };
                Top().Add(item); open.Add(item);
            }
            while (open.Count > 0 && open[^1].Container!.Start + open[^1].Container!.Count <= i) open.RemoveAt(open.Count - 1);
            if (i < plugins.Count) Top().Add(new ChainItem { Plugin = plugins[i] });
        }
        return root;
    }

    /// <summary>Writes a tree back as the rig's flat plug-in list and its containers, and sets the bypass plug-ins inherit.</summary>
    public static void Write(RigPreset rig, List<ChainItem> tree)
    {
        var plugins = new List<PluginSlot>(); var containers = new List<ChainContainer>();
        void Walk(List<ChainItem> items, int depth, bool bypassed)
        {
            foreach (var item in items)
            {
                if (item.Plugin is { } p) { p.GroupBypassed = bypassed; p.ChainContainers = null; plugins.Add(p); continue; }
                var c = item.Container!;
                var keep = depth < MaxDepth && containers.Count < MaxContainers;   // deeper or further ones dissolve into what holds them
                if (keep) { c.Start = plugins.Count; c.Depth = depth; containers.Add(c); }
                Walk(item.Children, keep ? depth + 1 : depth, bypassed || (keep && !c.Enabled));
                if (keep) c.Count = plugins.Count - c.Start;
            }
        }
        Walk(tree, 0, false);
        if (!plugins.SequenceEqual(rig.Plugins)) { rig.Plugins.Clear(); rig.Plugins.AddRange(plugins); }
        rig.Containers = containers.Count > 0 ? containers : null;
    }

    /// <summary>
    /// After the flat list changed in any way: a plug-in that left the list leaves the tree (its container stays), a new plug-in
    /// lands at the top level behind what holds the plug-in in front of it, and a new run that carries a packed list (a loaded
    /// chain) brings its containers.
    /// </summary>
    public static void Reconcile(RigPreset rig, List<ChainItem> before)
    {
        var now = rig.Plugins.ToList();
        var present = new HashSet<PluginSlot>(now, ReferenceEqualityComparer.Instance);
        var known = new HashSet<PluginSlot>(ReferenceEqualityComparer.Instance);
        void Prune(List<ChainItem> items)
        {
            items.RemoveAll(i => i.Plugin is { } p && !present.Contains(p));
            foreach (var i in items) { if (i.Plugin is { } p) known.Add(p); else Prune(i.Children); }
        }
        Prune(before);
        for (var i = 0; i < now.Count; i++)
        {
            if (known.Contains(now[i])) continue;
            var run = now.Skip(i).TakeWhile(p => !known.Contains(p)).ToList();
            var at = i == 0 ? 0 : before.FindIndex(t => Holds(t, now[i - 1])) + 1;
            var packed = run[0].ChainContainers;
            foreach (var c in packed ?? new()) if (c is not null && rig.Containers?.Any(k => k.Id == c.Id) == true) c.Id = Guid.NewGuid().ToString("N");   // the same chain loaded again
            before.InsertRange(at, Tree(run, packed));
            foreach (var p in run) known.Add(p);
            i += run.Count - 1;
        }
        Write(rig, before);
    }

    private static bool Holds(ChainItem item, object tag) => ReferenceEquals(item.Tag, tag) || item.Children.Any(c => Holds(c, tag));

    /// <summary>Makes the rig's containers valid for its plug-in list as it is now and sets the inherited bypass. Cheap when there are none.</summary>
    public static void Refresh(RigPreset rig)
    {
        if (rig.Containers is null && !rig.Plugins.Any(p => p.ChainContainers is not null || p.GroupBypassed)) return;
        // A list that arrived packed (a default chain): its containers become the rig's.
        for (var i = 0; i < rig.Plugins.Count; i++)
        {
            if (rig.Plugins[i].ChainContainers is not { } packed) continue;
            rig.Plugins[i].ChainContainers = null;
            foreach (var c in Valid(packed, rig.Plugins.Count - i))
            {
                if (rig.Containers?.Any(k => k.Id == c.Id) == true) continue;   // already the rig's: never nested twice
                c.Start += i;
                (rig.Containers ??= new()).Add(c);
            }
        }
        if (rig.Containers is { } all) rig.Containers = all.OrderBy(c => c.Start).ToList();   // a stable sort: outer stays before inner
        Write(rig, Tree(rig));
    }

    /// <summary>Where an item sits: the list that holds it and its place there. Null when the tree does not have it.</summary>
    public static (List<ChainItem> List, int Index)? Find(List<ChainItem> tree, object tag)
    {
        for (var i = 0; i < tree.Count; i++)
        {
            if (ReferenceEquals(tree[i].Tag, tag)) return (tree, i);
            if (Find(tree[i].Children, tag) is { } deeper) return deeper;
        }
        return null;
    }

    /// <summary>Every plug-in under the items, in chain order.</summary>
    public static IEnumerable<PluginSlot> Plugins(IEnumerable<ChainItem> items) => items.SelectMany(i => i.Plugin is { } p ? new[] { p } : Plugins(i.Children));

    /// <summary>
    /// Moves the items of <paramref name="tags"/> together, in chain order: to the end of <paramref name="into"/>, else in front of
    /// <paramref name="before"/>, else to the end of the chain. An item inside a moved container moves with it. False (nothing
    /// changed) when a container would land inside itself.
    /// </summary>
    public static bool Move(List<ChainItem> tree, IReadOnlyCollection<object> tags, object? before, ChainContainer? into)
    {
        var moving = new List<ChainItem>();
        void Collect(List<ChainItem> items) { foreach (var i in items) { if (tags.Contains(i.Tag, ReferenceEqualityComparer.Instance)) moving.Add(i); else Collect(i.Children); } }
        Collect(tree);
        var target = (object?)into ?? before;
        if (moving.Count == 0 || (target is not null && moving.Any(m => Holds(m, target) && !(into is null && ReferenceEquals(m.Tag, target))))) return false;
        // The item in front of which they land may itself move: the next one that stays takes its place.
        if (into is null && before is not null && moving.Any(m => ReferenceEquals(m.Tag, before)))
        {
            var (list, index) = Find(tree, before)!.Value;
            before = list.Skip(index + 1).FirstOrDefault(i => !moving.Contains(i))?.Tag;
            if (before is null && !ReferenceEquals(list, tree)) { into = Owner(tree, list); }
        }
        void Detach(List<ChainItem> items) { items.RemoveAll(moving.Contains); foreach (var i in items) Detach(i.Children); }
        Detach(tree);
        if (into is not null && Find(tree, into) is { } box) box.List[box.Index].Children.AddRange(moving);
        else if (before is not null && Find(tree, before) is { } place) place.List.InsertRange(place.Index, moving);
        else tree.AddRange(moving);
        return true;
    }

    private static ChainContainer? Owner(List<ChainItem> tree, List<ChainItem> children) =>
        tree.Select(i => ReferenceEquals(i.Children, children) ? i.Container : Owner(i.Children, children)).FirstOrDefault(c => c is not null);

    /// <summary>Puts the items into a new container where the first of them sits. Null when there is nothing to put in.</summary>
    public static ChainContainer? GroupItems(List<ChainItem> tree, IReadOnlyCollection<object> tags, string name = "Container")
    {
        var made = new ChainItem { Container = new ChainContainer { Name = name } };
        var placed = false;
        void First(List<ChainItem> items) { for (var i = 0; i < items.Count && !placed; i++) { if (tags.Contains(items[i].Tag, ReferenceEqualityComparer.Instance)) { items.Insert(i, made); placed = true; } else First(items[i].Children); } }
        First(tree);
        if (!placed) return null;
        if (Move(tree, tags, null, made.Container)) return made.Container;
        Remove(tree, made.Container, keepItems: true);
        return null;
    }

    /// <summary>True when every item sits directly in the same container (or all at the top level): only such rows are grouped, so no
    /// plug-in is pulled out of a container it shares with others.</summary>
    public static bool SameParent(List<ChainItem> tree, IReadOnlyCollection<object> tags)
    {
        var lists = tags.Select(t => Find(tree, t)?.List).ToList();
        return lists.Count > 0 && lists.All(l => l is not null && ReferenceEquals(l, lists[0]));
    }

    /// <summary>
    /// The one way a chain is edited: captures the tree, runs <paramref name="change"/> (which may edit the tree, the flat list, or
    /// both: a plug-in taken out must leave the flat list too), then writes the containers for the result.
    /// </summary>
    public static void Edit(RigPreset rig, Action<List<ChainItem>> change)
    {
        var tree = Tree(rig);
        change(tree);
        Reconcile(rig, tree);
    }

    /// <summary>Puts a run of plug-ins into a new container. Null when the run is empty.</summary>
    public static ChainContainer? Group(RigPreset rig, int start, int count, string name = "Container")
    {
        if (start < 0 || count < 1 || start + count > rig.Plugins.Count) return null;
        var tree = Tree(rig);
        var made = GroupItems(tree, rig.Plugins.Skip(start).Take(count).ToList(), name);
        if (made is not null) Write(rig, tree);
        return made is not null && rig.Containers?.Contains(made) == true ? made : null;
    }

    /// <summary>Removes a container from the tree; with <paramref name="keepItems"/> its items move up to what held it.</summary>
    public static void Remove(List<ChainItem> tree, ChainContainer container, bool keepItems)
    {
        if (Find(tree, container) is not { } at) return;
        var items = at.List[at.Index].Children;
        at.List.RemoveAt(at.Index);
        if (keepItems) at.List.InsertRange(at.Index, items);
    }

    /// <summary>Removes a container; its plug-ins move up to what held it.</summary>
    public static void Dissolve(RigPreset rig, ChainContainer container) { var tree = Tree(rig); Remove(tree, container, keepItems: true); Write(rig, tree); }

    /// <summary>
    /// The packed form for a bare plug-in list (a chain file, a default chain): the containers ride on the first plug-in, with runs
    /// counted from it. A reader that does not know them reads the plain list. A list without plug-ins carries none.
    /// </summary>
    public static void Pack(IReadOnlyList<PluginSlot> plugins, IReadOnlyList<ChainContainer>? containers)
    {
        foreach (var p in plugins) p.ChainContainers = null;
        var valid = Valid(containers, plugins.Count);
        if (valid.Count > 0 && plugins.Count > 0) plugins[0].ChainContainers = Copy(valid);
    }

    private static List<ChainContainer> Copy(List<ChainContainer> containers) =>
        System.Text.Json.JsonSerializer.Deserialize<List<ChainContainer>>(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(containers))!;

    /// <summary>A copy of one container with everything inside as a bare, packed plug-in list (new objects). Empty without plug-ins.</summary>
    public static List<PluginSlot> PackCopy(RigPreset rig, ChainContainer container)
    {
        var copy = System.Text.Json.JsonSerializer.Deserialize<RigPreset>(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(rig))!;
        var index = rig.Containers?.IndexOf(container) ?? -1;
        var tree = Tree(copy);
        if (index < 0 || copy.Containers is not { } all || index >= all.Count || Find(tree, all[index]) is not { } at) return new List<PluginSlot>();
        var only = new RigPreset();
        Write(only, new List<ChainItem> { at.List[at.Index] });
        Pack(only.Plugins, only.Containers);
        return only.Plugins;
    }
}
