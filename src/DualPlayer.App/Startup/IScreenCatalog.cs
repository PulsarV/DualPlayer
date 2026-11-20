namespace DualPlayer.App.Startup;

/// <summary>A connected screen: number from 1, resolution and whether it is the main screen.</summary>
internal sealed record ScreenInfo(int Number, int Width, int Height, bool IsMain);

/// <summary>The connected screens, queried before any window is created.</summary>
internal interface IScreenCatalog
{
    IReadOnlyList<ScreenInfo> Screens { get; }
}
