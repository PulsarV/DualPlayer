using System.Globalization;
using DualPlayer.Core;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;

namespace DualPlayer.App.Startup;

internal sealed record StartRequest(string File, IReadOnlyList<OutputAssignment> Outputs, int? Screen);

internal sealed record ValidatedStart(string File, PictureInfo Picture, IReadOnlyList<ResolvedOutput> Outputs, ScreenInfo Screen);

internal static class StartCommand
{
    private const int MaxOutputs = 3;
    private static readonly TimeSpan StartBudget = TimeSpan.FromSeconds(3);

    /// <summary>Parses <c>play &lt;file&gt; --out track=device [--out ...] [--screen n]</c>.</summary>
    public static Result<StartRequest> Parse(string[] args)
    {
        if (args.Length < 2 || args[0] != "play" || args[1].StartsWith("--", StringComparison.Ordinal))
        {
            return Usage("usage: play <file> --out <track>=<device> [--out ...] [--screen <n>]");
        }

        var outputs = new List<OutputAssignment>();
        int? screen = null;
        for (var i = 2; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length)
            {
                return Usage($"option {args[i]} needs a value");
            }

            var value = args[i + 1];
            switch (args[i])
            {
                case "--out":
                    var assignment = ParseOutput(value);
                    if (assignment is null)
                    {
                        return Usage($"--out expects <track>=<device>, got '{value}'");
                    }

                    outputs.Add(assignment);
                    break;
                case "--screen":
                    if (!TryPositive(value, out var n))
                    {
                        return Usage($"--screen expects a positive number, got '{value}'");
                    }

                    screen = n;
                    break;
                default:
                    return Usage($"unknown option '{args[i]}'");
            }
        }

        if (outputs.Count == 0)
        {
            return Usage("at least one --out <track>=<device> is required");
        }

        return outputs.Count > MaxOutputs
            ? Result<StartRequest>.Failure("outputs.too_many", "at most three --out options are allowed")
            : Result<StartRequest>.Success(new StartRequest(args[1], outputs, screen));
    }

    /// <summary>Checks in contract order: file, picture, tracks, devices, screen. First failure wins.</summary>
    public static Result<ValidatedStart> Validate(
        StartRequest request,
        IPictureProbe probe,
        IAudioDecoder decoder,
        DeviceCatalog devices,
        IScreenCatalog screens)
    {
        var picture = probe.Probe(request.File, StartBudget);
        if (!picture.IsSuccess)
        {
            return Result<ValidatedStart>.Failure(picture.Error);
        }

        var tracks = decoder.ListTracks(request.File);
        if (!tracks.IsSuccess)
        {
            return Result<ValidatedStart>.Failure(tracks.Error);
        }

        var resolved = OutputAssignmentValidator.Validate(tracks.Value, devices, request.Outputs);
        if (!resolved.IsSuccess)
        {
            return Result<ValidatedStart>.Failure(resolved.Error);
        }

        var list = screens.Screens;
        var screen = request.Screen is { } n
            ? list.FirstOrDefault(s => s.Number == n)
            : list.FirstOrDefault(s => s.IsMain) ?? list.FirstOrDefault();
        if (screen is null)
        {
            var wanted = request.Screen is { } w ? $"screen {w}" : "the main screen";
            var described = string.Join(", ", list.Select(Describe));
            return Result<ValidatedStart>.Failure(
                "start.screen_not_found",
                $"{wanted} was not found. Connected screens: {described}");
        }

        return Result<ValidatedStart>.Success(new ValidatedStart(request.File, picture.Value, resolved.Value, screen));
    }

    private static string Describe(ScreenInfo s) =>
        string.Create(CultureInfo.InvariantCulture, $"{s.Number} ({s.Width}x{s.Height}{(s.IsMain ? ", main" : string.Empty)})");

    private static Result<StartRequest> Usage(string message) => Result<StartRequest>.Failure("cli.usage", message);

    private static bool TryPositive(string text, out int n) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out n) && n > 0;

    private static OutputAssignment? ParseOutput(string value)
    {
        var eq = value.IndexOf('=', StringComparison.Ordinal);
        if (eq <= 0 || eq == value.Length - 1 || !TryPositive(value[..eq], out var track))
        {
            return null;
        }

        var device = value[(eq + 1)..];
        return new OutputAssignment(track, TryPositive(device, out var number) ? DeviceRef.ByNumber(number) : DeviceRef.ById(device));
    }
}
