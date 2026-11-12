using DualPlayer.Core.Playback;

namespace DualPlayer.Core.Outputs;

/// <summary>Turns requested track=device assignments into resolved outputs, or the first refusal.</summary>
public static class OutputAssignmentValidator
{
    public static Result<IReadOnlyList<ResolvedOutput>> Validate(
        IReadOnlyList<AudioTrackInfo> tracks,
        DeviceCatalog catalog,
        IReadOnlyList<OutputAssignment> assignments)
    {
        var resolved = new List<ResolvedOutput>(assignments.Count);
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assignment in assignments)
        {
            if (!tracks.Any(t => t.Number == assignment.TrackNumber))
            {
                return Result<IReadOnlyList<ResolvedOutput>>.Failure(
                    "playback.track_not_found",
                    $"track {assignment.TrackNumber} does not exist, {DescribeTracks(tracks)}");
            }

            var device = catalog.Find(assignment.Device);
            if (device is null)
            {
                return Result<IReadOnlyList<ResolvedOutput>>.Failure(
                    "outputs.device_not_found",
                    $"{assignment.Device} is not a connected playback device");
            }

            if (!used.Add(device.Id))
            {
                return Result<IReadOnlyList<ResolvedOutput>>.Failure(
                    "outputs.device_assigned_twice",
                    $"device '{device.Id}' is assigned to two outputs; one audio output per device");
            }

            resolved.Add(new ResolvedOutput(assignment.TrackNumber, device.Id));
        }

        return Result<IReadOnlyList<ResolvedOutput>>.Success(resolved);
    }

    private static string DescribeTracks(IReadOnlyList<AudioTrackInfo> tracks)
    {
        var numbers = tracks.Select(t => t.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        return numbers.Length switch
        {
            0 => "the file has no audio tracks",
            1 => $"the file has track {numbers[0]}",
            _ => $"the file has tracks {string.Join(", ", numbers[..^1])} and {numbers[^1]}",
        };
    }
}
