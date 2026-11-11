using DualPlayer.Console.Rendering;
using DualPlayer.Core.Playback;

namespace DualPlayer.Console.Commands;

/// <summary>Starts a run and prints each output with the device id it resolved to.</summary>
internal sealed class PlayCommand(IAudioEngine engine)
{
    public int Run(PlayRequest request, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var started = engine.StartRun(request.File, request.Outputs, request.Options);
        if (!started.IsSuccess)
        {
            return ExitCodes.Report(started.Error, error);
        }

        var index = 1;
        foreach (var o in started.Value.Outputs)
        {
            output.WriteLine(
                $"output {index++}  track {o.TrackNumber} (-> {ConsoleFormat.Layout(o.Format.ChannelLayout)} {o.Format.SampleRate})  device {o.DeviceName}  id {o.DeviceId}");
        }

        output.WriteLine($"started   clock {ConsoleFormat.Clock(started.Value.ClockPosition)}");
        return 0;
    }
}
