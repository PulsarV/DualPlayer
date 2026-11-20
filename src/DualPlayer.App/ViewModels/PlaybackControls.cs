using DualPlayer.Core;
using DualPlayer.Core.Playback;

namespace DualPlayer.App.ViewModels;

/// <summary>Adapts <see cref="PlaybackSession"/> to the commands the playback window drives; Exit ends the session once and tells the host.</summary>
internal sealed class PlaybackControls : IPlaybackControls
{
    private readonly PlaybackSession _session;
    private int _exited;

    public PlaybackControls(PlaybackSession session) => _session = session;

    /// <summary>Raised once, after the session has been ended by <see cref="Exit"/>.</summary>
    public event Action? Exited;

    public PlaybackState State => _session.State;

    public IObservable<SessionEvent> Events => _session.Events;

    public Result Pause() => _session.Pause();

    public Result Resume() => _session.Resume();

    public Result Seek(TimeSpan step) => _session.Seek(step);

    public Result NextFile() => _session.NextFile();

    public Result PreviousFile() => _session.PreviousFile();

    public Result ChangeOffset(int outputNumber, int deltaMs) => _session.ChangeOffset(outputNumber, deltaMs);

    public void Exit()
    {
        if (Interlocked.Exchange(ref _exited, 1) == 1)
        {
            return;
        }

        _session.Exit();
        Exited?.Invoke();
    }
}
