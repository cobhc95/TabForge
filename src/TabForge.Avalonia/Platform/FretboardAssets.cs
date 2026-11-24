using System;
using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: sharing decoded fretboard textures while panels use them, and applying FretboardWoodFinish to wood.
// Does not own: which texture a look uses (Core FretboardStyles) or the drawing (FretboardArtist).
// A missing or unreadable file gives null; the artist then falls back to the themed flat colour. Thread: UI thread.
internal sealed class FretboardAssets
{
    private static readonly ConcurrentDictionary<string, WeakReference<IUiImage>> _images = new();
    private string? _woodName, _bindingName, _inlayName, _capoName;
    private IUiImage? _wood, _binding, _inlay, _capo, _capoShadow;

    internal IUiImage? Get(string? file)
    {
        if (file is null) return null;
        if (_images.TryGetValue(file, out var cached) && cached.TryGetTarget(out var image)) return image;
        image = Load(file);
        if (image is not null) _images[file] = new WeakReference<IUiImage>(image);
        return image;
    }

    internal IUiImage? Wood(string name)
    {
        if (name != _woodName) (_woodName, _wood) = (name, Get(FretboardStyles.WoodFile(name)));
        return _wood;
    }

    internal IUiImage? Binding(string name)
    {
        if (name != _bindingName) (_bindingName, _binding) = (name, Get(FretboardStyles.BindingFile(name)));
        return _binding;
    }

    internal IUiImage? Inlay(string name)
    {
        if (name != _inlayName) (_inlayName, _inlay) = (name, Get(FretboardStyles.InlayFile(name)));
        return _inlay;
    }

    /// <summary>The full capo of a finish, or its cast shadow.</summary>
    internal IUiImage? Capo(string look, bool shadow)
    {
        if (look != _capoName) (_capoName, _capo, _capoShadow) = (look, Get(FretboardStyles.CapoFile(look, false, false)), Get(FretboardStyles.CapoFile(look, false, true)));
        return shadow ? _capoShadow : _capo;
    }

    internal void Clear()
    {
        _woodName = _bindingName = _inlayName = _capoName = null;
        _wood = _binding = _inlay = _capo = _capoShadow = null;
    }

    internal static (double Width, double Height)? SizeOf(IUiImage? image) =>
        image is AvaloniaImage { Source.Size: { Width: > 0, Height: > 0 } size } ? (size.Width, size.Height) : null;

    private static IUiImage? Load(string name)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri($"avares://TabForge/Assets/Fretboards/{name}"));
            var bitmap = new Bitmap(stream);
            if (!name.StartsWith("fb_wood_", StringComparison.Ordinal)) return new AvaloniaImage(bitmap);
            using (bitmap) return new AvaloniaImage(FretboardWoodFinish.Apply(bitmap));
        }
        catch (Exception) { return null; }
    }
}
