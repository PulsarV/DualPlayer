using System.Collections.Concurrent;
using System.Reflection;
using DualPlayer.Media.Video;

namespace DualPlayer.Media.Tests.Video;

/// <summary>
/// The reply to a refused speed change must not overwrite a newer <c>SetSpeed</c> (AC-14, AC-17). The renderer's reply
/// handler runs on mpv's event thread and needs no native mpv, so it is driven here through the renderer's own state;
/// the interleaving is made deterministic by holding the renderer's lock the way a <c>SetSpeed</c> in progress does.
/// </summary>
public sealed class MpvSpeedReplyTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void A_refused_latest_speed_puts_the_speed_back_to_the_last_one_mpv_took()
    {
        using var renderer = new MpvVideoRenderer(TimeProvider.System);
        Submit(renderer, id: 1, speed: 2.0);

        Reply(renderer, id: 1, error: -1);

        renderer.Speed.ShouldBe(1.0);
    }

    [Fact]
    public void A_refused_older_speed_leaves_a_newer_one_in_place()
    {
        using var renderer = new MpvVideoRenderer(TimeProvider.System);
        Submit(renderer, id: 1, speed: 2.0);
        Submit(renderer, id: 2, speed: 3.0);

        Reply(renderer, id: 1, error: -1);

        renderer.Speed.ShouldBe(3.0);
    }

    [Fact]
    public void A_refused_speed_reply_waits_for_a_speed_change_in_progress_and_then_leaves_the_newer_speed_in_place()
    {
        using var renderer = new MpvVideoRenderer(TimeProvider.System);
        Submit(renderer, id: 1, speed: 2.0);
        var gate = (Lock)Field(renderer, "_gate").GetValue(renderer)!;

        // A SetSpeed is in progress: it holds the lock until it has taken the next request id and written its speed.
        gate.Enter();
        var reply = new Thread(() => Reply(renderer, id: 1, error: -1));
        try
        {
            reply.Start();

            reply.Join(TimeSpan.FromMilliseconds(300)).ShouldBeFalse("the reply checked the request id and wrote the speed without the renderer's lock");

            Submit(renderer, id: 2, speed: 3.0);
        }
        finally
        {
            gate.Exit();
        }

        reply.Join(TimeSpan.FromSeconds(5)).ShouldBeTrue();
        renderer.Speed.ShouldBe(3.0);
    }

    // What SetSpeed leaves behind once its set command is submitted: the request, its id, the speed written.
    private static void Submit(MpvVideoRenderer renderer, long id, double speed)
    {
        var requests = (ConcurrentDictionary<long, double>)Field(renderer, "_speedRequests").GetValue(renderer)!;
        requests[id] = speed;
        Field(renderer, "_speedIds").SetValue(renderer, id);
        Field(renderer, "_setSpeed").SetValue(renderer, speed);
    }

    private static void Reply(MpvVideoRenderer renderer, long id, int error) =>
        typeof(MpvVideoRenderer).GetMethod("OnSpeedReply", Private)!.Invoke(renderer, [id, error, 0L]);

    private static FieldInfo Field(MpvVideoRenderer renderer, string name) =>
        renderer.GetType().GetField(name, Private) ?? throw new InvalidOperationException($"MpvVideoRenderer has no field {name}.");
}
