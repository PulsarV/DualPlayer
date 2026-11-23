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
using DualPlayer.Core.Settings;
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
        using var controls = new PlaybackControls(rig.Session, NullLogger<PlaybackControls>.Instance, rig.Engine.Time);
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
        using var controls = new PlaybackControls(rig.Session, NullLogger<PlaybackControls>.Instance, rig.Engine.Time);
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

    // T134 (review-2026-11-23c W7): Esc pressed while a start is running waits behind that start in the session's loop and
    // acts when the start returns. A failed start still prints its error and exits 2; the posted close must not shut the
    // application down with 0 before the start worker has set the exit code. The start is held inside the session (an
    // unreadable offsets file publishes a warning, and the subscriber below blocks on it), so Esc is queued behind it.
    // The race itself needs a seam: App.Play's optional `afterStart` runs on the start worker right after session.Start
    // returns, before the worker reads the closing flag and sets the exit code. Here it holds the worker until the UI
    // thread has handled Exited (the window closed) or one second has passed (the close then waits for the worker).
    [Fact]
    public void Esc_pressed_during_a_start_that_fails_still_prints_the_error_and_exits_2_AC_02_AC_06()
    {
        using var rig = new SessionRig(TwoDevices, null, 600, stored: Result<DeviceOffsets>.Failure("state.unreadable", "offsets file unreadable"));
        using var controls = new PlaybackControls(rig.Session, NullLogger<PlaybackControls>.Instance, rig.Engine.Time);
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        var services = PlayServices(rig, controls);
        var validated = new ValidatedStart(FilePath, new PictureInfo("h264", "High", 1920, 1080), [new ResolvedOutput(1, "no-such-device")], MainScreen);
        using var startHeld = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var windowClosed = new ManualResetEventSlim();
        using var holdOnWarning = rig.Session.Events.Subscribe(new HoldingObserver(startHeld, release));

        var stderr = CaptureConsole(() =>
        {
            PlaybackWindow? window = null;
            HeadlessApp.Run(() =>
            {
                window = new PlaybackWindow { DataContext = services.GetRequiredService<PlaybackViewModel>() };
                window.Closed += (_, _) => windowClosed.Set();
                App.Play(
                    desktop,
                    services,
                    new StartRequest(FilePath, [], null),
                    validated,
                    NullLogger.Instance,
                    () => window,
                    afterStart: () => windowClosed.Wait(TimeSpan.FromSeconds(1)));
                window.RaiseVideoHandleCreated(1);
                startHeld.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("the start never reached the held point");

                controls.Exit();
                Thread.Sleep(200); // Esc reaches the session's loop and waits there behind the held start
                release.Set();

                PumpUntilShutdown(desktop).ShouldBeTrue("the failed start never shut the application down");
            });
        }).Error;

        desktop.Received(1).Shutdown(2);
        desktop.DidNotReceive().Shutdown(0);
        stderr.ShouldContain("error outputs.device_not_found");
    }

    // T136 (review-2026-11-23d V2): the start worker reads `closing`, prints the error line and stores the exit code. A Closed that
    // lands between the line and the stored code used to read exit code 0 and shut down with 0 with the line already printed
    // (cli.md section 4). Here the error line itself posts the window's close and waits for Closed, so the close lands exactly in
    // that gap. The test's Closed handler is added after App.Play's, so it runs last: the app's handler has read the code by then.
    // After the fix the line is written under the lock, Closed waits for the stored code, and the wait below only times out.
    [Fact]
    public void A_window_closed_while_the_error_line_is_written_still_exits_2_with_the_line_AC_02_AC_06()
    {
        using var rig = new SessionRig(TwoDevices, null, 600);
        using var controls = new PlaybackControls(rig.Session, NullLogger<PlaybackControls>.Instance, rig.Engine.Time);
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        var services = PlayServices(rig, controls);
        var validated = new ValidatedStart(FilePath, new PictureInfo("h264", "High", 1920, 1080), [new ResolvedOutput(1, "no-such-device")], MainScreen);
        using var windowClosed = new ManualResetEventSlim();

        var stderr = CaptureConsole(() =>
        {
            PlaybackWindow? window = null;
            Console.SetError(new AfterErrorLineWriter(Console.Error, () =>
            {
                Dispatcher.UIThread.Post(() => window!.Close());
                windowClosed.Wait(TimeSpan.FromSeconds(2));
            }));
            HeadlessApp.Run(() =>
            {
                window = new PlaybackWindow { DataContext = services.GetRequiredService<PlaybackViewModel>() };
                App.Play(desktop, services, new StartRequest(FilePath, [], null), validated, NullLogger.Instance, () => window);
                window.Closed += (_, _) => windowClosed.Set();
                window.RaiseVideoHandleCreated(1);
                PumpUntilShutdown(desktop).ShouldBeTrue("the failed start never shut the application down");
                Thread.Sleep(200); // a second Shutdown, if the code were ever reported twice, would arrive now
                Dispatcher.UIThread.RunJobs();
            });
        }).Error;

        stderr.ShouldContain("error outputs.device_not_found");
        desktop.DidNotReceive().Shutdown(0);
        desktop.Received(1).Shutdown(2);
    }

    // T136 (review-2026-11-23d V1): Esc during a start that succeeds acts when the start returns; the worker closes the window.
    [Fact]
    public void Esc_pressed_during_a_start_that_succeeds_closes_the_window_prints_the_run_summary_and_exits_0_AC_06()
    {
        var folder = Directory.CreateTempSubdirectory("dualplayer-app-entry-");
        var file = Path.Combine(folder.FullName, "Film.mkv");
        File.WriteAllBytes(file, []);
        using var rig = new SessionRig(TwoDevices, null, 600, stored: Result<DeviceOffsets>.Failure("state.unreadable", "offsets file unreadable"));
        using var controls = new PlaybackControls(rig.Session, NullLogger<PlaybackControls>.Instance, rig.Engine.Time);
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        var services = PlayServices(rig, controls);
        var validated = new ValidatedStart(
            file,
            new PictureInfo("h264", "High", 1920, 1080),
            [new ResolvedOutput(1, "dev-1"), new ResolvedOutput(2, "dev-2")],
            MainScreen);
        using var startHeld = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var exited = new ManualResetEventSlim();
        using var holdOnWarning = rig.Session.Events.Subscribe(new HoldingObserver(startHeld, release));

        try
        {
            var captured = CaptureConsole(() =>
            {
                PlaybackWindow? window = null;
                HeadlessApp.Run(() =>
                {
                    window = new PlaybackWindow { DataContext = services.GetRequiredService<PlaybackViewModel>() };
                    App.Play(
                        desktop,
                        services,
                        new StartRequest(file, [], null),
                        validated,
                        NullLogger.Instance,
                        () => window,
                        afterStart: () =>
                        {
                            // Holds the worker until the UI thread has handled Exited (added after App.Play's handler, so its post is already
                            // queued ahead of the no-op posted here): the window stays open and only the worker's `exitRequested` check closes it.
                            exited.Wait(TimeSpan.FromSeconds(10));
                            Dispatcher.UIThread.InvokeAsync(() => { }).GetTask().Wait(TimeSpan.FromSeconds(10));
                        });
                    controls.Exited += () => exited.Set();
                    window.RaiseVideoHandleCreated(1);
                    startHeld.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("the start never reached the held point");

                    controls.Exit();
                    Thread.Sleep(200); // Esc reaches the session's loop and waits there behind the held start
                    release.Set();

                    var advancing = Task.Run(() => rig.Engine.AdvanceUntil(() => rig.Engine.Summary is not null));
                    PumpUntilShutdown(desktop).ShouldBeTrue("Esc during the start never closed the window");
                    advancing.GetAwaiter().GetResult().ShouldBeTrue("Esc never ended the session");
                });
            });

            desktop.Received(1).Shutdown(0);
            captured.Error.ShouldBeEmpty();
            var lines = captured.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => Regex.Replace(l.Trim(), "\\s+", " ")).ToList();
            lines.ShouldContain("run-summary file Film.mkv reason exit", string.Join('\n', lines));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    // T136 (review-2026-11-23d V1): the OS closes the window while the start is running and the start then fails: exit 0, no
    // error line, the abandoned start is logged (cli.md section 4). T140 (review-2026-11-23e U3): the worker waits in `afterStart`
    // for Exited, which the Closed handler raises only after it has marked the close under the lock, so the close always comes first.
    [Fact]
    public void A_window_closed_by_the_OS_during_a_start_that_fails_exits_0_without_an_error_line_and_logs_the_abandoned_start_AC_06()
    {
        using var rig = new SessionRig(TwoDevices, null, 600, stored: Result<DeviceOffsets>.Failure("state.unreadable", "offsets file unreadable"));
        using var controls = new PlaybackControls(rig.Session, NullLogger<PlaybackControls>.Instance, rig.Engine.Time);
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        var services = PlayServices(rig, controls);
        var validated = new ValidatedStart(FilePath, new PictureInfo("h264", "High", 1920, 1080), [new ResolvedOutput(1, "no-such-device")], MainScreen);
        var log = new RecordingLogger<App>();
        using var startHeld = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var exitedSeen = new ManualResetEventSlim();
        using var holdOnWarning = rig.Session.Events.Subscribe(new HoldingObserver(startHeld, release));

        var stderr = CaptureConsole(() =>
        {
            PlaybackWindow? window = null;
            HeadlessApp.Run(() =>
            {
                window = new PlaybackWindow { DataContext = services.GetRequiredService<PlaybackViewModel>() };
                App.Play(desktop, services, new StartRequest(FilePath, [], null), validated, log, () => window, afterStart: () => exitedSeen.Wait(TimeSpan.FromSeconds(10)));
                controls.Exited += () => exitedSeen.Set();
                window.RaiseVideoHandleCreated(1);
                startHeld.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("the start never reached the held point");

                release.Set();
                window.Close(); // the OS closes the window: Closed marks the close, then waits for the session to end
                PumpUntilShutdown(desktop).ShouldBeTrue("closing the window never shut the application down");
                SpinWait.SpinUntil(() => log.Lines.Any(l => l.Contains("The window closed during the start", StringComparison.Ordinal)), TimeSpan.FromSeconds(5));
            });
        }).Error;

        desktop.Received(1).Shutdown(0);
        stderr.ShouldNotContain("error");
        log.Lines.ShouldContain(l => l.Contains("The window closed during the start", StringComparison.Ordinal));
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
        using var controls = new PlaybackControls(rig.Session, NullLogger<PlaybackControls>.Instance, rig.Engine.Time);
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

    // T150 (review-2026-11-23h Y3): App.Play hands the command time (App.CommandTimestamp, taken at process start) to the session
    // and the App logger to the session-record writer. Without the time, StartTimings, the `started ... first-frame` line and the
    // `start` recovery budget count from the session start and under-report the 3 s start budget; with a null logger no cli.md
    // section 3 record reaches the production log. The command time is set 5 s before the start, so the logged
    // CommandToFirstFrameMs must carry at least that much (a start counted from itself takes well under a second on the fake clock).
    [Fact]
    public void App_Play_passes_the_start_command_time_and_the_real_logger_to_the_session_AC_01_AC_17()
    {
        var folder = Directory.CreateTempSubdirectory("dualplayer-app-entry-");
        var file = Path.Combine(folder.FullName, "Film.mkv");
        File.WriteAllBytes(file, []);
        using var rig = new SessionRig(TwoDevices, null, 600);
        using var controls = new PlaybackControls(rig.Session, NullLogger<PlaybackControls>.Instance, rig.Engine.Time);
        var log = new RecordingLogger<App>();
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        var services = PlayServices(rig, controls);
        var validated = new ValidatedStart(
            file,
            new PictureInfo("h264", "High", 1920, 1080),
            [new ResolvedOutput(1, "dev-1"), new ResolvedOutput(2, "dev-2")],
            MainScreen);
        var delay = TimeSpan.FromSeconds(5);
        var previous = App.CommandTimestamp;
        App.CommandTimestamp = rig.Engine.Time.GetTimestamp() - (long)(delay.TotalSeconds * rig.Engine.Time.TimestampFrequency);

        try
        {
            _ = CaptureConsole(() =>
            {
                PlaybackWindow? window = null;
                HeadlessApp.Run(() =>
                {
                    window = new PlaybackWindow { DataContext = services.GetRequiredService<PlaybackViewModel>() };
                    App.Play(desktop, services, new StartRequest(file, [], null), validated, log, () => window);
                    window.RaiseVideoHandleCreated(1);

                    // The start runs on a worker that waits on the rig's fake time: it moves on from here until the start's timings are logged.
                    var advancing = Task.Run(() => rig.Engine.AdvanceUntil(() => log.Lines.Any(l => l.StartsWith("StartTimings ", StringComparison.Ordinal))));
                    advancing.GetAwaiter().GetResult().ShouldBeTrue("the start never logged its StartTimings: " + string.Join('\n', log.Lines));

                    // A pause is the cheapest command that makes the session publish a PictureDelayChanged record (cli.md section 3).
                    controls.TogglePause();
                    var paused = Task.Run(() => rig.Engine.AdvanceUntil(() => log.Lines.Any(l => l.StartsWith("PictureDelayChanged ", StringComparison.Ordinal))));
                    paused.GetAwaiter().GetResult().ShouldBeTrue("the pause never logged a PictureDelayChanged record: " + string.Join('\n', log.Lines));

                    var ending = Task.Run(() => rig.Engine.AdvanceUntil(() => rig.Engine.Summary is not null));
                    window.Close();
                    PumpUntilShutdown(desktop).ShouldBeTrue("closing the window never shut the application down");
                    ending.GetAwaiter().GetResult().ShouldBeTrue("closing the window never ended the session");
                });
            });

            var timings = log.Lines.Single(l => l.StartsWith("StartTimings ", StringComparison.Ordinal));
            var match = Regex.Match(timings, @"CommandToFirstFrameMs=(?<ms>[0-9.,-]+)");
            match.Success.ShouldBeTrue(timings);
            double.Parse(match.Groups["ms"].Value, System.Globalization.CultureInfo.InvariantCulture)
                .ShouldBeGreaterThanOrEqualTo(delay.TotalMilliseconds, "the command time of the start was not passed to the session: " + timings);
            log.Lines.ShouldContain(l => l.StartsWith("LatencyEstimated", StringComparison.Ordinal), string.Join('\n', log.Lines));
            log.Lines.ShouldContain(l => l.StartsWith("PictureDelayChanged", StringComparison.Ordinal), string.Join('\n', log.Lines));
        }
        finally
        {
            App.CommandTimestamp = previous;
            folder.Delete(recursive: true);
        }
    }

    // T154 (review-2026-11-23i AA1): what App.Play prints at the start and where it places the window (cli.md section 1.1).
    // Output 2 carries track 2 on the second device, so a label that is the same for every output, or a device id in place of
    // the name, shows; a screen other than the main one shows in the picture line and in the number the window was placed on.
    [Theory]
    [InlineData(1, 1920, 1080, true)]
    [InlineData(2, 1280, 720, false)]
    public void App_Play_prints_the_output_and_picture_lines_and_places_the_window_on_the_validated_screen_AC_01_AC_03b_AC_12_AC_13(
        int screenNumber, int width, int height, bool isMain)
    {
        var folder = Directory.CreateTempSubdirectory("dualplayer-app-entry-");
        var file = Path.Combine(folder.FullName, "Film.mkv");
        File.WriteAllBytes(file, []);
        using var rig = new SessionRig(TwoDevices, null, 600);
        using var controls = new PlaybackControls(rig.Session, NullLogger<PlaybackControls>.Instance, rig.Engine.Time);
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        var services = PlayServices(rig, controls);
        var validated = new ValidatedStart(
            file,
            new PictureInfo("h264", "High", 1920, 1080),
            [new ResolvedOutput(1, "dev-1"), new ResolvedOutput(2, "dev-2")],
            new ScreenInfo(screenNumber, width, height, isMain));
        int? placedOn = null;

        try
        {
            var stdout = CaptureConsole(() =>
            {
                PlaybackWindow? window = null;
                HeadlessApp.Run(() =>
                {
                    window = new PlaybackWindow { DataContext = services.GetRequiredService<PlaybackViewModel>() };
                    App.Play(desktop, services, new StartRequest(file, [], null), validated, NullLogger.Instance, () => window);
                    placedOn = window.PlacedScreenNumber;
                    window.RaiseVideoHandleCreated(1);

                    var advancing = Task.Run(() => rig.Engine.AdvanceUntil(() => PrintedSoFar().Contains("picture ", StringComparison.Ordinal)));
                    advancing.GetAwaiter().GetResult().ShouldBeTrue("the start never printed its picture line");

                    var ending = Task.Run(() => rig.Engine.AdvanceUntil(() => rig.Engine.Summary is not null));
                    window.Close();
                    PumpUntilShutdown(desktop).ShouldBeTrue("closing the window never shut the application down");
                    ending.GetAwaiter().GetResult().ShouldBeTrue("closing the window never ended the session");
                });
            }).Out;

            var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => Regex.Replace(l.Trim(), "\\s+", " ")).ToList();
            var all = string.Join('\n', lines);
            lines.ShouldContain(l => l.StartsWith("output 1 track 1 device Device 1", StringComparison.Ordinal), all);
            lines.ShouldContain(l => l.StartsWith("output 2 track 2 device Device 2", StringComparison.Ordinal), all);
            lines.ShouldContain(l => Regex.IsMatch(l, $"^picture delay \\S+ screen {screenNumber} {width}x{height}$"), all);
            placedOn.ShouldBe(screenNumber);
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

    private static StringWriter? _capturedOut;

    // What the running CaptureConsole has had written to standard output so far.
    private static string PrintedSoFar() => _capturedOut!.ToString();

    private static (string Out, string Error) CaptureConsole(Action body)
    {
        var (oldOut, oldError) = (Console.Out, Console.Error);
        var (output, error) = (new StringWriter(), new StringWriter());
        _capturedOut = output;
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

    // Blocks the session's loop on the first offsets warning (published inside Start) until released.
    private sealed class HoldingObserver(ManualResetEventSlim held, ManualResetEventSlim release) : IObserver<SessionEvent>
    {
        public void OnNext(SessionEvent value)
        {
            if (value is OffsetsWarning)
            {
                held.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            }
        }

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }

    // Forwards to the writer it wraps; after each error line it runs `afterLine` (on the thread that wrote it).
    private sealed class AfterErrorLineWriter(TextWriter inner, Action afterLine) : TextWriter
    {
        public override System.Text.Encoding Encoding => inner.Encoding;

        public override void Write(char value) => inner.Write(value);

        public override void Write(string? value) => inner.Write(value);

        public override void WriteLine(string? value)
        {
            inner.WriteLine(value);
            if (value is not null && value.StartsWith("error", StringComparison.Ordinal))
            {
                afterLine();
            }
        }
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
