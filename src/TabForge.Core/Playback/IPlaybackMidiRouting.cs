namespace TabForge.Playback;

/// <summary>The routes a playback session can configure or use for instrument preview.</summary>
public interface IPlaybackMidiRouting
{
    double AudibleLatencyMs { get; }
    void SetRoutes(int[] routes, bool engineInUse = false);
    bool SendLiveEngineOnly(int status, int data1, int data2);
}
