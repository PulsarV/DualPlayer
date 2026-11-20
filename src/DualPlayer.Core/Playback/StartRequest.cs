using DualPlayer.Core.Outputs;

namespace DualPlayer.Core.Playback;

/// <summary>A start command: the file, each output's track and device, and the screen (null = main screen).</summary>
public sealed record StartRequest(string File, IReadOnlyList<OutputAssignment> Outputs, int? Screen = null);
