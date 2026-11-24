using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace DualPlayer.App.Startup;

/// <summary>Lists the screens Avalonia knows, numbered from 1 in its order, through a window that is never shown.</summary>
internal sealed class AvaloniaScreenCatalog : IScreenCatalog
{
    /// <remarks>Windows and screens belong to the UI thread; a call from another thread (validation runs on the pool) is marshalled there.</remarks>
    public IReadOnlyList<ScreenInfo> Screens =>
        Dispatcher.UIThread.CheckAccess() ? Read() : Dispatcher.UIThread.Invoke(Read);

    /// <summary>Reads the plain values the pure screen helpers work on from Avalonia's screens.</summary>
    internal static IReadOnlyList<ScreenBounds> ToBounds(IReadOnlyList<Screen> screens) =>
        [.. screens.Select(s => new ScreenBounds(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height, s.IsPrimary))];

    private static IReadOnlyList<ScreenInfo> Read()
    {
        var window = new Window();
        try
        {
            var all = window.Screens.All;
            return ScreenChoice.Map(ToBounds(all));
        }
        finally
        {
            window.Close();
        }
    }
}
