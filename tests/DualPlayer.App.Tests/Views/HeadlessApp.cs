using Avalonia;
using Avalonia.Headless;

namespace DualPlayer.App.Tests.Views;

/// <summary>The App on Avalonia's headless platform: real windows, real key routing, no display (T38).</summary>
public static class HeadlessApp
{
    private static readonly Lazy<HeadlessUnitTestSession> Shared = new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessApp)));

    /// <summary>One headless UI thread for every test in the assembly; each test runs its body on it.</summary>
    public static HeadlessUnitTestSession Session => Shared.Value;

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });

    public static void Run(Action body) =>
        Session.Dispatch(body, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
}
