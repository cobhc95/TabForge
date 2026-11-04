using System;
using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: decoding the shipped fretboard textures (woods, bindings, inlay materials, capos, wound strings) once each, by file name, for the panel that owns it,
//   applying FretboardWoodFinish only to wood, and remembering the texture each part of the current look resolved to,
//   so a steady frame builds no file names and looks nothing up.
// Does not own: which texture a look uses (Core FretboardStyles) or the drawing (FretboardArtist).
// A missing or unreadable file gives null; the artist then falls back to the themed flat colour. Thread: UI thread.
internal sealed class FretboardAssets
{
    private readonly ConcurrentDictionary<string, Lazy<IUiImage?>> _images = new();
    private string? _woodName, _bindingName, _inlayName, _capoName, _stringName;
    private IUiImage? _wood, _binding, _inlay, _capo, _capoShadow, _string;

    internal IUiImage? Get(string? file) =>
        file is null ? null : _images.GetOrAdd(file, name => new Lazy<IUiImage?>(() => Load(name))).Value;

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

    internal IUiImage? WoundString(string look)
    {
        if (look != _stringName) (_stringName, _string) = (look, Get(FretboardStyles.WoundStringFile(look)));
        return _string;
    }

    /// <summary>The full capo of a finish, or its cast shadow.</summary>
    internal IUiImage? Capo(string look, bool shadow)
    {
        if (look != _capoName) (_capoName, _capo, _capoShadow) = (look, Get(FretboardStyles.CapoFile(look, false, false)), Get(FretboardStyles.CapoFile(look, false, true)));
        return shadow ? _capoShadow : _capo;
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
