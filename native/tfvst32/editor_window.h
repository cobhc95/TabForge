// The plug-in editor window: a helper-owned top-level WS_POPUP window, owned by TabForge's window.
// It is never a cross-process child: the UI docks it by moving this popup over the FX area (same class name and
// owner rule as the existing 64-bit isolated host). All plug-in editor calls happen on the helper main thread.
#pragma once

#include <functional>
#include <string>
#include <utility>

#include <windows.h>

#include "editor_target.h"

namespace tfvst32 {

class EditorWindow {
public:
    EditorWindow() = default;
    EditorWindow(const EditorWindow&) = delete;
    EditorWindow& operator=(const EditorWindow&) = delete;
    ~EditorWindow() { Close(false); }

    /// <summary>Registers the window class (once) and sets per-monitor-v2 DPI awareness for this helper process.</summary>
    static void EnableDpiAwareness();

    /// <summary>Opens (or brings forward) the plug-in editor. False with <paramref name="error"/> when it has no editor or the window cannot be created.
    /// Under Wine an owner is optional (and a stale owner is ignored): the helper then owns a top-level window. Windows keeps the owner/docked rules.</summary>
    bool Open(EditorTarget& host, HWND owner, bool docked, const std::string& title, std::string& error);

    /// <summary>Closes the window and the plug-in editor. <paramref name="userClosed"/> sends MsgEditorClosed once.</summary>
    void Close(bool userClosed);

    bool IsOpen() const { return hwnd_ != nullptr; }
    int Width() const { return width_; }
    int Height() const { return height_; }

    /// <summary>Main thread callbacks: the user closed the window, or the plug-in asked for a new size.</summary>
    void SetCallbacks(std::function<void()> userClosed, std::function<void(int, int)> sized);
    /// <summary>Called on the timer while a dispatch has run long (the plug-in's own popup menu loop): service the pipe there.</summary>
    void SetNestedService(std::function<void()> service) { onNestedService_ = std::move(service); }
    /// <summary>The main loop brackets DispatchMessage with these, so the timer can tell a nested menu loop from a normal tick.</summary>
    void BeginDispatch() { dispatchDepth_++; dispatchStarted_ = GetTickCount64(); }
    void EndDispatch() { if (dispatchDepth_ > 0) dispatchDepth_--; }
    /// <summary>Announces the size the editor opened with (after the open reply), so the UI gets one size event.</summary>
    void AnnounceSize() const;

    static const wchar_t* ClassName() { return L"TabForgePluginEditor"; }

private:
    static LRESULT CALLBACK WndProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam);
    static void RegisterClass();
    void OnTimer();
    void ApplySize(int width, int height, bool positionOverOwner);

    EditorTarget* host_ = nullptr;
    HWND hwnd_ = nullptr;
    HWND owner_ = nullptr;
    HWND area_ = nullptr;
    bool docked_ = false;
    int width_ = 400;
    int height_ = 300;
    std::function<void()> onUserClosed_;
    std::function<void(int, int)> onSized_;
    std::function<void()> onNestedService_;
    int dispatchDepth_ = 0;
    ULONGLONG dispatchStarted_ = 0;
};

}  // namespace tfvst32
