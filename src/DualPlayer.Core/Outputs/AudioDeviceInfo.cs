namespace DualPlayer.Core.Outputs;

/// <summary>A playback device the OS offers: display name, device type when known, OS endpoint id.</summary>
public sealed record AudioDeviceInfo(string Name, string? Type, string Id);
