using System.Windows;
using System.Windows.Automation;

namespace TabForge.Views;

/// <summary>Stable UI Automation ids (and names, where a control has none of its own) for scripted UI tests and screen readers. Ids only: no visible text or behaviour.</summary>
internal static class UiIds
{
    /// <summary>Sets the AutomationId, and the Name when <paramref name="name"/> is given; returns the element.</summary>
    public static T Id<T>(T element, string id, string? name = null) where T : DependencyObject
    {
        AutomationProperties.SetAutomationId(element, id);
        if (name is not null) AutomationProperties.SetName(element, name);
        return element;
    }
}
