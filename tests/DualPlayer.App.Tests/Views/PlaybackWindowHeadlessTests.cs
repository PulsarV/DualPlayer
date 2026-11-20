using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using DualPlayer.App.ViewModels;
using DualPlayer.App.Views;
using DualPlayer.Core.Playback;
using NSubstitute;

namespace DualPlayer.App.Tests.Views;

/// <summary>
/// The test-plan's window rows (e2e-through-UI): the fixed keys pressed on the real playback window reach the session
/// commands, notes reach the renderer, and the window places itself on the named screen.
/// </summary>
public sealed class PlaybackWindowHeadlessTests
{
    private static (PlaybackWindow Window, IPlaybackControls Controls, IVideoRenderer Renderer) OpenWindow()
    {
        var controls = Substitute.For<IPlaybackControls>();
        controls.Events.Returns(Substitute.For<IObservable<SessionEvent>>());
        var renderer = Substitute.For<IVideoRenderer>();
        var window = new PlaybackWindow { DataContext = new PlaybackViewModel(controls, renderer) };
        window.Show();
        return (window, controls, renderer);
    }

    [Fact]
    public void Space_twice_in_the_window_pauses_and_resumes_AC_04() => HeadlessApp.Run(() =>
    {
        var (window, controls, _) = OpenWindow();

        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");

        controls.Received(2).TogglePause();
        window.Close();
    });

    [Fact]
    public void Arrow_keys_in_the_window_seek_by_10_and_60_seconds_AC_05() => HeadlessApp.Run(() =>
    {
        var (window, controls, _) = OpenWindow();

        window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);

        Received.InOrder(() =>
        {
            controls.Seek(TimeSpan.FromSeconds(-10));
            controls.Seek(TimeSpan.FromSeconds(10));
            controls.Seek(TimeSpan.FromSeconds(-60));
            controls.Seek(TimeSpan.FromSeconds(60));
        });
        window.Close();
    });

    [Fact]
    public void Page_keys_in_the_window_move_to_the_next_and_previous_file_and_Esc_exits_AC_07_AC_15b() => HeadlessApp.Run(() =>
    {
        var (window, controls, _) = OpenWindow();

        window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
        window.KeyPress(Key.PageUp, RawInputModifiers.None, PhysicalKey.PageUp, null);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

        Received.InOrder(() =>
        {
            controls.NextFile();
            controls.PreviousFile();
            controls.Exit();
        });
        window.Close();
    });

    [Fact]
    public void An_offset_key_in_the_window_changes_that_output_and_its_note_reaches_the_picture_AC_10() => HeadlessApp.Run(() =>
    {
        var controls = Substitute.For<IPlaybackControls>();
        var notes = new NoteFeed();
        controls.Events.Returns(notes);
        var renderer = Substitute.For<IVideoRenderer>();
        var window = new PlaybackWindow { DataContext = new PlaybackViewModel(controls, renderer) };
        window.Show();

        window.KeyPress(Key.D2, RawInputModifiers.None, PhysicalKey.Digit2, "2");
        window.KeyPress(Key.Q, RawInputModifiers.None, PhysicalKey.Q, "q");
        notes.Publish(new NoteShown("Output 2: +10 ms", NoteStyle.Info));

        controls.Received(1).ChangeOffset(2, 10);
        controls.Received(1).ChangeOffset(1, -10);
        renderer.Received(1).ShowNote("Output 2: +10 ms", NoteStyle.Info);
        window.Close();
    });

    [Fact]
    public void The_window_opens_fullscreen_on_the_named_screen_or_on_the_main_screen_AC_01() => HeadlessApp.Run(() =>
    {
        var (window, _, _) = OpenWindow();
        var screens = window.Screens.All;
        screens.ShouldNotBeEmpty();

        // Start elsewhere and windowed, so only PlaceOnScreen can put it fullscreen on the screen.
        window.WindowState = WindowState.Normal;
        window.Position = new Avalonia.PixelPoint(screens[0].Bounds.X + 300, screens[0].Bounds.Y + 200);

        window.PlaceOnScreen(1);
        window.WindowState.ShouldBe(WindowState.FullScreen);
        window.Position.ShouldBe(screens[0].Bounds.Position);

        window.WindowState = WindowState.Normal;
        window.Position = new Avalonia.PixelPoint(screens[0].Bounds.X + 300, screens[0].Bounds.Y + 200);
        window.PlaceOnScreen(null);
        window.WindowState.ShouldBe(WindowState.FullScreen);
        window.Position.ShouldBe(window.Screens.Primary!.Bounds.Position);
        window.Close();
    });

    private sealed class NoteFeed : IObservable<SessionEvent>
    {
        private readonly List<IObserver<SessionEvent>> _observers = [];

        public IDisposable Subscribe(IObserver<SessionEvent> observer)
        {
            _observers.Add(observer);
            return Substitute.For<IDisposable>();
        }

        public void Publish(SessionEvent value)
        {
            foreach (var observer in _observers)
            {
                observer.OnNext(value);
            }
        }
    }
}
