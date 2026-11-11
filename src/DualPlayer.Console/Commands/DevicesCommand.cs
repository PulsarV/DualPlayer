using DualPlayer.Core.Outputs;

namespace DualPlayer.Console.Commands;

/// <summary>Lists the playback devices, sorted by OS endpoint id so numbers are stable.</summary>
internal sealed class DevicesCommand(IAudioBackend backend)
{
    public int Run(TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        output.WriteLine("#  name  type  id");
        var devices = backend.ListDevices().OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
        for (var i = 0; i < devices.Count; i++)
        {
            output.WriteLine($"{i + 1}  {devices[i].Name}  {devices[i].Type ?? "unknown"}  {devices[i].Id}");
        }

        return 0;
    }
}
