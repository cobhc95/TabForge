
namespace TabForge.Avalonia.Features.Recording.Presenter;

// Owns: the cursor position a recording began at (RecordStart), split from RecordingPresenter.cs.
// Does not own: how it is used (RecordingPresenter).
internal sealed partial class RecordingPresenter
{
    internal sealed class RecordStart
    {
        public int Bar { get; set; }
        public int Cell { get; set; }
        public int String { get; set; }
    }
}
