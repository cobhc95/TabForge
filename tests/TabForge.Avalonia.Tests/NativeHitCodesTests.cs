using TabForge.Shell;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the pinned values of the shared Win32 message and hit-test codes (Core/Shell/NativeHitCodes): each equals its winuser.h value.
// Does not own: the hook's replies (NcHitTestHookTests) or the hit rules (TestChromeHitRules in the WPF self-tests).
public class NativeHitCodesTests
{
    [Theory]
    [InlineData("WM_MOUSEMOVE", 0x0200, NativeHitCodes.WmMouseMove)]
    [InlineData("WM_LBUTTONUP", 0x0202, NativeHitCodes.WmLButtonUp)]
    [InlineData("WM_NCHITTEST", 0x0084, NativeHitCodes.WmNcHitTest)]
    [InlineData("WM_NCMOUSEMOVE", 0x00A0, NativeHitCodes.WmNcMouseMove)]
    [InlineData("WM_NCLBUTTONDOWN", 0x00A1, NativeHitCodes.WmNcLButtonDown)]
    [InlineData("WM_NCLBUTTONUP", 0x00A2, NativeHitCodes.WmNcLButtonUp)]
    [InlineData("WM_NCMOUSELEAVE", 0x02A2, NativeHitCodes.WmNcMouseLeave)]
    [InlineData("HTCLIENT", 1, NativeHitCodes.HtClient)]
    [InlineData("HTCAPTION", 2, NativeHitCodes.HtCaption)]
    [InlineData("HTMINBUTTON", 8, NativeHitCodes.HtMinButton)]
    [InlineData("HTMAXBUTTON", 9, NativeHitCodes.HtMaxButton)]
    [InlineData("HTLEFT", 10, NativeHitCodes.HtLeft)]
    [InlineData("HTRIGHT", 11, NativeHitCodes.HtRight)]
    [InlineData("HTTOP", 12, NativeHitCodes.HtTop)]
    [InlineData("HTTOPLEFT", 13, NativeHitCodes.HtTopLeft)]
    [InlineData("HTTOPRIGHT", 14, NativeHitCodes.HtTopRight)]
    [InlineData("HTBOTTOM", 15, NativeHitCodes.HtBottom)]
    [InlineData("HTBOTTOMLEFT", 16, NativeHitCodes.HtBottomLeft)]
    [InlineData("HTBOTTOMRIGHT", 17, NativeHitCodes.HtBottomRight)]
    [InlineData("HTCLOSE", 20, NativeHitCodes.HtClose)]
    public void Each_code_equals_its_win32_header_value(string name, int headerValue, int code) =>
        Assert.True(headerValue == code, $"{name} is {code:X4}, the Win32 header value is {headerValue:X4}.");
}
