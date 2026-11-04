#include "editor_window.h"

#include <algorithm>

#include "vst2_host.h"

namespace tfvst32 {
namespace {
constexpr UINT_PTR kTimerId = 1;
constexpr UINT kTimerMs = 33;   // ~30 Hz: effEditIdle and the plug-in's own redraw
constexpr DWORD kPopup = 0x80000000u;
constexpr DWORD kCaption = 0x00C00000u, kSysMenu = 0x00080000u, kMinimizeBox = 0x00020000u, kMaximizeBox = 0x00010000u;
constexpr DWORD kThickFrame = 0x00040000u, kClipChildren = 0x02000000u;
constexpr DWORD kFloatingStyle = kCaption | kSysMenu | kMinimizeBox | kMaximizeBox | kThickFrame | kClipChildren;
constexpr DWORD kExToolWindow = 0x00000080u, kExDlgModalFrame = 0x00000001u;
constexpr DWORD kSwpNoZOrder = 0x0004, kSwpNoActivate = 0x0010, kSwpNoMove = 0x0002;
constexpr int kErrorClassAlreadyExists = 1410;
constexpr int kMaxEditorDimension = 4096;

const wchar_t* const kWindowTitle = L"TabForge Plug-in Editor";
constexpr int kEffEditGetRect = 13, kEffEditOpen = 14, kEffEditClose = 15, kEffEditIdle = 19;

bool IsWine() {
    const HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
    return ntdll != nullptr && GetProcAddress(ntdll, "wine_get_version") != nullptr;
}

std::wstring WindowTitle(const std::string& product) {
    if (product.empty()) return kWindowTitle;
    const int size = MultiByteToWideChar(CP_UTF8, 0, product.c_str(), -1, nullptr, 0);
    if (size <= 1) return kWindowTitle;
    std::wstring wide(static_cast<size_t>(size), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, product.c_str(), -1, wide.data(), size);
    wide.resize(static_cast<size_t>(size - 1));
    return L"TabForge - " + wide;
}
struct ERect { int16_t top, left, bottom, right; };
static_assert(sizeof(ERect) == 8, "VST2 ERect layout");

void EnablePerMonitorDpi() {
    const HMODULE user32 = GetModuleHandleW(L"user32.dll");
    if (user32 == nullptr) return;
    using SetContext = BOOL(WINAPI*)(DPI_AWARENESS_CONTEXT);
    const auto setContext = reinterpret_cast<SetContext>(GetProcAddress(user32, "SetProcessDpiAwarenessContext"));
    if (setContext != nullptr && setContext(reinterpret_cast<DPI_AWARENESS_CONTEXT>(-4))) return;  // -4: PER_MONITOR_AWARE_V2
    SetProcessDPIAware();
}

UINT DpiOf(HWND window) {
    const HMODULE user32 = GetModuleHandleW(L"user32.dll");
    if (user32 != nullptr) {
        using GetDpi = UINT(WINAPI*)(HWND);
        const auto getDpi = reinterpret_cast<GetDpi>(GetProcAddress(user32, "GetDpiForWindow"));
        if (getDpi != nullptr && window != nullptr) {
            const UINT dpi = getDpi(window);
            if (dpi >= 48) return dpi;
        }
    }
    return 96;
}
}  // namespace

void EditorWindow::EnableDpiAwareness() { EnablePerMonitorDpi(); }

void EditorWindow::SetCallbacks(std::function<void()> userClosed, std::function<void(int, int)> sized) {
    onUserClosed_ = std::move(userClosed);
    onSized_ = std::move(sized);
}

void EditorWindow::AnnounceSize() const {
    if (onSized_ && width_ > 0 && height_ > 0) onSized_(width_, height_);
}

void EditorWindow::RegisterClass() {
    static bool registered = false;
    if (registered) return;
    WNDCLASSEXW cls{};
    cls.cbSize = sizeof(cls);
    cls.style = CS_DBLCLKS;
    cls.lpfnWndProc = &EditorWindow::WndProc;
    cls.hInstance = GetModuleHandleW(nullptr);
    cls.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    cls.hbrBackground = GetSysColorBrush(COLOR_BTNFACE);
    cls.lpszClassName = ClassName();
    if (RegisterClassExW(&cls) == 0 && static_cast<int>(GetLastError()) != kErrorClassAlreadyExists) return;
    registered = true;
}

bool EditorWindow::Open(EditorTarget& host, HWND owner, bool docked, const std::string& title, std::string& error) {
    if (hwnd_ != nullptr) {
        if (docked_ == docked) { ShowWindow(hwnd_, SW_SHOW); return true; }
        Close(false);
    }
    if (!host.HasEditor()) { error = "the plug-in has no editor"; return false; }
    if (owner == nullptr || !IsWindow(owner)) {
        // Under Wine the engine is a Linux process and cannot pass a Windows HWND. The helper then owns a
        // normal top-level window; docking is impossible without an owner, so it is a floating window.
        if (!IsWine()) {
            error = "the editor owner window is no longer available (it may have closed)";
            return false;
        }
        owner = nullptr;
        docked = false;
    }
    RegisterClass();

    // Docked: the owner TabForge passes is the FX area; the popup's owner must be that area's root window, exactly
    // what PluginEditorWin32.FindEditorPopup looks for. Floating: the owner TabForge passes is already the root.
    area_ = docked ? owner : nullptr;
    owner_ = docked ? GetAncestor(owner, GA_ROOT) : owner;
    if (owner_ == nullptr) owner_ = owner;
    docked_ = docked;

    int startWidth = 400, startHeight = 300, x = CW_USEDEFAULT, y = CW_USEDEFAULT;
    if (docked_ && area_ != nullptr) {
        RECT area{};
        if (GetWindowRect(area_, &area)) {
            startWidth = std::max(1, static_cast<int>(area.right - area.left));
            startHeight = std::max(1, static_cast<int>(area.bottom - area.top));
            x = area.left;
            y = area.top;
        }
    } else {
        const UINT dpi = DpiOf(owner_);
        startWidth = MulDiv(startWidth, static_cast<int>(dpi), 96);
        startHeight = MulDiv(startHeight, static_cast<int>(dpi), 96);
    }

    const DWORD style = docked_ ? kPopup | kClipChildren : kFloatingStyle;
    const DWORD exStyle = docked_ ? kExToolWindow : kExDlgModalFrame;
    const std::wstring caption = WindowTitle(title);
    hwnd_ = CreateWindowExW(exStyle, ClassName(), caption.c_str(), static_cast<DWORD>(style), x, y, startWidth, startHeight,
                            owner_, nullptr, GetModuleHandleW(nullptr), this);
    if (hwnd_ == nullptr) {
        error = "the editor window could not be created (error " + std::to_string(GetLastError()) + ")";
        owner_ = area_ = nullptr;
        return false;
    }
    SetWindowLongPtrW(hwnd_, GWLP_HWNDPARENT, reinterpret_cast<LONG_PTR>(owner_));
    // Show the parent before the plug-in attaches its view: Wine creates the VST3 child window while the parent is
    // already mapped, so the view is painted on its first idle tick instead of staying an unmapped child.
    ShowWindow(hwnd_, SW_SHOW);
    if (!host.OpenEditor(hwnd_)) {
        error = "the plug-in did not open its editor";
        DestroyWindow(hwnd_);
        hwnd_ = nullptr;
        owner_ = area_ = nullptr;
        return false;
    }
    host_ = &host;

    int width = 400, height = 300;
    host.EditorSize(width, height);
    ApplySize(width, height, docked_);
    SetTimer(hwnd_, kTimerId, kTimerMs, nullptr);
    if (!docked_) SetForegroundWindow(hwnd_);
    return true;
}

void EditorWindow::Close(bool userClosed) {
    const bool wasOpen = hwnd_ != nullptr;
    HWND hwnd = hwnd_;
    hwnd_ = nullptr;   // first: DestroyWindow below sends WM_DESTROY back into this instance
    if (hwnd != nullptr) {
        KillTimer(hwnd, kTimerId);
        if (host_ != nullptr && host_->EditorOpen()) host_->CloseEditor();
        DestroyWindow(hwnd);
    }
    host_ = nullptr;
    owner_ = nullptr;
    area_ = nullptr;
    docked_ = false;
    if (userClosed && wasOpen && onUserClosed_) onUserClosed_();
}

void EditorWindow::ApplySize(int width, int height, bool positionOverOwner) {
    if (hwnd_ == nullptr) return;
    width_ = std::clamp(width, 1, kMaxEditorDimension);
    height_ = std::clamp(height, 1, kMaxEditorDimension);
    RECT rect{0, 0, width_, height_};
    UINT flags = kSwpNoZOrder | kSwpNoActivate;
    if (!docked_) {
        AdjustWindowRectEx(&rect, kFloatingStyle, FALSE, kExDlgModalFrame);
        flags |= kSwpNoMove;
    } else if (positionOverOwner && area_ != nullptr) {
        RECT area{};
        if (GetWindowRect(area_, &area)) { rect.left = area.left; rect.top = area.top; }
    }
    SetWindowPos(hwnd_, nullptr, rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top, flags);
}

void EditorWindow::OnTimer() {
    if (hwnd_ == nullptr || host_ == nullptr) return;
    if (owner_ != nullptr && !IsWindow(owner_)) { Close(false); return; }
    // A plug-in's own popup menu runs a nested message loop inside our DispatchMessage. Its timer ticks keep arriving,
    // but calling effEditIdle re-enters the plug-in inside that loop (some editors fault). Keep the pipe serviced
    // instead, so the engine sees an answer and never mistakes this for a dead helper.
    constexpr ULONGLONG kLongDispatchMs = 100;
    if (dispatchDepth_ > 0 && GetTickCount64() - dispatchStarted_ >= kLongDispatchMs) {
        if (onNestedService_) onNestedService_();
        return;
    }
    host_->EditorIdle();
    int width = 0, height = 0;
    if (host_->TakeEditorResize(width, height)) {
        ApplySize(width, height, docked_);
        if (onSized_) onSized_(width_, height_);
    }
}

LRESULT CALLBACK EditorWindow::WndProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam) {
    EditorWindow* self = reinterpret_cast<EditorWindow*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    if (message == WM_NCCREATE) {
        const auto* create = reinterpret_cast<const CREATESTRUCTW*>(lParam);
        self = static_cast<EditorWindow*>(create->lpCreateParams);
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
    }
    switch (message) {
        case WM_CLOSE:
            if (self != nullptr) self->Close(true); else DestroyWindow(hwnd);
            return 0;
        case WM_TIMER:
            if (self != nullptr && wParam == kTimerId) { self->OnTimer(); return 0; }
            break;
        case WM_SIZE:
            if (self != nullptr && !self->docked_ && self->host_ != nullptr) {
                const int width = LOWORD(lParam), height = HIWORD(lParam);
                if (width > 0 && height > 0 && (width != self->width_ || height != self->height_)) {
                    self->width_ = width;
                    self->height_ = height;
                    self->host_->SetEditorSize(width, height);
                    if (self->onSized_) self->onSized_(width, height);
                }
            }
            break;
        case WM_DPICHANGED:
            if (self != nullptr && !self->docked_) {
                const auto* suggested = reinterpret_cast<const RECT*>(lParam);
                SetWindowPos(hwnd, nullptr, suggested->left, suggested->top, suggested->right - suggested->left,
                             suggested->bottom - suggested->top, kSwpNoZOrder | kSwpNoActivate);
            }
            break;
        case WM_DESTROY:
            if (self != nullptr && self->hwnd_ == hwnd) self->hwnd_ = nullptr;
            break;
        default:
            break;
    }
    return DefWindowProcW(hwnd, message, wParam, lParam);
}

