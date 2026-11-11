using System.Runtime.Versioning;
using DualPlayer.Core.Outputs;
using DualPlayer.Platform.Windows.Audio;
using DualPlayer.Tests;

namespace DualPlayer.Platform.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class WasapiRenderTests
{
    private const string LossDeviceVariable = "DUALPLAYER_LOSS_DEVICE";
    private static readonly TimeSpan LossWindow = TimeSpan.FromSeconds(60);

    [Theory]
    [InlineData(0L, 10_000_000L, 0L)]
    [InlineData(10_000_000L, 10_000_000L, 10_000_000L)] // 1 s at a 10 MHz timestamp base
    [InlineData(10_000_000L, 1_000_000_000L, 1_000_000_000L)] // 1 s at a 1 GHz base
    [InlineData(5_000_000L, 1_000_000_000L, 500_000_000L)] // 0.5 s
    public void Qpc_reading_in_100ns_units_converts_to_the_time_provider_timestamp_base(
        long qpc100Ns, long frequency, long expected) =>
        QpcTimeBase.ToTimestamp(qpc100Ns, frequency).ShouldBe(expected);

    [Fact]
    public void Qpc_conversion_does_not_overflow_for_a_machine_that_has_been_up_for_months()
    {
        const long ninetyDays100Ns = 90L * 24 * 3600 * 10_000_000;
        QpcTimeBase.ToTimestamp(ninetyDays100Ns, 1_000_000_000L).ShouldBe(90L * 24 * 3600 * 1_000_000_000L);
    }

    [Fact]
    public void Qpc_conversion_is_monotonic()
    {
        var a = QpcTimeBase.ToTimestamp(123_456_789L, 10_000_000L);
        var b = QpcTimeBase.ToTimestamp(123_456_790L, 10_000_000L);
        b.ShouldBeGreaterThanOrEqualTo(a);
    }

    [HardwareFact]
    public void Ten_seconds_of_silence_render_with_monotonic_device_positions_on_the_time_provider_base()
    {
        var backend = new WasapiAudioBackend();
        var info = backend.ListDevices().First();
        var source = new RecordingSource();

        var opened = backend.Open(info.Id, TimeSpan.FromMilliseconds(50), source);
        opened.IsSuccess.ShouldBeTrue();
        var device = opened.Value!;
        var before = TimeProvider.System.GetTimestamp();
        device.Start();
        Thread.Sleep(TimeSpan.FromSeconds(10));
        device.Close();
        var after = TimeProvider.System.GetTimestamp();

        var positions = source.Positions.ToArray();
        positions.Length.ShouldBeGreaterThan(100);
        for (var i = 1; i < positions.Length; i++)
        {
            positions[i].FramesPlayed.ShouldBeGreaterThanOrEqualTo(positions[i - 1].FramesPlayed);
            positions[i].Timestamp.ShouldBeGreaterThanOrEqualTo(positions[i - 1].Timestamp);
        }

        positions[^1].Timestamp.ShouldBeInRange(before, after);
    }

    /// <summary>
    /// Operator-driven DoD check for AC-06 (loss raised when the device is disabled). Run on the target PC with
    /// <c>DUALPLAYER_HARDWARE=1</c> and <c>DUALPLAYER_LOSS_DEVICE=&lt;endpoint id&gt;</c> (ids are listed by
    /// <c>ListDevices</c>); within <see cref="LossWindow"/> of the test starting, disable that endpoint
    /// (Sound settings → device → Disable) or unplug / switch it off. Skipped when the variable is not set.
    /// </summary>
    [HardwareFact]
    public async Task Disabling_a_rendering_device_raises_device_lost_exactly_once()
    {
        var deviceId = Environment.GetEnvironmentVariable(LossDeviceVariable);
        Assert.SkipWhen(
            string.IsNullOrEmpty(deviceId),
            $"Needs an operator to disable a device; set {LossDeviceVariable} to its endpoint id to run.");

        var opened = new WasapiAudioBackend().Open(deviceId!, TimeSpan.FromMilliseconds(50), new RecordingSource());
        opened.IsSuccess.ShouldBeTrue(opened.Error?.Message);
        var device = opened.Value!;
        var raised = 0;
        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        device.DeviceLost += (_, _) =>
        {
            Interlocked.Increment(ref raised);
            lost.TrySetResult();
        };

        try
        {
            device.Start();
            TestContext.Current.SendDiagnosticMessage(
                $"Disable or unplug device {deviceId} within {LossWindow.TotalSeconds} s.");

            await lost.Task.WaitAsync(LossWindow, TestContext.Current.CancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

            Volatile.Read(ref raised).ShouldBe(1);
        }
        finally
        {
            device.Close();
        }
    }

    [HardwareFact]
    public async Task Starting_twice_or_after_close_neither_throws_nor_raises_device_lost()
    {
        var backend = new WasapiAudioBackend();
        var info = backend.ListDevices().First();
        var opened = backend.Open(info.Id, TimeSpan.FromMilliseconds(50), new RecordingSource());
        opened.IsSuccess.ShouldBeTrue(opened.Error?.Message);
        var device = opened.Value!;
        var raised = 0;
        device.DeviceLost += (_, _) => Interlocked.Increment(ref raised);

        device.Start();
        Should.NotThrow(device.Start);
        device.Close();
        Should.NotThrow(device.Start);
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        Volatile.Read(ref raised).ShouldBe(0);
    }

    private sealed class RecordingSource : IRenderSource
    {
        public System.Collections.Concurrent.ConcurrentQueue<DevicePosition> Positions { get; } = new();

        public void Render(Span<float> buffer, DevicePosition position)
        {
            buffer.Clear();
            Positions.Enqueue(position);
        }
    }
}
