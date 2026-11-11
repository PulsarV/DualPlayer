using DualPlayer.Console.Rendering;
using DualPlayer.Core.Playback;

namespace DualPlayer.Console.Commands;

/// <summary>Lists the audio tracks of a file; nothing plays.</summary>
internal sealed class TracksCommand(IAudioDecoder decoder)
{
    public int Run(TracksRequest request, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var tracks = decoder.ListTracks(request.File);
        if (!tracks.IsSuccess)
        {
            return ExitCodes.Report(tracks.Error, error);
        }

        output.WriteLine("#  language  layout  rate");
        foreach (var track in tracks.Value)
        {
            output.WriteLine($"{track.Number}  {track.Language}  {ConsoleFormat.Layout(track.ChannelLayout)}  {track.SampleRate}");
        }

        return 0;
    }
}
