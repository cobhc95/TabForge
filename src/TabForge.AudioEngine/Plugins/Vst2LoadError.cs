namespace TabForge.AudioEngine.Plugins;

// Owns: the plain-language text for a Windows error when a VST2 library fails to load, always carrying the error code.
// Does not own: loading the library (Vst2Plugin).
public static class Vst2LoadError
{
    public const int BadImageFormat = 193, ModuleNotFound = 126, AccessDenied = 5;

    public static string Describe(int code) => code switch
    {
        BadImageFormat => $"This is a 32-bit plug-in; TabForge loads 64-bit plug-ins only (Windows error {code}).",
        ModuleNotFound => $"The plug-in could not be loaded: a file it needs is missing (Windows error {code}).",
        AccessDenied => $"The plug-in could not be loaded: access denied (Windows error {code}).",
        _ => $"The plug-in library could not be loaded (Windows error {code}).",
    };
}
