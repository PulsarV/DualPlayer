namespace DualPlayer.Core.Outputs;

/// <summary>Frames the device has played and the reading's timestamp on the <see cref="TimeProvider"/> base.</summary>
public readonly record struct DevicePosition(long FramesPlayed, long Timestamp);
