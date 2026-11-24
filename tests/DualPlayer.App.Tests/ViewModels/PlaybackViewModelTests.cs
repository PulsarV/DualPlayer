using Avalonia.Input;
using DualPlayer.App.ViewModels;
using DualPlayer.Core.Playback;
using NSubstitute;

namespace DualPlayer.App.Tests.ViewModels;

public sealed class PlaybackViewModelTests
{
    private sealed class Events : IObservable<SessionEvent>
    {
        private readonly List<IObserver<SessionEvent>> _observers = [];

        public IDisposable Subscribe(IObserver<SessionEvent> observer)
        {
            _observers.Add(observer);
            return new Nop();
        }

        public void Publish(SessionEvent e)
        {
            foreach (var o in _observers.ToArray())
            {
                o.OnNext(e);
            }
        }

        private sealed class Nop : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private readonly IPlaybackControls _controls = Substitute.For<IPlaybackControls>();
    private readonly IVideoRenderer _renderer = Substitute.For<IVideoRenderer>();
    private readonly Events _events = new();
    private readonly PlaybackViewModel _vm;

    public PlaybackViewModelTests()
    {
        _controls.Events.Returns(_events);
        _vm = new PlaybackViewModel(_controls, _renderer);
    }

    [Fact]
    public void Space_toggles_pause()
    {
        _vm.HandleKey(Key.Space).ShouldBeTrue();

        _controls.Received(1).TogglePause();
    }

    [Theory]
    [InlineData(Key.Left, -10)]
    [InlineData(Key.Right, 10)]
    [InlineData(Key.Down, -60)]
    [InlineData(Key.Up, 60)]
    public void Arrow_keys_seek_by_ten_or_sixty_seconds(Key key, int seconds)
    {
        _vm.HandleKey(key).ShouldBeTrue();

        _controls.Received(1).Seek(TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void Page_keys_move_to_previous_and_next_file()
    {
        _vm.HandleKey(Key.PageUp).ShouldBeTrue();
        _vm.HandleKey(Key.PageDown).ShouldBeTrue();

        _controls.Received(1).PreviousFile();
        _controls.Received(1).NextFile();
    }

    [Fact]
    public void Escape_exits()
    {
        _vm.HandleKey(Key.Escape).ShouldBeTrue();

        _controls.Received(1).Exit();
    }

    [Theory]
    [InlineData(Key.D1, 1, 10)]
    [InlineData(Key.Q, 1, -10)]
    [InlineData(Key.D2, 2, 10)]
    [InlineData(Key.W, 2, -10)]
    [InlineData(Key.D3, 3, 10)]
    [InlineData(Key.E, 3, -10)]
    public void Offset_keys_change_that_outputs_offset_by_ten_ms(Key key, int output, int delta)
    {
        _vm.HandleKey(key).ShouldBeTrue();

        _controls.Received(1).ChangeOffset(output, delta);
    }

    [Fact]
    public void Unmapped_key_does_nothing()
    {
        _vm.HandleKey(Key.A).ShouldBeFalse();

        _controls.ReceivedCalls().Count(c => c.GetMethodInfo().Name != "get_Events" && c.GetMethodInfo().Name != "get_State")
            .ShouldBe(0);
    }

    [Fact]
    public void Shown_note_is_forwarded_to_the_renderer_with_its_style()
    {
        _events.Publish(new NoteShown("Output 2 later by 10 ms", NoteStyle.Warning));

        _renderer.Received(1).ShowNote("Output 2 later by 10 ms", NoteStyle.Warning);
    }
}
