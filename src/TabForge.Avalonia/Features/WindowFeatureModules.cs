using System;
using System.Collections.Generic;
using Avalonia.Controls;
using TabForge.Avalonia.Features.Help;
using TabForge.Avalonia.Features.LiveInput;
using TabForge.Avalonia.Features.Lyrics;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views.Docking;
using TabForge.Documents;

namespace TabForge.Avalonia;

internal sealed record WindowTransportFeatureContext(
    MainWindowCommands Commands,
    MainWindowDocumentPanes Panes,
    AvaloniaPlaybackSessions? Playback,
    MainWindowTabs Songs,
    Window Owner,
    MainWindowSettings? Settings,
    Action<string> ShowStatus);

internal sealed record WindowFeatureModule(
    Action<DockPaneSetup, Func<SongDocument>, Func<AvaloniaPlaybackSession?>, Action<Action>>? RegisterPanes = null,
    Action<IWindowCommandRegistration, Window, MainWindowSettings?>? RegisterCommands = null,
    Func<WindowTransportFeatureContext, IDisposable?>? RegisterTransport = null);

internal static class WindowFeatureModules
{
    private static readonly IReadOnlyList<WindowFeatureModule> BuiltIn =
        Array.AsReadOnly(new[] { HelpFeature.Module, LyricsPaneFlow.Module, MainWindowLiveInput.Module });

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

    internal static void RegisterTransport(WindowTransportFeatureContext context, Action<IDisposable> onClose)
    {
        foreach (var module in BuiltIn)
        {
            var disposable = module.RegisterTransport?.Invoke(context);
            if (disposable is not null) onClose(disposable);
        }
    }
}
