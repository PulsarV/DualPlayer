using DualPlayer.App.Startup;
using DualPlayer.Core;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using NSubstitute;
using StartRequest = DualPlayer.App.Startup.StartRequest;

namespace DualPlayer.App.Tests.Startup;

public sealed class StartCommandTests
{
    private const string File = "/films/a.mkv";

    private static readonly AudioDeviceInfo DevA = new("Speakers", null, "id-a");
    private static readonly AudioDeviceInfo DevB = new("Headset", null, "id-b");

    [Fact]
    public void Parse_reads_file_outputs_in_order_and_screen()
    {
        var r = StartCommand.Parse(["play", File, "--out", "1=1", "--out", "2=id-b", "--screen", "2"]);

        r.IsSuccess.ShouldBeTrue();
        r.Value.File.ShouldBe(File);
        r.Value.Screen.ShouldBe(2);
        r.Value.Outputs.Count.ShouldBe(2);
        r.Value.Outputs[0].ShouldBe(new OutputAssignment(1, DeviceRef.ByNumber(1)));
        r.Value.Outputs[1].ShouldBe(new OutputAssignment(2, DeviceRef.ById("id-b")));
    }

    [Fact]
    public void Parse_allows_the_same_track_on_two_outputs()
    {
        StartCommand.Parse(["play", File, "--out", "1=1", "--out", "1=2"]).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("play")]
    [InlineData("play|/films/a.mkv")]
    [InlineData("play|/films/a.mkv|--out|x=1")]
    [InlineData("play|/films/a.mkv|--out|1")]
    [InlineData("play|/films/a.mkv|--out|1=1|--screen|0")]
    [InlineData("play|/films/a.mkv|--out|1=1|--screen|abc")]
    [InlineData("play|/films/a.mkv|--out|1=1|--screen")]
    [InlineData("play|/films/a.mkv|--bogus")]
    public void Parse_rejects_missing_or_malformed_arguments_as_cli_usage(string joined)
    {
        var r = StartCommand.Parse(joined.Split('|'));

        r.IsSuccess.ShouldBeFalse();
        r.Error.Code.ShouldBe("cli.usage");
        StartExitCodes.For(r.Error).ShouldBe(2);
    }

    [Fact]
    public void Parse_rejects_four_outputs_as_too_many()
    {
        var r = StartCommand.Parse(["play", File, "--out", "1=1", "--out", "1=2", "--out", "1=3", "--out", "1=4"]);

        r.Error!.Code.ShouldBe("outputs.too_many");
        StartExitCodes.For(r.Error).ShouldBe(2);
    }

    [Fact]
    public void Validate_succeeds_and_resolves_outputs_and_default_main_screen()
    {
        var r = Validate(Request(), new Rig());

        r.IsSuccess.ShouldBeTrue();
        r.Value.Outputs.ShouldBe([new ResolvedOutput(1, "id-a"), new ResolvedOutput(2, "id-b")]);
        r.Value.Screen.IsMain.ShouldBeTrue();
    }

    [Fact]
    public void Validate_file_without_picture_is_reported_with_exit_2()
    {
        var rig = new Rig();
        rig.Probe.Fail(File, "video.no_picture", "the file has no picture");

        var r = Validate(Request(), rig);

        r.Error!.Code.ShouldBe("video.no_picture");
        StartExitCodes.For(r.Error).ShouldBe(2);
    }

    [Fact]
    public void Validate_picture_the_pc_cannot_decode_names_the_format()
    {
        var rig = new Rig();
        rig.Probe.Fail(File, "video.not_hardware_decodable", "picture format av1 Main cannot be decoded in hardware");

        var r = Validate(Request(), rig);

        r.Error!.Code.ShouldBe("video.not_hardware_decodable");
        r.Error.Message.ShouldContain("av1");
        StartExitCodes.For(r.Error).ShouldBe(2);
    }

