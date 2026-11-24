using DualPlayer.App.Startup;
using DualPlayer.Core;
using DualPlayer.Core.Playback;
using NSubstitute;

namespace DualPlayer.App.Tests.Startup;

public sealed class WindowBoundRendererTests
{
    private const nint SurfaceHandle = 0x1234;

    private readonly IVideoRenderer _inner = Substitute.For<IVideoRenderer>();
    private readonly WindowBoundRenderer _renderer;

    public WindowBoundRendererTests()
    {
        _renderer = new WindowBoundRenderer(_inner, new VideoSurface { Handle = SurfaceHandle });
    }

    [Fact]
    public void A_load_with_no_handle_draws_into_the_surface_window_AC_01()
    {
        _renderer.Load("movie.mkv", 0, false);

        _inner.Received(1).Load("movie.mkv", SurfaceHandle, false, Arg.Any<TimeSpan?>());
    }

    [Fact]
    public void A_load_with_a_handle_keeps_that_handle_AC_01()
    {
        _renderer.Load("movie.mkv", 0x77, false);

        _inner.Received(1).Load("movie.mkv", 0x77, false, Arg.Any<TimeSpan?>());
    }

    [Fact]
    public void A_load_passes_the_file_pause_flag_and_timeout_through_unchanged_AC_06b()
    {
        _renderer.Load("clip.mp4", 0, true, TimeSpan.FromSeconds(3));

        _inner.Received(1).Load("clip.mp4", SurfaceHandle, true, TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void A_load_without_a_timeout_leaves_the_renderers_own_default_AC_06b()
    {
        _renderer.Load("clip.mp4", 0, false);

        _inner.Received(1).Load("clip.mp4", SurfaceHandle, false, null);
    }

    [Fact]
    public void A_load_returns_the_inner_result_AC_06b()
    {
        var failure = Result.Failure("mpv.load_timeout", "timed out");
        _inner.Load(Arg.Any<string>(), Arg.Any<nint>(), Arg.Any<bool>(), Arg.Any<TimeSpan?>()).Returns(failure);

        _renderer.Load("clip.mp4", 0, false).ShouldBe(failure);
    }

    [Fact]
    public void A_decode_failure_reaches_a_subscribed_handler_with_the_same_error_AC_06b()
    {
        var error = new Error("mpv.decode_failed", "picture damaged");
        var received = new List<Error>();
        _renderer.DecodeFailed += received.Add;

        _inner.DecodeFailed += Raise.Event<Action<Error>>(error);

        received.Count.ShouldBe(1);
        received[0].ShouldBeSameAs(error);
    }

    [Fact]
    public void An_unsubscribed_decode_failure_handler_no_longer_hears_it_AC_06b()
    {
        var received = new List<Error>();
        Action<Error> handler = received.Add;
        _renderer.DecodeFailed += handler;
        _renderer.DecodeFailed -= handler;

        _inner.DecodeFailed += Raise.Event<Action<Error>>(new Error("mpv.decode_failed", "picture damaged"));

        received.ShouldBeEmpty();
    }

    [Fact]
    public void The_end_of_file_reaches_a_subscribed_handler_and_not_an_unsubscribed_one_AC_06b()
    {
        var heard = 0;
        var dropped = 0;
        Action keep = () => heard++;
        Action drop = () => dropped++;
        _renderer.EndOfFile += keep;
        _renderer.EndOfFile += drop;
        _renderer.EndOfFile -= drop;

        _inner.EndOfFile += Raise.Event<Action>();

        heard.ShouldBe(1);
        dropped.ShouldBe(0);
    }

    [Fact]
    public void The_hardware_decoding_notice_reaches_a_subscribed_handler_and_not_an_unsubscribed_one_AC_06b()
    {
        var heard = 0;
        var dropped = 0;
        Action keep = () => heard++;
        Action drop = () => dropped++;
        _renderer.HardwareDecodingInactive += keep;
        _renderer.HardwareDecodingInactive += drop;
        _renderer.HardwareDecodingInactive -= drop;

        _inner.HardwareDecodingInactive += Raise.Event<Action>();

        heard.ShouldBe(1);
        dropped.ShouldBe(0);
    }
}
