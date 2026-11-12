using DualPlayer.Console.Commands;
using DualPlayer.Core;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using NSubstitute;

namespace DualPlayer.Console.Tests;

public sealed class ListAndPlayCommandTests
{
    private const string IdA = "{0.0.0.00000000}.{00000000-0000-0000-0000-000000000001}";
    private const string IdB = "{0.0.0.00000000}.{00000000-0000-0000-0000-000000000002}";
    private const string File = "movie.mkv";

    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    private static PlayRequest ParsePlay(params string[] args)
    {
        var parsed = CommandLine.Parse(args);
        parsed.IsSuccess.ShouldBeTrue(parsed.Error?.Message);
        return parsed.Value.ShouldBeOfType<PlayRequest>();
    }

    [Fact]
    public void Devices_command_lists_every_device_numbered_from_1_with_name_type_and_full_id_and_exits_0()
    {
        var backend = Substitute.For<IAudioBackend>();
        backend.ListDevices().Returns(
        [
            new AudioDeviceInfo("Test Headset B", "bluetooth", IdB),
            new AudioDeviceInfo("Test Headset A", "bluetooth", IdA),
        ]);

        var exit = new DevicesCommand(backend).Run(_out, _err);

        exit.ShouldBe(0);
        var lines = _out.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines.Length.ShouldBe(3);
        lines[1].ShouldStartWith("1");
        lines[1].ShouldContain("Test Headset A");
        lines[1].ShouldContain("bluetooth");
        lines[1].ShouldContain(IdA);
        lines[2].ShouldStartWith("2");
        lines[2].ShouldContain(IdB);
    }

    [Fact]
    public void Tracks_command_lists_audio_tracks_with_language_layout_and_rate_and_plays_nothing()
    {
        var decoder = Substitute.For<IAudioDecoder>();
        decoder.ListTracks(File).Returns(Result<IReadOnlyList<AudioTrackInfo>>.Success(
        [
            new AudioTrackInfo(1, "ukr", ChannelLayout.Surround51, 48000),
            new AudioTrackInfo(2, "eng", ChannelLayout.Stereo, 44100),
        ]));

        var exit = new TracksCommand(decoder).Run(new TracksRequest(File), _out, _err);

        exit.ShouldBe(0);
        var text = _out.ToString();
        text.ShouldContain("ukr");
        text.ShouldContain("5.1");
        text.ShouldContain("48000");
        text.ShouldContain("eng");
        text.ShouldContain("stereo");
        text.ShouldContain("44100");
        decoder.DidNotReceiveWithAnyArgs().Open(default!, default!);
    }

    [Fact]
    public void Tracks_command_reports_file_unreadable_on_stderr_and_exits_2()
    {
        var decoder = Substitute.For<IAudioDecoder>();
        decoder.ListTracks(File).Returns(Result<IReadOnlyList<AudioTrackInfo>>.Failure("playback.file_unreadable", "cannot open movie.mkv"));

        var exit = new TracksCommand(decoder).Run(new TracksRequest(File), _out, _err);

        exit.ShouldBe(2);
        _err.ToString().ShouldContain("error playback.file_unreadable: cannot open movie.mkv");
    }

    [Fact]
    public void Parser_builds_a_play_request_with_numbered_and_id_devices_and_default_options()
    {
        var request = ParsePlay("play", File, "--out", "1=1", "--out", $"1={IdB}");

        request.File.ShouldBe(File);
        request.Outputs.Count.ShouldBe(2);
        request.Outputs[0].ShouldBe(new OutputAssignment(1, DeviceRef.ByNumber(1)));
        request.Outputs[1].ShouldBe(new OutputAssignment(1, DeviceRef.ById(IdB)));
        request.Options.RingBuffer.ShouldBe(TimeSpan.FromMilliseconds(2000));
        request.Options.StartLevel.ShouldBe(TimeSpan.FromMilliseconds(500));
        request.Options.DeviceBuffer.ShouldBe(TimeSpan.FromMilliseconds(40));
    }

