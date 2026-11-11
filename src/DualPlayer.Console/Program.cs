using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace DualPlayer.Console;

internal static class Program
{
    private const int ExitFailure = 1;
    private const int ExitUsage = 2;

    public static int Main(string[] args)
    {
        var paths = ConsoleCompositionRoot.PathsForCurrentUser();
        using var serilog = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(paths.LogDirectory, "dualplayer-console-.log"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 20 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 10,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture)
            .CreateLogger();
        using var loggerFactory = new SerilogLoggerFactory(serilog);
        var log = loggerFactory.CreateLogger(typeof(Program));

        try
        {
            if (args.Length == 0)
            {
                System.Console.Error.WriteLine("error cli.usage: a command is required");
                return ExitUsage;
            }

            using var services = ConsoleCompositionRoot.Build(paths, loggerFactory);
            System.Console.Error.WriteLine($"error cli.usage: unknown command '{args[0]}'");
            return ExitUsage;
        }
        catch (Exception ex)
        {
            log.LogCritical(ex, "DualPlayer.Console terminated unexpectedly");
            return ExitFailure;
        }
    }
}
