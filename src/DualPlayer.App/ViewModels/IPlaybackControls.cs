using DualPlayer.Core;
using DualPlayer.Core.Playback;

namespace DualPlayer.App.ViewModels;

/// <summary>The session commands the playback window drives; implemented by an adapter over <see cref="PlaybackSession"/>.</summary>
public interface IPlaybackControls
{
    PlaybackState State { get; }

    IObservable<SessionEvent> Events { get; }

    Result Pause();

    Result Resume();

    Result Seek(TimeSpan step);

    Result NextFile();

    Result PreviousFile();

    Result ChangeOffset(int outputNumber, int deltaMs);

    void Exit();
}
