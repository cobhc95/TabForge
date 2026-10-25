using System.Collections.Generic;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the middle-click table: each MiddleClick setting maps to the action a tab performs.
public sealed class TabBehaviourSettingsTests
{
    public static IEnumerable<object[]> MiddleClickRows =>
    [
        [MiddleClickActions.Close, TabMiddleClickAction.Close],
        [MiddleClickActions.NewTab, TabMiddleClickAction.NewTab],
        [MiddleClickActions.Duplicate, TabMiddleClickAction.Duplicate],
        [MiddleClickActions.Nothing, TabMiddleClickAction.Nothing],
    ];

    [Theory]
    [MemberData(nameof(MiddleClickRows))]
    public void MiddleClickSettingMapsToTabAction(string setting, TabMiddleClickAction expected) =>
        Assert.Equal(expected, TabBehaviourSettings.MiddleClickAction(new TabSettings { MiddleClick = setting }));
}
