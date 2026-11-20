using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using DualPlayer.Core.Playback;

namespace DualPlayer.App.ViewModels;

public sealed partial class PlaybackViewModel : ObservableObject
{
    private const int OffsetStepMs = 10;

    private readonly IPlaybackControls _controls;
    private readonly IVideoRenderer _renderer;

    public PlaybackViewModel(IPlaybackControls controls, IVideoRenderer renderer)
    {
        _controls = controls;
        _renderer = renderer;
        _controls.Events.Subscribe(new NoteObserver(this));
    }

    /// <summary>Maps a Viewer key to a session command; returns whether the key is one of the fixed keys.</summary>
    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Space:
                _ = _controls.State == PlaybackState.Paused ? _controls.Resume() : _controls.Pause();
                return true;
            case Key.Left:
                _ = _controls.Seek(TimeSpan.FromSeconds(-10));
                return true;
            case Key.Right:
                _ = _controls.Seek(TimeSpan.FromSeconds(10));
                return true;
            case Key.Down:
                _ = _controls.Seek(TimeSpan.FromSeconds(-60));
                return true;
            case Key.Up:
                _ = _controls.Seek(TimeSpan.FromSeconds(60));
                return true;
            case Key.PageUp:
                _ = _controls.PreviousFile();
                return true;
            case Key.PageDown:
                _ = _controls.NextFile();
                return true;
            case Key.Escape:
                _controls.Exit();
                return true;
            case Key.D1:
                return Offset(1, OffsetStepMs);
            case Key.Q:
                return Offset(1, -OffsetStepMs);
            case Key.D2:
                return Offset(2, OffsetStepMs);
            case Key.W:
                return Offset(2, -OffsetStepMs);
            case Key.D3:
                return Offset(3, OffsetStepMs);
            case Key.E:
                return Offset(3, -OffsetStepMs);
            default:
                return false;
        }
    }

    private bool Offset(int output, int deltaMs)
    {
        _ = _controls.ChangeOffset(output, deltaMs);
        return true;
    }

    private sealed class NoteObserver(PlaybackViewModel owner) : IObserver<SessionEvent>
    {
        public void OnNext(SessionEvent value)
        {
            if (value is NoteShown note)
            {
                owner._renderer.ShowNote(note.Text, note.Style);
            }
        }

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }
}
