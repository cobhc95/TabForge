namespace TabForge.Documents;

/// <summary>Applies a tab-switch preference to playback sessions other than the selected document.</summary>
public static class TabPlaybackSwitchPolicy
{
    public static void Apply(IEnumerable<DocumentSession> openDocuments, DocumentSession selected, string? preference)
    {
        var action = TabPlaybackActions.Resolve(preference);
        if (action == TabPlaybackAction.Continue) return;

        foreach (var previous in openDocuments)
        {
            if (ReferenceEquals(previous, selected) || !previous.Playback.Engine.IsPlaying) continue;
            if (action == TabPlaybackAction.Pause)
            {
                previous.Playback.Engine.Pause();
                previous.Playback.Clock.Paused();   // its audio clips pause with it
            }
            else if (action == TabPlaybackAction.Stop)
            {
                previous.Playback.Engine.Stop();
                previous.Playback.Clock.Stopped();
                previous.Playback.ClearPlaybackPosition();
            }
        }
    }
}
