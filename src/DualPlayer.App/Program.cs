using Avalonia;
using DualPlayer.Core;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace DualPlayer.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var paths = CompositionRoot.PathsForCurrentUser();
        using var serilog = CreateSerilog(paths);
        using var loggerFactory = new SerilogLoggerFactory(serilog);
        var log = loggerFactory.CreateLogger(typeof(Program));
        log.LogInformation(
            "DualPlayer starting {Version} on {OS}",
            typeof(Program).Assembly.GetName().Version,
            System.Runtime.InteropServices.RuntimeInformation.OSDescription);

        try
        {
            using var services = CompositionRoot.Build(paths, loggerFactory);
            App.Services = services;
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            log.LogCritical(ex, "DualPlayer terminated unexpectedly");
            throw;
        }
        finally
        {
            log.LogInformation("DualPlayer stopped");
        }
    }

    // Used by the Avalonia previewer as well as Main.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static Serilog.Core.Logger CreateSerilog(AppPaths paths) =>
        new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(paths.LogDirectory, "dualplayer-.log"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 20 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 10,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture)
            .CreateLogger();
}
