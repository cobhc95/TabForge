// The editor surface both hosts expose to EditorWindow. VST2 reads its ERect; VST3 asks the tfvst3 shim.
// All calls run on the helper main thread, never on the audio thread.
#pragma once

#include <windows.h>

namespace tfvst32 {

class EditorTarget {
public:
    virtual ~EditorTarget() = default;

    virtual bool HasEditor() const = 0;
    /// <summary>Gives the plug-in a parent window; true when its editor opened.</summary>
    virtual bool OpenEditor(HWND parent) = 0;
    virtual void CloseEditor() = 0;
    virtual bool EditorOpen() const = 0;
    virtual void EditorIdle() = 0;
    /// <summary>The preferred size after an open: the plug-in's own, else a sane default.</summary>
    virtual bool EditorSize(int& width, int& height) = 0;
    /// <summary>A plug-in-requested resize since the last call (atomic for VST2, shim poll for VST3).</summary>
    virtual bool TakeEditorResize(int& width, int& height) = 0;
    /// <summary>The user resized the helper-owned window: tells the plug-in's view its new client size.</summary>
    virtual void SetEditorSize(int width, int height) = 0;
};

}  // namespace tfvst32
