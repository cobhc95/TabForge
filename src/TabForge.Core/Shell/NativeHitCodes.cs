// Owns: the numeric Win32 codes that the Windows shells use for non-client hit testing and caption
//       mouse messages (the values of winuser.h: WM_NCHITTEST, the WM_NC* mouse messages, including the middle button,
//       WM_MOUSEMOVE, WM_LBUTTONUP, WM_MBUTTONUP and the HT* codes).
// Does not own: the P/Invoke declarations (each app's Windows shell), the hit rules (ChromeHitRules) or the native replies (the hooks).
// Tests: TabForge.Avalonia.Tests.NativeHitCodesTests (pins each value to its Win32 header value).
namespace TabForge.Shell;

/// <summary>Win32 window-message and hit-test numbers, shared by the Windows shells. Plain values, no P/Invoke.</summary>
public static class NativeHitCodes
{
    public const int WmMouseMove = 0x0200, WmLButtonUp = 0x0202, WmMButtonDown = 0x0207, WmMButtonUp = 0x0209, WmNcHitTest = 0x0084,
        WmNcMouseMove = 0x00A0, WmNcLButtonDown = 0x00A1, WmNcLButtonUp = 0x00A2, WmNcMButtonDown = 0x00A7,
        WmNcMButtonUp = 0x00A8, WmNcMouseLeave = 0x02A2, WmMouseActivate = 0x0021, WmCaptureChanged = 0x0215;

    public const int HtClient = 1, HtCaption = 2, HtMinButton = 8, HtMaxButton = 9, HtLeft = 10, HtRight = 11, HtTop = 12,
        HtTopLeft = 13, HtTopRight = 14, HtBottom = 15, HtBottomLeft = 16, HtBottomRight = 17, HtClose = 20;
}
