using Avalonia;
using DualPlayer.App.Startup;
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
        AppPaths? paths = null;
        return Run(
            args,
            TimeProvider.System,
            Console.Error,
            () =>
            {
                paths = CompositionRoot.PathsForCurrentUser();
                return new SerilogLoggerFactory(CreateSerilog(paths), dispose: true);
            },
            (request, loggerFactory) =>
            {
                using var services = CompositionRoot.Build(paths!, loggerFactory);
                App.Services = services;
                App.Request = request;
                return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            });
    }

    /// <summary>
    /// The process entry without the process: takes the start command's time, sets up logging, parses the command line
    /// and either refuses it (cli.md §4/§5) or starts the App.
    /// </summary>
    internal static int Run(
        string[] args,
        TimeProvider time,
        TextWriter stderr,
        Func<ILoggerFactory> createLogging,
        Func<StartRequest, ILoggerFactory, int> startApp)
    {
        // The start command's time, before anything else runs: the start budget is measured from here (spec §6).
        App.CommandTimestamp = time.GetTimestamp();
        using var loggerFactory = createLogging();
        var log = loggerFactory.CreateLogger(typeof(Program));
        log.LogInformation(
            "DualPlayer starting {Version} on {OS}",
            typeof(Program).Assembly.GetName().Version,
            System.Runtime.InteropServices.RuntimeInformation.OSDescription);

        var parsed = StartCommand.Parse(args);
        if (!parsed.IsSuccess)
        {
            StartErrors.Report(parsed.Error, stderr, log);
            return StartExitCodes.For(parsed.Error);
        }

        try
        {
            return startApp(parsed.Value, loggerFactory);
        }
        catch (Exception ex)
        {
            log.LogCritical(ex, "DualPlayer terminated unexpectedly");
            return 1;
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
