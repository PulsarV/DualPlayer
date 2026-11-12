using System.Runtime.Versioning;
using DualPlayer.Platform.Windows.Audio;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace DualPlayer.Platform.Windows.Tests;

/// <summary>F23: a failed MMCSS registration is logged with the Win32 error instead of being ignored.</summary>
[SupportedOSPlatform("windows")]
public sealed class MmcssLogTests
{
    [Fact]
    public void A_zero_handle_logs_a_warning_with_the_win32_error()
    {
        var logger = Substitute.For<ILogger>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        MmcssLog.WarnIfNotRaised(IntPtr.Zero, 1552, logger, "Speakers");

        logger.ReceivedWithAnyArgs(1).Log(LogLevel.Warning, default, (object)null!, null, null!);
        var call = logger.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(ILogger.Log));
        call.GetArguments()[0].ShouldBe(LogLevel.Warning);
        call.GetArguments()[2]!.ToString()!.ShouldContain("1552");
    }

    [Fact]
    public void A_valid_handle_logs_nothing()
    {
        var logger = Substitute.For<ILogger>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        MmcssLog.WarnIfNotRaised(new IntPtr(42), 0, logger, "Speakers");

        logger.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)).ShouldBe(0);
    }
}
