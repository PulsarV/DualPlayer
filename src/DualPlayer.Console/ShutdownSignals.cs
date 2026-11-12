using System.Runtime.InteropServices;
using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Playback;

namespace DualPlayer.Console;

/// <summary>Ctrl+C and console-window close behave like <c>stop</c>, ending the run as interrupt (AC-13).</summary>
internal sealed class ShutdownSignals(IAudioEngine engine, Action? afterStop = null) : IDisposable
{
    private readonly List<IDisposable> _registrations = [];
    private readonly Lock _gate = new();
    private ConsoleCancelEventHandler? _cancelHandler;
    private bool _deferred;
    private bool _pending;
    private bool _interrupted;

    /// <summary>Holds interrupts that arrive before the run exists; <see cref="RunStarted"/> applies them.</summary>
    public void DeferUntilRunStarts()
    {
        lock (_gate)
        {
            _deferred = true;
        }
    }

    /// <summary>The run now exists: an interrupt held while it was starting stops it now.</summary>
    public void RunStarted()
    {
        lock (_gate)
        {
            _deferred = false;
            if (!_pending)
            {
                return;
            }
        }

        StopOnce();
    }

    /// <summary>Stops the run once; later calls while stopping are ignored.</summary>
    public void Interrupt()
    {
        lock (_gate)
        {
            if (_deferred)
            {
                _pending = true;
                return;
            }
        }

        StopOnce();
    }

    public void Register()
    {
        _cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            Interrupt();
        };
        System.Console.CancelKeyPress += _cancelHandler;
        _registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal));
        _registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGHUP, OnSignal));
    }

    public void Dispose()
    {
        if (_cancelHandler is not null)
        {
            System.Console.CancelKeyPress -= _cancelHandler;
        }

        foreach (var r in _registrations)
        {
            r.Dispose();
        }
    }

    private void StopOnce()
    {
        lock (_gate)
        {
            if (_interrupted)
            {
                return;
            }

            _interrupted = true;
        }

        engine.Stop(RunEndReason.Interrupt);
        afterStop?.Invoke();
    }

    private void OnSignal(PosixSignalContext context)
    {
        context.Cancel = true;
        Interrupt();
    }
}
