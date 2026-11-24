using System.Reflection;
using DualPlayer.Core;
using DualPlayer.Media.Video;

namespace DualPlayer.Media.Tests.Video;

/// <summary>
/// What the renderer makes of mpv's end-file event (AC-06b): an error after the file loaded is a picture failure
/// (<c>video.decode_failed</c>), an error while still loading is a load failure that releases the waiting <c>Load</c>
/// and raises nothing, and a plain end of file is <c>EndOfFile</c>. The handler runs on mpv's event thread and needs no
/// running mpv, so it is driven here through the renderer's own state. Only the error text needs the library's
/// <c>mpv_error_string</c>, hence the skip when libmpv is not installed.
/// </summary>
public sealed class MpvEndFileTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int EndReasonEof = 0;
    private const int EndReasonError = 4;
    private const int ErrorLoadingFailed = -13;

    [Fact]
    public void An_error_end_file_after_the_file_loaded_raises_one_decode_failed_and_no_end_of_file_AC_06b()
    {
        RequireLibmpv();
        using var renderer = new MpvVideoRenderer(TimeProvider.System);
        var events = Watch(renderer);
        Arrange(renderer, loaded: true, loading: false);

        EndFile(renderer, EndReasonError, ErrorLoadingFailed);

        events.Failures.Select(e => e.Code).ShouldBe(["video.decode_failed"]);
        events.EndOfFiles.ShouldBe(0);
        LoadError(renderer).ShouldBeNull();
    }

    [Fact]
    public void An_error_end_file_while_the_file_is_still_loading_sets_the_load_error_releases_the_load_wait_and_raises_no_event_AC_06b()
    {
        RequireLibmpv();
        using var renderer = new MpvVideoRenderer(TimeProvider.System);
        var events = Watch(renderer);
        Arrange(renderer, loaded: false, loading: true);

        EndFile(renderer, EndReasonError, ErrorLoadingFailed);

        events.Failures.ShouldBeEmpty();
        events.EndOfFiles.ShouldBe(0);
        LoadError(renderer)!.Code.ShouldBe("mpv.load_failed");
        LoadDone(renderer).IsSet.ShouldBeTrue();
    }

    [Fact]
    public void An_end_of_file_end_file_raises_end_of_file_and_no_decode_failed_AC_06b()
    {
        using var renderer = new MpvVideoRenderer(TimeProvider.System);
        var events = Watch(renderer);
        Arrange(renderer, loaded: true, loading: false);

        EndFile(renderer, EndReasonEof, 0);

        events.EndOfFiles.ShouldBe(1);
        events.Failures.ShouldBeEmpty();
        LoadError(renderer).ShouldBeNull();
    }

    [Theory]
    [InlineData(EndReasonError)]
    [InlineData(EndReasonEof)]
    public void An_end_file_of_a_file_that_is_no_longer_current_raises_nothing_AC_06b(int reason)
    {
        using var renderer = new MpvVideoRenderer(TimeProvider.System);
        var events = Watch(renderer);
        // Still loading: past the guard an error would build its text with mpv_error_string and set the load error, so the
        // guard is pinned without libmpv (the call would throw, or set _loadError, instead of returning).
        Arrange(renderer, loaded: false, loading: true);
        Set(renderer, "_currentLoad", 2);

        EndFile(renderer, reason, ErrorLoadingFailed);

        events.Failures.ShouldBeEmpty();
        events.EndOfFiles.ShouldBe(0);
        LoadError(renderer).ShouldBeNull();
        LoadDone(renderer).IsSet.ShouldBeFalse();
    }

    private static void RequireLibmpv() =>
        Assert.SkipUnless(MpvLibrary.Load().IsSuccess, "libmpv is not installed (brew install mpv / build/fetch-libmpv.ps1).");

    // The state OnEndFile reads: the file number the event thread is on is the current load.
    private static void Arrange(MpvVideoRenderer renderer, bool loaded, bool loading)
    {
        Set(renderer, "_fileSequence", 1);
        Set(renderer, "_currentLoad", 1);
        Set(renderer, "_fileLoaded", loaded);
        Set(renderer, "_loading", loading);
        Set(renderer, "_loadingFile", "clip.mkv");
    }

    private static Events Watch(MpvVideoRenderer renderer)
    {
        var events = new Events();
        renderer.DecodeFailed += events.Failures.Add;
        renderer.EndOfFile += () => events.EndOfFiles++;
        return events;
    }

    private static void EndFile(MpvVideoRenderer renderer, int reason, int error) =>
        typeof(MpvVideoRenderer).GetMethod("OnEndFile", Private)!.Invoke(renderer, [reason, error]);

    private static Error? LoadError(MpvVideoRenderer renderer) => (Error?)Field(renderer, "_loadError").GetValue(renderer);

    private static ManualResetEventSlim LoadDone(MpvVideoRenderer renderer) => (ManualResetEventSlim)Field(renderer, "_loadDone").GetValue(renderer)!;

    private static void Set(MpvVideoRenderer renderer, string name, object value) => Field(renderer, name).SetValue(renderer, value);

    private static FieldInfo Field(MpvVideoRenderer renderer, string name) =>
        renderer.GetType().GetField(name, Private) ?? throw new InvalidOperationException($"MpvVideoRenderer has no field {name}.");

    private sealed class Events
    {
        public List<Error> Failures { get; } = [];

        public int EndOfFiles { get; set; }
    }
}