bool Vst2Host::ReadEditorRect(int& width, int& height) {
    width = 400;
    height = 300;
    __try {
        ERect* rect = nullptr;
        Dispatch(kEffEditGetRect, 0, 0, &rect, 0);
        if (rect != nullptr && rect->right > rect->left && rect->bottom > rect->top) {
            width = std::clamp(static_cast<int>(rect->right - rect->left), 1, kMaxEditorDimension);
            height = std::clamp(static_cast<int>(rect->bottom - rect->top), 1, kMaxEditorDimension);
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        width = 400;   // a garbage ERect pointer must not take the helper down
        height = 300;
    }
    return true;
}

bool Vst2Host::EditorDispatchGuarded(int opcode, int index, intptr_t value, void* ptr, float opt, intptr_t* result) {
    __try {
        const intptr_t answer = Dispatch(opcode, index, value, ptr, opt);
        if (result != nullptr) *result = answer;
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

bool Vst2Host::EditorSize(int& width, int& height) {
    if (effect_ == nullptr || !hasEditor_) {
        width = 400;
        height = 300;
        return false;
    }
    std::lock_guard<std::mutex> lock(pluginMutex_);
    return ReadEditorRect(width, height);
}

bool Vst2Host::OpenEditor(HWND parent) {
    if (effect_ == nullptr || !hasEditor_ || editorOpen_) return false;
    std::lock_guard<std::mutex> lock(pluginMutex_);
    // The editor creates its own child windows under this host window. Many plug-ins return 0 even when they
    // opened successfully, so the window is considered open once the call returned (like the managed host).
    if (!EditorDispatchGuarded(kEffEditOpen, 0, 0, parent, 0, nullptr)) return false;
    editorOpen_ = true;
    return true;
}

void Vst2Host::CloseEditor() {
    if (effect_ == nullptr || !editorOpen_) return;
    std::lock_guard<std::mutex> lock(pluginMutex_);
    editorOpen_ = false;
    EditorDispatchGuarded(kEffEditClose, 0, 0, nullptr, 0, nullptr);
}

void Vst2Host::EditorIdle() {
    if (effect_ == nullptr || !editorOpen_) return;
    std::lock_guard<std::mutex> lock(pluginMutex_);
    EditorDispatchGuarded(kEffEditIdle, 0, 0, nullptr, 0, nullptr);
}

bool Vst2Host::TakeEditorResize(int& width, int& height) {
    if (resized_.exchange(0, std::memory_order_acq_rel) == 0) return false;
    width = std::clamp(pendingWidth_.load(std::memory_order_acquire), 1, kMaxEditorDimension);
    height = std::clamp(pendingHeight_.load(std::memory_order_acquire), 1, kMaxEditorDimension);
    return true;
}

}  // namespace tfvst32