    [Fact]
    public void Validate_picture_probe_that_ran_out_of_time_is_refused_like_an_undecodable_picture_with_exit_2_AC_02()
    {
        var rig = new Rig();
        rig.Probe.Fail(File, "video.probe_timeout", "the first picture was not decoded within the time limit");

        var r = Validate(Request(), rig);

        r.IsSuccess.ShouldBeFalse();
        r.Error!.Code.ShouldBe("video.probe_timeout");
        StartExitCodes.For(r.Error).ShouldBe(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_with_the_real_probe_answers_an_empty_or_whitespace_file_as_unreadable_with_exit_2(string file)
    {
        var request = StartCommand.Parse(["play", file, "--out", "1=1"]);
        request.IsSuccess.ShouldBeTrue();
        var rig = new Rig();

        var r = StartCommand.Validate(request.Value, new DualPlayer.Media.Video.FfmpegPictureProbe(), rig.Decoder, new DeviceCatalog([DevA, DevB]), rig.Screens);

        r.IsSuccess.ShouldBeFalse();
        r.Error!.Code.ShouldBe("playback.file_unreadable");
        StartExitCodes.For(r.Error).ShouldBe(2);
    }

    [Fact]
    public void Validate_file_the_os_denies_is_reported_with_exit_4()
    {
        var rig = new Rig();
        rig.Probe.Fail(File, "playback.file_access_denied", "the operating system denied access to the file");

        var r = Validate(Request(), rig);

        r.Error!.Code.ShouldBe("playback.file_access_denied");
        StartExitCodes.For(r.Error).ShouldBe(4);
    }

    [Fact]
    public void Validate_unknown_screen_lists_connected_screens_with_numbers_and_resolutions()
    {
        var r = Validate(Request(screen: 3), new Rig());

        r.Error!.Code.ShouldBe("start.screen_not_found");
        r.Error.Message.ShouldBe("screen 3 was not found. Connected screens: 1 (1920x1080, main), 2 (3840x2160)");
        StartExitCodes.For(r.Error).ShouldBe(2);
    }

    [Fact]
    public void Validate_picture_error_comes_before_track_device_and_screen_errors()
    {
        var rig = new Rig();
        rig.Probe.Fail(File, "video.no_picture", "the file has no picture");
        var request = new StartRequest(File, [new OutputAssignment(9, DeviceRef.ByNumber(9))], 7);

        Validate(request, rig).Error!.Code.ShouldBe("video.no_picture");
    }

    [Fact]
    public void Validate_track_error_comes_before_device_and_screen_errors()
    {
        var request = new StartRequest(File, [new OutputAssignment(9, DeviceRef.ByNumber(9))], 7);

        Validate(request, new Rig()).Error!.Code.ShouldBe("playback.track_not_found");
    }

    [Fact]
    public void Validate_device_error_comes_before_screen_error()
    {
        var request = new StartRequest(File, [new OutputAssignment(1, DeviceRef.ByNumber(9))], 7);

        Validate(request, new Rig()).Error!.Code.ShouldBe("outputs.device_not_found");
    }

    [Fact]
    public void Validate_same_device_on_two_outputs_is_refused()
    {
        var request = new StartRequest(File, [new OutputAssignment(1, DeviceRef.ByNumber(1)), new OutputAssignment(2, DeviceRef.ById("id-a"))], null);

        Validate(request, new Rig()).Error!.Code.ShouldBe("outputs.device_assigned_twice");
    }

    [Fact]
    public void Error_line_is_error_code_colon_message()
    {
        StartExitCodes.FormatError(new Error("start.screen_not_found", "screen 3 was not found"))
            .ShouldBe("error start.screen_not_found: screen 3 was not found");
    }

    private static StartRequest Request(int? screen = null) =>
        new(File, [new OutputAssignment(1, DeviceRef.ByNumber(1)), new OutputAssignment(2, DeviceRef.ByNumber(2))], screen);

    [Fact]
    public void A_missing_or_wrong_video_renderer_library_is_refused_before_any_window_opens()
    {
        var rig = new Rig();

        var r = StartCommand.Validate(
            Request(),
            rig.Probe,
            rig.Decoder,
            new DeviceCatalog([DevA, DevB]),
            rig.Screens,
            () => Result.Failure("mpv.not_found", "libmpv-2.dll was not found beside the executable."));

        r.IsSuccess.ShouldBeFalse();
        r.Error.Code.ShouldBe("mpv.not_found");
        StartExitCodes.For(r.Error).ShouldBe(2);
    }

    [Fact]
    public void A_present_video_renderer_library_lets_the_start_through()
    {
        var rig = new Rig();

        var r = StartCommand.Validate(Request(), rig.Probe, rig.Decoder, new DeviceCatalog([DevA, DevB]), rig.Screens, Result.Success);

        r.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_device_that_refuses_to_open_is_reported_as_access_denied_with_exit_4_before_any_window_opens()
    {
        var rig = new Rig();
        var access = Substitute.For<IDeviceAccessProbe>();
        access.CheckAccess("id-a").Returns(Result.Success());
        access.CheckAccess("id-b").Returns(Result.Failure("outputs.device_access_denied", "Windows refused access to device Headset."));

        var r = StartCommand.Validate(Request(), rig.Probe, rig.Decoder, new DeviceCatalog([DevA, DevB]), rig.Screens, null, access);

        r.IsSuccess.ShouldBeFalse();
        r.Error.Code.ShouldBe("outputs.device_access_denied");
        StartExitCodes.For(r.Error).ShouldBe(4);
        StartExitCodes.FormatError(r.Error).ShouldBe("error outputs.device_access_denied: Windows refused access to device Headset.");
    }

    [Fact]
    public void Devices_that_open_let_the_start_through_and_are_each_tried_once()
    {
        var rig = new Rig();
        var access = Substitute.For<IDeviceAccessProbe>();
        access.CheckAccess(Arg.Any<string>()).Returns(Result.Success());

        var r = StartCommand.Validate(Request(), rig.Probe, rig.Decoder, new DeviceCatalog([DevA, DevB]), rig.Screens, null, access);

        r.IsSuccess.ShouldBeTrue();
        access.Received(1).CheckAccess("id-a");
        access.Received(1).CheckAccess("id-b");
    }

    [Fact]
    public void Validate_screen_error_comes_before_a_failing_renderer_library()
    {
        var rig = new Rig();

        var r = StartCommand.Validate(
            Request(screen: 9),
            rig.Probe,
            rig.Decoder,
            new DeviceCatalog([DevA, DevB]),
            rig.Screens,
            () => Result.Failure("mpv.not_found", "libmpv-2.dll was not found beside the executable."));

        r.Error!.Code.ShouldBe("start.screen_not_found");
    }

    [Fact]
    public void Validate_device_access_refusal_comes_before_a_missing_screen()
    {
        var rig = new Rig();
        var access = Substitute.For<IDeviceAccessProbe>();
        access.CheckAccess(Arg.Any<string>()).Returns(Result.Failure("outputs.device_access_denied", "Windows refused access to device Headset."));

        var r = StartCommand.Validate(Request(screen: 9), rig.Probe, rig.Decoder, new DeviceCatalog([DevA, DevB]), rig.Screens, null, access);

        r.Error!.Code.ShouldBe("outputs.device_access_denied");
        StartExitCodes.For(r.Error).ShouldBe(4);
    }

    private static Result<ValidatedStart> Validate(StartRequest request, Rig rig) =>
        StartCommand.Validate(request, rig.Probe, rig.Decoder, new DeviceCatalog([DevA, DevB]), rig.Screens);

    private sealed class Rig
    {
        public ScriptedProbe Probe { get; } = new();

        public TrackDecoder Decoder { get; } = new();

        public FakeScreens Screens { get; } = new();
    }

    private sealed class ScriptedProbe : IPictureProbe
    {
        private readonly Dictionary<string, Error> _failures = [];

        public void Fail(string path, string code, string message) => _failures[path] = new Error(code, message);

        public Result<PictureInfo> Probe(string path, TimeSpan timeLimit) =>
            _failures.TryGetValue(path, out var e)
                ? Result<PictureInfo>.Failure(e)
                : Result<PictureInfo>.Success(new PictureInfo("h264", "High", 1920, 1080));
    }

    private sealed class TrackDecoder : IAudioDecoder
    {
        public Result<IReadOnlyList<AudioTrackInfo>> ListTracks(string path) =>
            Result<IReadOnlyList<AudioTrackInfo>>.Success(
                [new AudioTrackInfo(1, "en", ChannelLayout.Stereo, 48000), new AudioTrackInfo(2, "uk", ChannelLayout.Stereo, 48000)]);

        public Result<IDecodeSession> Open(string path, IReadOnlyList<DecodeRequest> outputs) =>
            throw new InvalidOperationException("validation must not open a decode session");
    }

    private sealed class FakeScreens : IScreenCatalog
    {
        public IReadOnlyList<ScreenInfo> Screens { get; } = [new(1, 1920, 1080, true), new(2, 3840, 2160, false)];
    }
}
