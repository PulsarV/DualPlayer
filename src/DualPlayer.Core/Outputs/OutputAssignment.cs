namespace DualPlayer.Core.Outputs;

/// <summary>A device named either by its list number or by its full OS endpoint id.</summary>
public readonly record struct DeviceRef
{
    private DeviceRef(int? number, string? id)
    {
        Number = number;
        Id = id;
    }

    public int? Number { get; }

    public string? Id { get; }

    public static DeviceRef ByNumber(int number) => new(number, null);

    public static DeviceRef ById(string id) => new(null, id);

    public override string ToString() => Number is { } n ? $"device {n}" : $"device '{Id}'";
}

/// <summary>One requested output: an audio track (numbered from 1) played on a device.</summary>
public sealed record OutputAssignment(int TrackNumber, DeviceRef Device);

/// <summary>A validated output: the track and the full OS id the device resolved to.</summary>
public sealed record ResolvedOutput(int TrackNumber, string DeviceId);
