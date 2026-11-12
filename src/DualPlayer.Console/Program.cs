using DualPlayer.Console.Commands;
using DualPlayer.Media.Audio;
using Microsoft.Extensions.DependencyInjection;
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
        AppDomain.CurrentDomain.ProcessExit += (_, _) => serilog.Dispose();
        var log = loggerFactory.CreateLogger(typeof(Program));

        try
        {
            var ffmpeg = FfmpegLibrary.Load();
            if (ffmpeg.IsSuccess)
            {
                StartupDiagnostics.LogFfmpeg(ffmpeg.Value, log);
            }
            else
            {
                log.LogWarning("FFmpeg not loaded: {Code} {Message}", ffmpeg.Error.Code, ffmpeg.Error.Message);
            }

            var parsed = CommandLine.Parse(args);
            if (!parsed.IsSuccess)
            {
                System.Console.Error.WriteLine($"error {parsed.Error.Code}: {parsed.Error.Message}");
                return ExitUsage;
            }

            using var services = ConsoleCompositionRoot.Build(paths, loggerFactory);
            var stdout = System.Console.Out;
            var stderr = System.Console.Error;
            return parsed.Value switch
            {
                DevicesRequest => services.GetRequiredService<DevicesCommand>().Run(stdout, stderr),
                TracksRequest t => services.GetRequiredService<TracksCommand>().Run(t, stdout, stderr),
                PlayRequest p => services.GetRequiredService<PlayCommand>().Run(p, stdout, stderr, global::System.Console.In),
                _ => ExitUsage,
            };
        }
        catch (Exception ex)
        {
            log.LogCritical(ex, "DualPlayer.Console terminated unexpectedly");
            return ExitFailure;
        }
    }
}
