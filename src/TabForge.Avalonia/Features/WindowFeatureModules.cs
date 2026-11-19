using System;
using System.Collections.Generic;
using Avalonia.Controls;
using TabForge.Avalonia.Features.Help;
using TabForge.Avalonia.Features.Lyrics;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views.Docking;
using TabForge.Documents;

namespace TabForge.Avalonia;

internal sealed record WindowFeatureModule(
    Action<DockPaneSetup, Func<SongDocument>, Func<AvaloniaPlaybackSession?>, Action<Action>>? RegisterPanes = null,
    Action<IWindowCommandRegistration, Window, MainWindowSettings?>? RegisterCommands = null);

internal static class WindowFeatureModules
{
    private static readonly IReadOnlyList<WindowFeatureModule> BuiltIn =
        Array.AsReadOnly(new[] { HelpFeature.Module, LyricsPaneFlow.Module });

    internal static void RegisterPanes(DockPaneSetup setup, MainWindowTabs songs,
        AvaloniaPlaybackSessions? playback, SubscriptionBag subscriptions)
    {
        void ObserveActive(Action callback)
        {
            songs.ActiveChanged += callback;
            subscriptions.Add(() => songs.ActiveChanged -= callback);
        }

        foreach (var module in BuiltIn)
            module.RegisterPanes?.Invoke(setup, () => songs.Active, () => playback?.For(songs.Active), ObserveActive);
    }

    internal static void RegisterCommands(IWindowCommandRegistration commands, Window owner, MainWindowSettings? settings)
    {
        foreach (var module in BuiltIn)
            module.RegisterCommands?.Invoke(commands, owner, settings);
    }
}
