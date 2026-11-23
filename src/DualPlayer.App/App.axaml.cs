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
        Func<PlaybackWindow>? createWindow = null,
        Action? afterStart = null)
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

        // The UI thread (Exited, Closed) and the start worker both touch this state, so it is read and written under `gate`.
        var gate = new object();
        var exitCode = 0;
        var closing = false;
        var startRunning = false;
        var exitRequested = false;

        controls.Exited += () => Dispatcher.UIThread.Post(() =>
        {
            lock (gate)
            {
                if (closing)
                {
                    return;
                }

                if (startRunning)
                {
                    // Esc acts when the start returns: the start worker closes the window, after it has set the exit code.
                    exitRequested = true;
                    return;
                }

                closing = true;
            }

            window.Close();
        });
        window.Closed += (_, _) =>
        {
            int code;
            lock (gate)
            {
                closing = true;
                code = exitCode;
            }

            // Closed by the OS or Alt+F4 is the same as Esc: the session ends, the run summary prints, the exit code is 0.
            controls.ExitNow();
            sessionLog.Dispose();
            printer.Dispose();
            desktop.Shutdown(code);
        };

        void CloseWindow() => Dispatcher.UIThread.Post(() =>
        {
            lock (gate)
            {
                closing = true;
            }

            window.Close();
        });

        window.VideoHandleCreated += handle =>
        {
            surface.Handle = handle;
            lock (gate)
            {
                startRunning = true;
            }

            _ = Task.Run(() =>
            {
                var close = false;
                try
                {
                    // The devices as validated, by OS id: every later start (a file change) keeps them even if the list is renumbered.
                    var outputs = validated.Outputs.Select(o => new OutputAssignment(o.TrackNumber, DeviceRef.ById(o.DeviceId))).ToList();
                    var started = session.Start(new CoreStartRequest(validated.File, outputs, request.Screen) { CommandTimestamp = CommandTimestamp });
                    afterStart?.Invoke();

                    // Esc during the start waits behind it and the Exited handler leaves the window open until this worker is done, so a
                    // failed start sets its exit code (2 or 4, with the error line) before the window closes. Exit 0 with no error line is
                    // only a window the OS closed during the start (`closing`); a start that succeeded closes at once if Esc was pressed.
                    // The close check, the error line and the exit code are one step under the lock, so a window the OS closes meanwhile
                    // either came first (exit 0, no line) or finds the code written (2 or 4, with the line).
                    lock (gate)
                    {
                        var code = StartErrors.AfterSessionStart(started, closing, Console.Error, log);
                        if (code is { } failed)
                        {
                            exitCode = failed;
                        }

                        startRunning = false;
                        close = code is not null || exitRequested;
                    }
                }
                catch (Exception ex)
                {
                    log.LogCritical(ex, "DualPlayer terminated unexpectedly");
                    lock (gate)
                    {
                        exitCode = 1;
                        startRunning = false;
                    }

                    close = true;
                }

                if (close)
                {
                    CloseWindow();
                }
            });
        };

        window.Show();
    }
}
