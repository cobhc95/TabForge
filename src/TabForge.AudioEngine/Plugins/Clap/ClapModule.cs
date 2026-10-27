using System.Runtime.InteropServices;
using System.Text;

namespace TabForge.AudioEngine.Plugins.Clap;

// Owns: one loaded .clap module: its entry (init / deinit), plug-in factory and the descriptors it lists.
// Does not own: instances (ClapPlugin), trust (PluginIdentity.Hold runs before a module is opened).
// Tests: ClapHostTests (descriptor parsing against a fake entry).
// Threads: main thread only (CLAP entry init / deinit and factory calls are main-thread).

/// <summary>What a CLAP descriptor says about one plug-in of the file.</summary>
public sealed record ClapDescriptorInfo(int Index, string Id, string Name, string Vendor, string Version, string Description, IReadOnlyList<string> Features)
{
    public bool IsInstrument => Features.Contains(ClapIds.FeatureInstrument, StringComparer.Ordinal);
    public bool IsEffect => Features.Contains(ClapIds.FeatureAudioEffect, StringComparer.Ordinal);
}

/// <summary>An opened .clap file. <see cref="Dispose"/> calls the entry's deinit and releases the library.</summary>
internal sealed unsafe class ClapModule : IDisposable
{
    private readonly ClapPluginEntry* _entry;
    private readonly Action? _release;
    private bool _disposed;

    public string Path { get; }
    public IReadOnlyList<ClapDescriptorInfo> Plugins { get; }
    internal ClapPluginFactory* Factory { get; }

    /// <summary>Loads <paramref name="path"/> through <paramref name="loader"/> (the caller has already checked trust) and lists its plug-ins.</summary>
    public static ClapModule Open(string path, IPluginModuleLoader loader)
    {
        var library = loader.Load(path);
        try
        {
            if (!NativeLibrary.TryGetExport(library, ClapIds.Entry, out var export))
                throw new InvalidOperationException("This file is not a CLAP plug-in (no clap_entry).");
            return new ClapModule(path, (ClapPluginEntry*)export, () => loader.Free(library));
        }
        catch
        {
            loader.Free(library);
            throw;
        }
    }

    /// <summary>Opens an entry that is already in memory (tests with a fake plug-in); <paramref name="release"/> runs after deinit.</summary>
    internal ClapModule(string path, ClapPluginEntry* entry, Action? release)
    {
        Path = path;
        _entry = entry;
        _release = release;
        if (entry->Version.Major < 1) throw new InvalidOperationException($"CLAP version {entry->Version.Major}.{entry->Version.Minor} is not supported.");
        var pathBytes = Encoding.UTF8.GetBytes(path + "\0");
        byte ok;
        fixed (byte* p = pathBytes) ok = entry->Init(p);
        if (ok == 0) throw new InvalidOperationException("The CLAP plug-in refused to start (clap_entry.init returned false).");
        try
        {
            var id = Encoding.ASCII.GetBytes(ClapIds.FactoryId + "\0");
            fixed (byte* p = id) Factory = (ClapPluginFactory*)entry->GetFactory(p);
            if (Factory == null) throw new InvalidOperationException("This CLAP file has no plug-in factory.");
            Plugins = ReadDescriptors(Factory);
        }
        catch
        {
            entry->Deinit();
            throw;
        }
    }

    private static List<ClapDescriptorInfo> ReadDescriptors(ClapPluginFactory* factory)
    {
        var count = (int)Math.Min(factory->GetPluginCount(factory), 256u);
        var list = new List<ClapDescriptorInfo>(count);
        for (var i = 0; i < count; i++)
            if (Parse(i, factory->GetPluginDescriptor(factory, (uint)i)) is { } info) list.Add(info);
        return list;
    }

    /// <summary>Reads a descriptor's text and feature list; null for a missing descriptor or one without an id.</summary>
    internal static ClapDescriptorInfo? Parse(int index, ClapPluginDescriptor* d)
    {
        if (d == null || d->Id == null) return null;
        var features = new List<string>();
        if (d->Features != null)
            for (var f = 0; f < 64 && d->Features[f] != null; f++) features.Add(Text(d->Features[f]));
        var id = Text(d->Id);
        var name = Text(d->Name);
        return new ClapDescriptorInfo(index, id, name.Length > 0 ? name : id, Text(d->Vendor), Text(d->VersionText), Text(d->Description), features);
    }

    /// <summary>Picks the plug-in matching the wanted role (first instrument or first effect), else the first one; null when the file lists none.</summary>
    public ClapDescriptorInfo? Pick(bool wantInstrument) =>
        Plugins.FirstOrDefault(p => p.IsInstrument == wantInstrument && (p.IsInstrument || p.IsEffect)) ?? Plugins.FirstOrDefault();

    internal static string Text(byte* p)
    {
        if (p == null) return "";
        var n = 0;
        while (n < 4096 && p[n] != 0) n++;
        return Encoding.UTF8.GetString(p, n).Trim();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _entry->Deinit(); }
        finally { _release?.Invoke(); }
    }
}
