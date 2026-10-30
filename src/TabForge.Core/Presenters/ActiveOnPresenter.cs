using System.Collections.Generic;
using System.Text;
using TabForge.Plugins;

namespace TabForge.Presenters;

// Owns: the words of the "Active on" menu entry, the popup and the row hint (the rule's description too).
// Does not own: the rule and its matcher (ActiveOnRules), the popup or the rows (Views/Fx/ActiveOn, FxChainRows).
// Tests: FxActiveOnTests.
public static class ActiveOnPresenter
{
    public const string MenuText = "Active on...";
    public const string Title = "Active on";
    public const string DriverSection = "Driver type";
    public const string DeviceSection = "Output device";
    public const string DriverAny = "Any (ASIO or WASAPI)";
    public const string DriverAsio = "ASIO only";
    public const string DriverWasapi = "WASAPI only";
    public const string DeviceAny = "Any output device";
    public const string InactiveHint = "Inactive on this device";
    public const string NotConnected = "not connected";
    public const string DoneText = "Done";
    public const string CancelText = "Cancel";
    public const string ClearText = "Clear rule";
    public const string Subtitle = "{0} plays only when both match. Leave a choice on Any to ignore it.";
    public const string StatusActive = "Right now: active (matches)";
    public const string StatusInactive = "Right now: inactive on this device";
    public const string ButtonName = "Active on";
    public const string ButtonTipNone = "Active on: any driver, any device. Click to set a rule.";
    public const string ButtonTipRule = "Active on: {0}. Click to change.";
    public const string ButtonTipDisabled = "Select a plug-in or container";
    public const string MenuTip = "Play this effect (or everything in this container) only on one driver type and output device";
    public const string BadgeTip = "Active on: {0}";

    /// <summary>A driver kind's label in the popup.</summary>
    public static string DriverLabel(ActiveOnDriverKind? kind) => kind switch
    {
        ActiveOnDriverKind.Asio => DriverAsio,
        ActiveOnDriverKind.Wasapi => DriverWasapi,
        _ => DriverAny,
    };

    /// <summary>"Every output", "ASIO only", "Headphones" or "ASIO only · Headphones".</summary>
    public static string Describe(ActiveOnRule? rule)
    {
        if (rule is null || rule.IsAny) return "every output";
        var text = new StringBuilder();
        if (rule.DriverKind is { } kind) text.Append(DriverLabel(kind));
        if (!string.IsNullOrWhiteSpace(rule.DeviceName))
        {
            if (text.Length > 0) text.Append(" · ");
            text.Append(rule.DeviceName);
        }
        return text.ToString();
    }

    /// <summary>The badge's tooltip, with the inactive note when the rule does not allow the current output.</summary>
    public static string BadgeToolTip(ActiveOnRule? rule, bool inactive) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, BadgeTip, Describe(rule)) + (inactive ? "\n" + InactiveHint : "");

    /// <summary>The popup subtitle for the named plug-in or container.</summary>
    public static string SubtitleFor(string? subject) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Subtitle, string.IsNullOrWhiteSpace(subject) ? "This effect" : subject);

    /// <summary>The right-now line of the popup.</summary>
    public static string Status(bool active) => active ? StatusActive : StatusInactive;

    /// <summary>The icon button's tooltip: the plain invitation without a rule, else "ASIO only, Focusrite USB".</summary>
    public static string ButtonToolTip(ActiveOnRule? rule, bool hasTarget = true)
    {
        if (!hasTarget) return ButtonTipDisabled;
        if (rule is null || rule.IsAny) return ButtonTipNone;
        var parts = new List<string>();
        if (rule.DriverKind is { } kind) parts.Add(DriverLabel(kind));
        if (!string.IsNullOrWhiteSpace(rule.DeviceName)) parts.Add(rule.DeviceName.Trim());
        return string.Format(System.Globalization.CultureInfo.CurrentCulture, ButtonTipRule, string.Join(", ", parts));
    }
}
