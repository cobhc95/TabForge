using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// Add plug-in: every plug-in in your folders with its vendor, format and role; type to filter, double-click (or
/// Enter) to add. The folders are scanned when the window opens (progress below), or once and remembered when
/// Settings > Audio &amp; VST > Remember the plug-in list is on (then Rescan updates it).
/// </summary>
internal static class PluginBrowser
{
    public static VstPluginInfo? Choose(Window owner, IFxChainHost host) => new PluginBrowserDialog(owner, host).Run();
}
