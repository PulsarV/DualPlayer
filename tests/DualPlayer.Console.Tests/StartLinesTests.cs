using DualPlayer.Console.Commands;
using DualPlayer.Core;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;
using DualPlayer.Media.Audio;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace DualPlayer.Console.Tests;

/// <summary>T30 (F12, F25): the exact start lines of contracts/cli.md section 1.3 and the FFmpeg version/license log line.</summary>
public sealed class StartLinesTests
{
    private const string IdA = "{0.0.0.00000000}.{00000000-0000-0000-0000-000000000001}";
    private const string IdB = "{0.0.0.00000000}.{00000000-0000-0000-0000-000000000002}";

    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    private static string[] SplitLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static MixFormat Stereo48() => new(48000, 2, ChannelLayout.Stereo);

    private string[] PlayLines(RunStarted started, int outputs = 1)
    {
        var engine = Substitute.For<IAudioEngine>();
        var assignments = Enumerable.Range(1, outputs).Select(i => new OutputAssignment(i, DeviceRef.ByNumber(i))).ToArray();
        var request = new PlayRequest("movie.mkv", assignments, new EngineOptions());
        engine.StartRun(default!, default!, default!).ReturnsForAnyArgs(Result<RunStarted>.Success(started));

        new PlayCommand(engine).Run(request, _out, _err);

        return SplitLines(_out.ToString());
    }

    [Fact]
    public void A_stereo_track_on_a_stereo_device_at_the_same_rate_prints_the_language_and_layout_only()
    {
        var lines = PlayLines(new RunStarted(
            [new StartedOutput(1, IdA, "Test Headset A", Stereo48()) { Source = new AudioTrackInfo(1, "eng", ChannelLayout.Stereo, 48000) }],
            TimeSpan.Zero));

        lines[0].ShouldBe($"output 1  track 1 (eng stereo)  device Test Headset A  id {IdA}");
    }

    [Fact]
    public void A_5_1_track_folded_to_a_stereo_device_prints_the_fold_and_that_the_LFE_is_dropped()
    {
        var lines = PlayLines(new RunStarted(
            [new StartedOutput(1, IdA, "Test Headset A", Stereo48()) { Source = new AudioTrackInfo(1, "ukr", ChannelLayout.Surround51, 48000) }],
            TimeSpan.Zero));

        lines[0].ShouldBe($"output 1  track 1 (ukr 5.1 -> stereo, LFE dropped)  device Test Headset A  id {IdA}");
    }

    [Fact]
    public void A_44100_Hz_stereo_track_on_a_48000_Hz_device_prints_the_source_rate_and_the_device_rate()
    {
        var lines = PlayLines(
            new RunStarted(
                [
                    new StartedOutput(1, IdA, "Test Headset A", Stereo48()) { Source = new AudioTrackInfo(1, "ukr", ChannelLayout.Surround51, 48000) },
                    new StartedOutput(2, IdB, "Test Headset B", Stereo48()) { Source = new AudioTrackInfo(2, "eng", ChannelLayout.Stereo, 44100) },
                ],
                TimeSpan.Zero),
            outputs: 2);

        lines[0].ShouldBe($"output 1  track 1 (ukr 5.1 -> stereo, LFE dropped)  device Test Headset A  id {IdA}");
        lines[1].ShouldBe($"output 2  track 2 (eng stereo 44100 -> 48000)  device Test Headset B  id {IdB}");
        lines.Length.ShouldBe(2, "the started line now comes from the event printer on AudioStarted");
    }

    [Fact]
    public void The_real_engine_reports_each_started_output_with_the_source_track_it_decodes()
    {
        using var rig = new EngineRig([new RigDevice(48_000), new RigDevice(48_000)]);
        var assignments = new[] { new OutputAssignment(1, DeviceRef.ByNumber(1)), new OutputAssignment(2, DeviceRef.ByNumber(2)) };

        var result = rig.Call(() => rig.Engine.StartRun("film.mkv", assignments, new EngineOptions()));

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        result.Value.Outputs[0].Source.ShouldBe(new AudioTrackInfo(1, "eng", ChannelLayout.Stereo, 48_000));
        result.Value.Outputs[1].Source.ShouldBe(new AudioTrackInfo(2, "ukr", ChannelLayout.Stereo, 48_000));
    }

    // ---- F25 ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Console_start_logs_the_ffmpeg_version_and_license_once_at_information_level()
    {
        var log = new CaptureLogger();

        StartupDiagnostics.LogFfmpeg(new FfmpegInfo("7.1.1", "LGPL version 2.1 or later", 7), log);

        var entry = log.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldContain("7.1.1");
        entry.Message.ShouldContain("LGPL version 2.1 or later");
    }

    [Theory]
    [InlineData("GPL version 2 or later")]
    [InlineData("nonfree and unredistributable")]
    public void A_license_that_is_not_LGPL_also_logs_a_warning_naming_it(string license)
    {
        var log = new CaptureLogger();

        StartupDiagnostics.LogFfmpeg(new FfmpegInfo("7.1.1", license, 7), log);

        log.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains(license, StringComparison.Ordinal));
        log.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("7.1.1", StringComparison.Ordinal));
    }

    [Fact]
    public void An_LGPL_license_logs_no_warning()
    {
        var log = new CaptureLogger();

        StartupDiagnostics.LogFfmpeg(new FfmpegInfo("7.1.1", "LGPL version 2.1 or later", 7), log);

        log.Entries.ShouldNotContain(e => e.Level >= LogLevel.Warning);
    }

    private sealed class CaptureLogger : ILogger
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _entries.Add((logLevel, formatter(state, exception)));
    }
}