    [Fact]
    public void Parser_applies_buffer_options_in_milliseconds()
    {
        var request = ParsePlay("play", File, "--out", "1=1", "--ring-ms", "3000", "--start-level-ms", "800", "--device-buffer-ms", "60");

        request.Options.RingBuffer.ShouldBe(TimeSpan.FromMilliseconds(3000));
        request.Options.StartLevel.ShouldBe(TimeSpan.FromMilliseconds(800));
        request.Options.DeviceBuffer.ShouldBe(TimeSpan.FromMilliseconds(60));
    }

    [Theory]
    [InlineData("play", File)]
    [InlineData("play", File, "--out", "1")]
    [InlineData("play", File, "--out", "x=1")]
    [InlineData("play", File, "--out", "1=1", "--ring-ms", "abc")]
    [InlineData("play", File, "--out", "1=1", "--ring-ms", "400", "--start-level-ms", "500")]
    [InlineData("tracks")]
    [InlineData("bogus")]
    public void Parser_rejects_malformed_arguments_with_cli_usage(params string[] args)
    {
        var parsed = CommandLine.Parse(args);

        parsed.IsSuccess.ShouldBeFalse();
        parsed.Error!.Code.ShouldBe("cli.usage");
    }

    [Fact]
    public void Parser_recognises_devices_and_tracks_commands()
    {
        CommandLine.Parse(["devices"]).Value.ShouldBeOfType<DevicesRequest>();
        CommandLine.Parse(["tracks", File]).Value.ShouldBeOfType<TracksRequest>().File.ShouldBe(File);
    }

    [Fact]
    public void Play_command_starts_the_run_and_echoes_each_output_with_its_track_device_name_and_resolved_id()
    {
        var engine = Substitute.For<IAudioEngine>();
        var request = ParsePlay("play", File, "--out", "1=1", "--out", "2=2");
        engine.StartRun(File, request.Outputs, request.Options).Returns(Result<RunStarted>.Success(new RunStarted(
        [
            new StartedOutput(1, IdA, "Test Headset A", new MixFormat(48000, 2, ChannelLayout.Stereo)),
            new StartedOutput(2, IdB, "Test Headset B", new MixFormat(48000, 2, ChannelLayout.Stereo)),
        ], TimeSpan.Zero)));

        var exit = new PlayCommand(engine).Run(request, _out, _err);

        exit.ShouldBe(0);
        var lines = _out.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines.ShouldContain(l => l.StartsWith("output 1") && l.Contains("track 1") && l.Contains("Test Headset A") && l.Contains(IdA));
        lines.ShouldContain(l => l.StartsWith("output 2") && l.Contains("track 2") && l.Contains("Test Headset B") && l.Contains(IdB));
        _err.ToString().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("playback.file_unreadable", "cannot open movie.mkv", 2)]
    [InlineData("playback.track_not_found", "track 3 does not exist, the file has tracks 1 and 2", 2)]
    [InlineData("outputs.device_not_found", "device 9 is not a connected playback device", 2)]
    [InlineData("outputs.device_assigned_twice", "one audio output per device", 2)]
    [InlineData("outputs.device_access_denied", "the operating system denied access to Test Headset A", 4)]
    public void Play_command_maps_a_refusal_to_stderr_code_message_and_exit_code_without_a_started_line(string code, string message, int expectedExit)
    {
        var engine = Substitute.For<IAudioEngine>();
        engine.StartRun(default!, default!, default!).ReturnsForAnyArgs(Result<RunStarted>.Failure(code, message));
        var request = ParsePlay("play", File, "--out", "1=1");

        var exit = new PlayCommand(engine).Run(request, _out, _err);

        exit.ShouldBe(expectedExit);
        _err.ToString().ShouldContain($"error {code}: {message}");
        _out.ToString().ShouldNotContain("started");
    }
}
