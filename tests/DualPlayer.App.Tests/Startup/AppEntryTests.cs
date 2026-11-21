using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using DualPlayer.App.Startup;
using DualPlayer.App.Tests.Views;
using DualPlayer.App.ViewModels;
using DualPlayer.App.Views;
using DualPlayer.Core;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StartRequest = DualPlayer.App.Startup.StartRequest;

namespace DualPlayer.App.Tests.Startup;

/// <summary>
/// The entry glue of <c>App.RunAsync</c> and <c>App.Play</c> (T46, review-2026-11-21 R11): the exit code and the standard
/// error line after a start failure, and the closing path. <c>RunAsync</c> is private (called by reflection), <c>Play</c> is internal; both get
/// a substitute desktop lifetime that records <c>Shutdown(exitCode)</c>; standard output and error are process-wide, so these
/// tests run alone (the <see cref="ConsoleCollection"/>).
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class AppEntryTests
{
    private const string FilePath = "/films/a.mkv";

    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];

    private static readonly ScreenInfo MainScreen = new(1, 1920, 1080, true);

    [Theory]
    [InlineData("video.no_picture", 2)]
    [InlineData("playback.file_access_denied", 4)]
    public void A_start_refused_before_any_window_prints_the_error_line_logs_it_and_shuts_down_with_its_exit_code(string code, int exitCode)
    {
        var log = new RecordingLogger<App>();
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        var services = EntryServices(log, new FailingProbe(new Error(code, "the file cannot be played")));

        var stderr = CaptureConsole(() => RunAsync(desktop, services, new StartRequest(FilePath, [new OutputAssignment(1, DeviceRef.ByNumber(1))], null)).GetAwaiter().GetResult()).Error;

        desktop.Received(1).Shutdown(exitCode);
        stderr.ShouldContain($"error {code}: the file cannot be played");
        log.Lines.ShouldContain(l => l.Contains($"Start refused: {code}", StringComparison.Ordinal));
    }

    // T55 (review-2026-11-21b Q14): the device is opened and released during validation, so a refusal exits 4 before
    // App.Play. The container holds no PlaybackSession: had Play been reached, it would have failed and shut down with 1.
    [Fact]
    public void A_device_that_refuses_to_open_shuts_down_with_exit_code_4_before_any_window_opens()
    {
        var log = new RecordingLogger<App>();
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        var backend = Substitute.For<IAudioBackend>();
        backend.ListDevices().Returns([new AudioDeviceInfo("Speakers", null, "id-a")]);
        backend.Open("id-a", Arg.Any<TimeSpan>(), Arg.Any<IRenderSource>())
            .Returns(Result<IAudioDevice>.Failure("outputs.device_access_denied", "Windows refused access to device Speakers."));
        var services = EntryServices(log, new PictureProbe(), new OneTrackDecoder(), backend);

        var stderr = CaptureConsole(() => RunAsync(desktop, services, new StartRequest(FilePath, [new OutputAssignment(1, DeviceRef.ByNumber(1))], null)).GetAwaiter().GetResult()).Error;

        desktop.Received(1).Shutdown(4);
        desktop.DidNotReceive().Shutdown(1);
        stderr.ShouldContain("error outputs.device_access_denied: Windows refused access to device Speakers.");
        backend.Received(1).Open("id-a", Arg.Any<TimeSpan>(), Arg.Any<IRenderSource>());
        services.GetService<PlaybackSession>().ShouldBeNull();
        log.Entries.ShouldNotContain(e => e.Level == LogLevel.Critical);
    }

    [Fact]
    public void A_crash_while_validating_the_start_is_logged_as_critical_and_shuts_down_with_exit_code_1()
    {
        var log = new RecordingLogger<App>();
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        var services = EntryServices(log, new ThrowingProbe());

        CaptureConsole(() => RunAsync(desktop, services, new StartRequest(FilePath, [new OutputAssignment(1, DeviceRef.ByNumber(1))], null)).GetAwaiter().GetResult());

        desktop.Received(1).Shutdown(1);
        log.Entries.ShouldContain(e => e.Level == LogLevel.Critical && e.Exception is InvalidOperationException);
    }

    // The engine rig and the fake time run on the test's own thread (as in PlayEndToEndTests); only the window work of
    // App.Play runs on the headless UI thread, which then closes the window by itself when Esc has ended the session.
    [Fact]
    public void Esc_closes_the_window_prints_the_run_summary_and_shuts_down_with_exit_code_0()
    {
        var folder = Directory.CreateTempSubdirectory("dualplayer-app-entry-");
        var file = Path.Combine(folder.FullName, "Film.mkv");
        File.WriteAllBytes(file, []);
        using var rig = new SessionRig(TwoDevices, null, 600);
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = file })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        using var controls = new PlaybackControls(rig.Session, NullLogger<PlaybackControls>.Instance);
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        var services = PlayServices(rig, controls);
        var validated = new ValidatedStart(
            file,
            new PictureInfo("h264", "High", 1920, 1080),
            [new ResolvedOutput(1, "dev-1"), new ResolvedOutput(2, "dev-2")],
            MainScreen);

        try
        {
            var stdout = CaptureConsole(() =>
            {
                var exited = false;
                controls.Exited += () => exited = true;

                // On the UI thread: the window is built, Esc is pressed and the posted close is run; the engine rig's fake
                // time moves on a worker meanwhile, as the session needs it to end the run.
                HeadlessApp.Run(() =>
                {
                    App.Play(desktop, services, new StartRequest(file, [], null), validated, NullLogger.Instance);
                    controls.Exit();
                    var advancing = Task.Run(() => rig.Engine.AdvanceUntil(() => exited));
                    PumpUntilShutdown(desktop).ShouldBeTrue("closing the window never shut the application down");
                    advancing.GetAwaiter().GetResult().ShouldBeTrue("Esc never ended the session");
                });
            }).Out;

            desktop.Received(1).Shutdown(0);
            var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => Regex.Replace(l.Trim(), "\\s+", " ")).ToList();
            lines.ShouldContain("run-summary file Film.mkv reason exit", string.Join('\n', lines));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    // The video handle only appears on a native host, which the headless platform does not have:
    // the test hands App.Play its own window and raises the handle itself.
    [Fact]
    public void A_session_start_that_fails_after_the_window_opened_prints_the_error_line_closes_the_window_and_shuts_down_with_its_exit_code()
    {
        using var rig = new SessionRig(TwoDevices, null, 600);
        using var controls = new PlaybackControls(rig.Session, NullLogger<PlaybackControls>.Instance);
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        var services = PlayServices(rig, controls);

        // The one output names a device that is not connected, so the session's start fails with outputs.device_not_found.
        var validated = new ValidatedStart(FilePath, new PictureInfo("h264", "High", 1920, 1080), [new ResolvedOutput(1, "no-such-device")], MainScreen);

        var stderr = CaptureConsole(() =>
        {
            PlaybackWindow? window = null;
            HeadlessApp.Run(() =>
            {
                window = new PlaybackWindow { DataContext = services.GetRequiredService<PlaybackViewModel>() };
                App.Play(desktop, services, new StartRequest(FilePath, [], null), validated, NullLogger.Instance, () => window);
                window.RaiseVideoHandleCreated(1);
                PumpUntilShutdown(desktop).ShouldBeTrue("the failed start never shut the application down");
            });
        }).Error;

        desktop.Received(1).Shutdown(2);
        stderr.ShouldContain("error outputs.device_not_found");
    }

    // Closing the window without Esc (the OS, Alt+F4) must end the session like Esc: the run summary is
    // printed and the exit code is 0. Esc alone would hide a Closed handler that forgets to end the session.
    [Fact]
    public void Closing_the_window_without_Esc_ends_the_session_prints_the_run_summary_and_shuts_down_with_exit_code_0()
    {
        var folder = Directory.CreateTempSubdirectory("dualplayer-app-entry-");
        var file = Path.Combine(folder.FullName, "Film.mkv");
        File.WriteAllBytes(file, []);
        using var rig = new SessionRig(TwoDevices, null, 600);
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = file })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        using var controls = new PlaybackControls(rig.Session, NullLogger<PlaybackControls>.Instance);
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        var services = PlayServices(rig, controls);
        var validated = new ValidatedStart(
            file,
            new PictureInfo("h264", "High", 1920, 1080),
            [new ResolvedOutput(1, "dev-1"), new ResolvedOutput(2, "dev-2")],
            MainScreen);

        try
        {
            var stdout = CaptureConsole(() =>
            {
                PlaybackWindow? window = null;
                HeadlessApp.Run(() =>
                {
                    window = new PlaybackWindow { DataContext = services.GetRequiredService<PlaybackViewModel>() };
                    App.Play(desktop, services, new StartRequest(file, [], null), validated, NullLogger.Instance, () => window);

                    // The close ends the session on the UI thread, which needs the rig's fake time to move on a worker.
                    var advancing = Task.Run(() => rig.Engine.AdvanceUntil(() => rig.Engine.Summary is not null));
                    window.Close();
                    PumpUntilShutdown(desktop).ShouldBeTrue("closing the window never shut the application down");
                    advancing.GetAwaiter().GetResult().ShouldBeTrue("closing the window never ended the session");
                });
            }).Out;

            desktop.Received(1).Shutdown(0);
            var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => Regex.Replace(l.Trim(), "\\s+", " ")).ToList();
            lines.ShouldContain("run-summary file Film.mkv reason exit", string.Join('\n', lines));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    // Runs on the headless UI thread: runs the jobs posted to it (the window's close, posted by Esc or by a failed start)
    // between looks at the desktop lifetime, until it was told to shut down.
    private static bool PumpUntilShutdown(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (desktop.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IClassicDesktopStyleApplicationLifetime.Shutdown)))
            {
                return true;
            }

            Thread.Sleep(5);
        }

        return false;
    }

    private static Task RunAsync(IClassicDesktopStyleApplicationLifetime desktop, IServiceProvider services, StartRequest request)
    {
        var method = typeof(App).GetMethod("RunAsync", BindingFlags.NonPublic | BindingFlags.Static).ShouldNotBeNull("App.RunAsync was renamed or removed");
        return (Task)method.Invoke(null, [desktop, services, request])!;
    }

    // What RunAsync reads from the container for a start that is refused before any window.
    private static ServiceProvider EntryServices(RecordingLogger<App> log, IPictureProbe probe, IAudioDecoder? decoder = null, IAudioBackend? backend = null)
    {
        if (backend is null)
        {
            backend = Substitute.For<IAudioBackend>();
            backend.ListDevices().Returns([new AudioDeviceInfo("Speakers", null, "id-a")]);
        }

        return new ServiceCollection()
            .AddSingleton<ILogger<App>>(log)
            .AddSingleton(probe)
            .AddSingleton(decoder ?? new NoTracksDecoder())
            .AddSingleton(backend)
            .AddSingleton<IScreenCatalog>(new OneScreen())
            .AddSingleton<IDeviceAccessProbe>(new BackendDeviceAccessProbe(backend))
            .BuildServiceProvider();
    }

    // What App.Play reads from the container: the real session, controls and view model over the engine rig.
    private static ServiceProvider PlayServices(SessionRig rig, PlaybackControls controls) =>
        new ServiceCollection()
            .AddSingleton(rig.Session)
            .AddSingleton(controls)
            .AddSingleton(new VideoSurface())
            .AddSingleton<IAudioBackend>(rig.Engine.Backend)
            .AddSingleton<IAudioEngine>(rig.Engine.Engine)
            .AddSingleton<TimeProvider>(rig.Engine.Time)
            .AddSingleton(new PlaybackViewModel(controls, rig.Renderer))
            .BuildServiceProvider();

    private static (string Out, string Error) CaptureConsole(Action body)
    {
        var (oldOut, oldError) = (Console.Out, Console.Error);
        var (output, error) = (new StringWriter(), new StringWriter());
        Console.SetOut(TextWriter.Synchronized(output));
        Console.SetError(TextWriter.Synchronized(error));
        try
        {
            body();
        }
        finally
        {
            Console.SetOut(oldOut);
            Console.SetError(oldError);
        }

        return (output.ToString(), error.ToString());
    }

    private sealed class FailingProbe(Error error) : IPictureProbe
    {
        public Result<PictureInfo> Probe(string path, TimeSpan timeLimit) => Result<PictureInfo>.Failure(error);
    }

    private sealed class ThrowingProbe : IPictureProbe
    {
        public Result<PictureInfo> Probe(string path, TimeSpan timeLimit) => throw new InvalidOperationException("the probe crashed");
    }

    private sealed class PictureProbe : IPictureProbe
    {
        public Result<PictureInfo> Probe(string path, TimeSpan timeLimit) => Result<PictureInfo>.Success(new PictureInfo("h264", "High", 1920, 1080));
    }

    private sealed class OneTrackDecoder : IAudioDecoder
    {
        public Result<IReadOnlyList<AudioTrackInfo>> ListTracks(string path) =>
            Result<IReadOnlyList<AudioTrackInfo>>.Success([new AudioTrackInfo(1, "en", ChannelLayout.Stereo, 48000)]);

        public Result<IDecodeSession> Open(string path, IReadOnlyList<DecodeRequest> outputs) =>
            throw new InvalidOperationException("validation must not open a decode session");
    }

    private sealed class NoTracksDecoder : IAudioDecoder
    {
        public Result<IReadOnlyList<AudioTrackInfo>> ListTracks(string path) => Result<IReadOnlyList<AudioTrackInfo>>.Success([]);

        public Result<IDecodeSession> Open(string path, IReadOnlyList<DecodeRequest> outputs) =>
            throw new InvalidOperationException("validation must not open a decode session");
    }

    private sealed class OneScreen : IScreenCatalog
    {
        public IReadOnlyList<ScreenInfo> Screens { get; } = [MainScreen];
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<(LogLevel Level, string Text, Exception? Exception)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Text, Exception? Exception)> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public IReadOnlyList<string> Lines => [.. Entries.Select(e => e.Text)];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
            {
                _entries.Add((logLevel, formatter(state, exception), exception));
            }
        }
    }
}

/// <summary>Tests that swap the process-wide standard output or error run one at a time, apart from every other test.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleCollection
{
    public const string Name = "Process-wide console";
}
