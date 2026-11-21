using DualPlayer.Core;
using DualPlayer.Core.Playback;
using Microsoft.Extensions.Logging;

namespace DualPlayer.App.ViewModels;

/// <summary>
/// Adapts <see cref="PlaybackSession"/> to the commands the playback window drives: each key's command is queued and run
/// off the UI thread in key order; ending the session happens once and tells the host.
/// </summary>
internal sealed class PlaybackControls : IPlaybackControls, IDisposable
{
    private readonly PlaybackSession _session;
    private readonly CommandQueue _queue;
    private readonly TimeProvider _time;
    private int _exited;

    public PlaybackControls(PlaybackSession session, ILogger<PlaybackControls> logger, TimeProvider time)
    {
        _session = session;
        _time = time;
        _queue = new CommandQueue(logger);
    }

    /// <summary>Raised once, after the session has been ended by <see cref="Exit"/> or <see cref="ExitNow"/>.</summary>
    public event Action? Exited;

    public PlaybackState State => _session.State;

    public IObservable<SessionEvent> Events => _session.Events;

    public void TogglePause() =>
        Post("pause", keyAt => _session.State == PlaybackState.Paused ? _session.Resume(keyAt) : _session.Pause());

    public void Seek(TimeSpan step) => Post("seek", keyAt => _session.Seek(step, keyAt));

    public void NextFile() => Post("next-file", keyAt => _session.NextFile(keyAt));

    public void PreviousFile() => Post("previous-file", keyAt => _session.PreviousFile(keyAt));

    public void ChangeOffset(int outputNumber, int deltaMs) => Post("offset", _ => _session.ChangeOffset(outputNumber, deltaMs));

    public void Exit() => _queue.Post("exit", () =>
    {
        ExitNow();
        return Result.Success();
    });

    /// <summary>Ends the session now (the window was closed); does nothing the second time.</summary>
    public void ExitNow()
    {
        if (Interlocked.Exchange(ref _exited, 1) == 1)
        {
            return;
        }

        _session.Exit();
        Exited?.Invoke();
    }

    public void Dispose() => _queue.Dispose();

    // Exit is terminal: a key that comes after it, or is still queued behind it, never reaches the session. The key is
    // stamped when it arrives, so its recovery is timed from the press however long it waits behind earlier commands.
    private void Post(string name, Func<long, Result> command)
    {
        var keyAt = _time.GetTimestamp();
        _queue.Post(name, () => Volatile.Read(ref _exited) == 1 ? Result.Success() : command(keyAt));
    }
}
