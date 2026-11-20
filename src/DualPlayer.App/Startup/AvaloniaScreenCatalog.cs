using Avalonia.Controls;
using Avalonia.Threading;

namespace DualPlayer.App.Startup;

/// <summary>Lists the screens Avalonia knows, numbered from 1 in its order, through a window that is never shown.</summary>
internal sealed class AvaloniaScreenCatalog : IScreenCatalog
{
    /// <remarks>Windows and screens belong to the UI thread; a call from another thread (validation runs on the pool) is marshalled there.</remarks>
    public IReadOnlyList<ScreenInfo> Screens =>
        Dispatcher.UIThread.CheckAccess() ? Read() : Dispatcher.UIThread.Invoke(Read);

    private static IReadOnlyList<ScreenInfo> Read()
    {
        var window = new Window();
        try
        {
            var all = window.Screens.All;
            return [.. all.Select((s, i) => new ScreenInfo(i + 1, s.Bounds.Width, s.Bounds.Height, s.IsPrimary))];
        }
        finally
        {
            window.Close();
        }
    }
}
