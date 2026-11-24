using DualPlayer.Core.Playback;

namespace DualPlayer.App.ViewModels;

/// <summary>
/// The session commands the playback window drives; implemented by an adapter over <see cref="PlaybackSession"/>.
/// Every command is queued and returns at once: the UI thread never waits for the session (spec §6.1).
/// </summary>
public interface IPlaybackControls
{
    PlaybackState State { get; }

    IObservable<SessionEvent> Events { get; }

    /// <summary>Pauses when playing, resumes when paused; decided when the command runs, not when the key is pressed.</summary>
    void TogglePause();

    void Seek(TimeSpan step);

    void NextFile();

    void PreviousFile();

    void ChangeOffset(int outputNumber, int deltaMs);

    /// <summary>Ends the session after the commands already queued.</summary>
    void Exit();
}
