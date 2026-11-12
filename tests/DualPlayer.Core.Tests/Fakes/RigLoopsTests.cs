using DualPlayer.Core.Outputs;
using Microsoft.Extensions.Time.Testing;

namespace DualPlayer.Core.Tests.Fakes;

public sealed class RigLoopsTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    private sealed class Silent : IRenderSource
    {
        public void Render(Span<float> buffer, DevicePosition position) => buffer.Clear();
    }

    private static FakeAudioDevice StartedDevice(FakeTimeProvider time)
    {
        var device = new FakeAudioDevice(time, new MixFormat(48_000, 2, ChannelLayout.Stereo), TimeSpan.FromMilliseconds(100), new Silent(), 0);
        device.Start();
        return device;
    }

    [Fact]
    public async Task Filling_a_device_that_was_lost_returns_instead_of_spinning()
    {
        var time = new FakeTimeProvider();
        var device = StartedDevice(time);
        device.SimulateLoss();
        time.Advance(TimeSpan.FromMilliseconds(10));

        var fill = Task.Run(() => RigLoops.FillQueue(device, 960, 480), TestContext.Current.CancellationToken);

        (await RigLoops.CompletesWithin(fill, Bound)).ShouldBeTrue("FillQueue spun on a lost device");
    }

    [Fact]
    public async Task Filling_a_device_that_was_closed_returns_instead_of_spinning()
    {
        var time = new FakeTimeProvider();
        var device = StartedDevice(time);
        device.Close();
        time.Advance(TimeSpan.FromMilliseconds(10));

        var fill = Task.Run(() => RigLoops.FillQueue(device, 960, 480), TestContext.Current.CancellationToken);

        (await RigLoops.CompletesWithin(fill, Bound)).ShouldBeTrue("FillQueue spun on a closed device");
    }

    [Fact]
    public void Filling_an_open_device_queues_the_requested_depth()
    {
        var device = StartedDevice(new FakeTimeProvider());

        RigLoops.FillQueue(device, 960, 480);

        device.WrittenFrames.ShouldBe(960);
    }

    [Fact]
    public void Waiting_for_something_that_never_happens_fails_the_test()
    {
        Should.Throw<ShouldAssertException>(() => RigLoops.WaitOrFail(() => false, TimeSpan.FromMilliseconds(50), "a thing"));
    }

    [Fact]
    public void Waiting_for_something_that_holds_returns()
    {
        RigLoops.WaitOrFail(() => true, TimeSpan.FromMilliseconds(50), "a thing");
    }
}
