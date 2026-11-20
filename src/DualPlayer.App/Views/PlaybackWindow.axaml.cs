using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using DualPlayer.App.ViewModels;

namespace DualPlayer.App.Views;

/// <summary>The Viewers' whole surface: a black fullscreen window around mpv's native video child, answering the fixed keys.</summary>
public partial class PlaybackWindow : Window
{
    public PlaybackWindow() => InitializeComponent();

    /// <summary>The native window handle mpv renders into; zero until the window is shown.</summary>
    public nint VideoHandle => Video.NativeHandle;

    /// <summary>Raised once the video child exists and its handle can be handed to <c>IVideoRenderer.Load</c>.</summary>
    public event Action<nint>? VideoHandleCreated
    {
        add => Video.NativeHandleCreated += value;
        remove => Video.NativeHandleCreated -= value;
    }

    /// <summary>
    /// Places the window on the screen with the given number (from 1, in the order Avalonia lists them); when no number is
    /// given, or it is unknown, on the main screen. Call before <see cref="Window.Show()"/>.
    /// </summary>
    public void PlaceOnScreen(int? screenNumber)
    {
        var screens = Screens.All;
        var target = screenNumber is >= 1 && screenNumber <= screens.Count
            ? screens[screenNumber.Value - 1]
            : Screens.Primary;

        if (target is null)
        {
            return;
        }

        // Fullscreen goes to the screen the window is on, so move it there first.
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = target.Bounds.Position;
        WindowState = WindowState.FullScreen;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is PlaybackViewModel vm && vm.HandleKey(e.Key))
        {
            e.Handled = true;
        }
    }
}
