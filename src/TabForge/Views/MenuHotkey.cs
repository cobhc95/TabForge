using System.Windows;
using System.Windows.Controls;

namespace TabForge.Views;

/// <summary>
/// Ties a menu item to the catalogue command it runs, so the gesture text beside it is read from the live bindings
/// (<c>local:MenuHotkey.Id="Edit.Undo"</c>). Nothing types a key into a menu: a rebind, a preset change or an unbound command
/// shows at once, and an unbound command shows nothing.
/// </summary>
public static class MenuHotkey
{
    public static readonly DependencyProperty IdProperty = DependencyProperty.RegisterAttached(
        "Id", typeof(string), typeof(MenuHotkey), new PropertyMetadata(null));

    public static string? GetId(DependencyObject d) => (string?)d.GetValue(IdProperty);
    public static void SetId(DependencyObject d, string? value) => d.SetValue(IdProperty, value);

    /// <summary>
    /// Sets <see cref="MenuItem.InputGestureText"/> on every item under <paramref name="root"/> (sub-menus included) that has a
    /// command id, from <paramref name="textFor"/>. Returns how many items it set.
    /// </summary>
    public static int Apply(ItemsControl root, Func<string, string> textFor)
    {
        var count = 0;
        foreach (var item in root.Items)
        {
            if (item is not MenuItem menuItem) continue;
            if (GetId(menuItem) is { Length: > 0 } id) { menuItem.InputGestureText = textFor(id); count++; }
            count += Apply(menuItem, textFor);
        }
        return count;
    }
}
