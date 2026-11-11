using System.Globalization;
using DualPlayer.Core;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;

namespace DualPlayer.Console.Commands;

/// <summary>A parsed console command.</summary>
internal abstract record CommandRequest;

internal sealed record DevicesRequest : CommandRequest;

internal sealed record TracksRequest(string File) : CommandRequest;

internal sealed record PlayRequest(string File, IReadOnlyList<OutputAssignment> Outputs, EngineOptions Options) : CommandRequest;

/// <summary>Parses command-line arguments; every malformed input is a <c>cli.usage</c> error.</summary>
internal static class CommandLine
{
    private const string UsageCode = "cli.usage";

    public static Result<CommandRequest> Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return Usage("a command is required: devices, tracks <file> or play <file> --out <track>=<device>");
        }

        return args[0] switch
        {
            "devices" when args.Count == 1 => Result<CommandRequest>.Success(new DevicesRequest()),
            "devices" => Usage("devices takes no arguments"),
            "tracks" when args.Count == 2 => Result<CommandRequest>.Success(new TracksRequest(args[1])),
            "tracks" => Usage("usage: tracks <file>"),
            "play" => ParsePlay(args),
            _ => Usage($"unknown command '{args[0]}'"),
        };
    }

    private static Result<CommandRequest> ParsePlay(IReadOnlyList<string> args)
    {
        if (args.Count < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
        {
            return Usage("usage: play <file> --out <track>=<device> [--ring-ms n] [--start-level-ms n] [--device-buffer-ms n]");
        }

        var outputs = new List<OutputAssignment>();
        int ringMs = 2000, startLevelMs = 500, deviceBufferMs = 40;

        for (var i = 2; i < args.Count; i += 2)
        {
            var option = args[i];
            if (i + 1 >= args.Count)
            {
                return Usage($"option {option} needs a value");
            }

            var value = args[i + 1];
            switch (option)
            {
                case "--out":
                    var assignment = ParseOut(value);
                    if (assignment is null)
                    {
                        return Usage($"--out expects <track>=<device>, got '{value}'");
                    }

                    outputs.Add(assignment);
                    break;
                case "--ring-ms":
                    if (!TryMs(value, out ringMs))
                    {
                        return Usage($"--ring-ms expects a positive integer, got '{value}'");
                    }

                    break;
                case "--start-level-ms":
                    if (!TryMs(value, out startLevelMs))
                    {
                        return Usage($"--start-level-ms expects a positive integer, got '{value}'");
                    }

                    break;
                case "--device-buffer-ms":
                    if (!TryMs(value, out deviceBufferMs))
                    {
                        return Usage($"--device-buffer-ms expects a positive integer, got '{value}'");
                    }

                    break;
                default:
                    return Usage($"unknown option '{option}'");
            }
        }

        if (outputs.Count == 0)
        {
            return Usage("at least one --out <track>=<device> is required");
        }

        if (startLevelMs > ringMs)
        {
            return Usage("--start-level-ms must not exceed --ring-ms");
        }

        var options = new EngineOptions
        {
            RingBuffer = TimeSpan.FromMilliseconds(ringMs),
            StartLevel = TimeSpan.FromMilliseconds(startLevelMs),
            DeviceBuffer = TimeSpan.FromMilliseconds(deviceBufferMs),
        };
        return Result<CommandRequest>.Success(new PlayRequest(args[1], outputs, options));
    }

    private static OutputAssignment? ParseOut(string value)
    {
        var split = value.IndexOf('=', StringComparison.Ordinal);
        if (split <= 0 || split == value.Length - 1)
        {
            return null;
        }

        if (!int.TryParse(value.AsSpan(0, split), NumberStyles.None, CultureInfo.InvariantCulture, out var track) || track < 1)
        {
            return null;
        }

        var device = value[(split + 1)..];
        var deviceRef = int.TryParse(device, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? DeviceRef.ByNumber(number)
            : DeviceRef.ById(device);
        return new OutputAssignment(track, deviceRef);
    }

    private static bool TryMs(string value, out int ms) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ms) && ms > 0;

    private static Result<CommandRequest> Usage(string message) => Result<CommandRequest>.Failure(UsageCode, message);
}
