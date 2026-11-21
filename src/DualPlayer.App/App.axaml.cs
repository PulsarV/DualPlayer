using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DualPlayer.App.Startup;
using DualPlayer.App.ViewModels;
using DualPlayer.App.Views;
using DualPlayer.Core;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Media.Video;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CoreStartRequest = DualPlayer.Core.Playback.StartRequest;
using StartRequest = DualPlayer.App.Startup.StartRequest;

namespace DualPlayer.App;

public partial class App : Application
{
    /// <summary>Set by <see cref="Program.Main"/> before the Avalonia lifetime starts.</summary>
    internal static IServiceProvider? Services { get; set; }

    /// <summary>The parsed <c>play</c> command; set by <see cref="Program.Main"/>.</summary>
    internal static StartRequest? Request { get; set; }

    /// <summary>When the process received the start command (<see cref="TimeProvider.System"/> timestamps); set by <see cref="Program.Main"/>.</summary>
    internal static long? CommandTimestamp { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    private static Result VideoRendererLibrary()
    {
        var loaded = MpvLibrary.Load();
        return loaded.IsSuccess ? Result.Success() : Result.Failure(loaded.Error);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Services is not null && Request is not null)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunAsync(desktop, Services, Request);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunAsync(IClassicDesktopStyleApplicationLifetime desktop, IServiceProvider services, StartRequest request)
    {
        var log = services.GetRequiredService<ILogger<App>>();
        try
        {
            // Validation first: no window exists until every check has passed.
            var validated = await Task.Run(() => StartCommand.Validate(
                request,
                services.GetRequiredService<IPictureProbe>(),
                services.GetRequiredService<IAudioDecoder>(),
                new DeviceCatalog(services.GetRequiredService<IAudioBackend>().ListDevices()),
                services.GetRequiredService<IScreenCatalog>(),
                VideoRendererLibrary,
                services.GetRequiredService<IDeviceAccessProbe>())).ConfigureAwait(true);

            if (!validated.IsSuccess)
            {
                StartErrors.Report(validated.Error, Console.Error, log);
                desktop.Shutdown(StartExitCodes.For(validated.Error));
                return;
            }

            Play(desktop, services, request, validated.Value, log);
        }
        catch (Exception ex)
        {
            log.LogCritical(ex, "DualPlayer terminated unexpectedly");
            desktop.Shutdown(1);
        }
    }

    internal static void Play(
        IClassicDesktopStyleApplicationLifetime desktop,
        IServiceProvider services,
        StartRequest request,
        ValidatedStart validated,
        ILogger log,
        Func<PlaybackWindow>? createWindow = null)
    {
        var session = services.GetRequiredService<PlaybackSession>();
        var controls = services.GetRequiredService<PlaybackControls>();
        var surface = services.GetRequiredService<VideoSurface>();
        var devices = new DeviceCatalog(services.GetRequiredService<IAudioBackend>().ListDevices());
        var labels = validated.Outputs
            .Select(o => new OutputLabel(o.TrackNumber, devices.Devices.FirstOrDefault(d => d.Id == o.DeviceId)?.Name ?? o.DeviceId))
            .ToList();

        var printer = new TerminalPrinter(
            services.GetRequiredService<IAudioEngine>().Events,
            session.Events,
            Console.Out,
            Console.Error,
            log,
            new TerminalContext(labels, validated.Screen, validated.File, services.GetRequiredService<TimeProvider>()));
        var sessionLog = new SessionEventLogWriter(session.Events, log);

        var window = createWindow?.Invoke() ?? new PlaybackWindow { DataContext = services.GetRequiredService<PlaybackViewModel>() };
        window.PlaceOnScreen(validated.Screen.Number);

        var exitCode = 0;
        var closing = false;
        controls.Exited += () => Dispatcher.UIThread.Post(() =>
        {
            if (!closing)
            {
                closing = true;
                window.Close();
            }
        });
        window.Closed += (_, _) =>
        {
            Volatile.Write(ref closing, true);
            // Closed by the OS or Alt+F4 is the same as Esc: the session ends, the run summary prints, the exit code is 0.
            controls.ExitNow();
            sessionLog.Dispose();
            printer.Dispose();
            desktop.Shutdown(exitCode);
        };

        void CloseWindow() => Dispatcher.UIThread.Post(() =>
        {
            closing = true;
            window.Close();
        });

        window.VideoHandleCreated += handle =>
        {
            surface.Handle = handle;
            _ = Task.Run(() =>
            {
                try
                {
                    // The devices as validated, by OS id: every later start (a file change) keeps them even if the list is renumbered.
                    var outputs = validated.Outputs.Select(o => new OutputAssignment(o.TrackNumber, DeviceRef.ById(o.DeviceId))).ToList();
                    var started = session.Start(new CoreStartRequest(validated.File, outputs, request.Screen) { CommandTimestamp = CommandTimestamp });

                    // Esc during the start: the session ran Exit after this start, so nothing plays on; no error, exit 0.
                    if (StartErrors.AfterSessionStart(started, Volatile.Read(ref closing), Console.Error, log) is { } code)
                    {
                        exitCode = code;
                        CloseWindow();
                    }
                }
                catch (Exception ex)
                {
                    log.LogCritical(ex, "DualPlayer terminated unexpectedly");
                    exitCode = 1;
                    CloseWindow();
                }
            });
        };

        window.Show();
    }
}
