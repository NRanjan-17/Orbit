namespace Orbit.Core.Switching;

/// <summary>One way of asking Windows to (re)connect or release the headphones' audio profiles.</summary>
public interface IAudioConnector
{
    string Name { get; }

    /// <summary>Asks Windows to connect. Returning true means the request was accepted, NOT that it connected.</summary>
    Task<bool> RequestConnectAsync(CancellationToken ct);

    /// <summary>Asks Windows to disconnect (release the headphones so another device can take them).</summary>
    Task<bool> RequestDisconnectAsync(CancellationToken ct);
}

/// <summary>Reports whether the headphones currently have a Bluetooth connection to this PC.</summary>
public interface IConnectionState
{
    bool IsConnected { get; }
}

public enum SwitchOutcome
{
    Connected,
    Released,
    AlreadyConnected,
    AlreadyReleased,
    CoolingDown,

    /// <summary>
    /// Windows accepted the request(s) but no connection came up. Most likely another device (e.g. a Mac)
    /// holds the headphones, or they are asleep/out of range. Captures show no advertised
    /// "connected elsewhere" state, so Orbit cannot tell which (docs/protocol-findings.md F4).
    /// </summary>
    Failed,

    Busy,
}

public sealed record SwitchResult(SwitchOutcome Outcome, int Attempts, string Message);

/// <summary>
/// Connect/release with verification, retry with exponential backoff, and a cooldown after failures
/// so the global hotkey or auto-connect cannot hammer the Bluetooth stack.
/// </summary>
public sealed class SwitchCoordinator(IAudioConnector connector, IConnectionState state, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _coolDownUntil = DateTimeOffset.MinValue;

    /// <summary>Delays before attempts 2, 3, ... (attempt 1 is immediate).</summary>
    public TimeSpan[] Backoff { get; init; } = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)];

    /// <summary>How long to wait for the connection to appear after each request.</summary>
    public TimeSpan VerifyTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public TimeSpan VerifyPollInterval { get; init; } = TimeSpan.FromMilliseconds(150);

    public TimeSpan FailureCooldown { get; init; } = TimeSpan.FromSeconds(30);

    public bool IsCoolingDown => _time.GetUtcNow() < _coolDownUntil;

    public async Task<SwitchResult> ConnectAsync(CancellationToken ct = default)
    {
        if (state.IsConnected)
        {
            return new(SwitchOutcome.AlreadyConnected, 0, "Already connected to this PC.");
        }

        if (IsCoolingDown)
        {
            return new(SwitchOutcome.CoolingDown, 0, $"Waiting before trying again ({(_coolDownUntil - _time.GetUtcNow()).TotalSeconds:0} s).");
        }

        if (!await _gate.WaitAsync(0, ct))
        {
            return new(SwitchOutcome.Busy, 0, "A switch is already in progress.");
        }

        try
        {
            var attempts = 0;
            for (var i = 0; i <= Backoff.Length; i++)
            {
                if (i > 0)
                {
                    await Task.Delay(Backoff[i - 1], _time, ct);
                }

                attempts++;
                await connector.RequestConnectAsync(ct);
                if (await WaitForAsync(connected: true, ct))
                {
                    return new(SwitchOutcome.Connected, attempts, "Connected to this PC.");
                }
            }

            _coolDownUntil = _time.GetUtcNow() + FailureCooldown;
            return new(SwitchOutcome.Failed, attempts,
                "Couldn't connect. The headphones are probably in use by another device (such as your Mac), asleep, or out of range. " +
                "Release them on the other device, or wake them, and try again.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SwitchResult> ReleaseAsync(CancellationToken ct = default)
    {
        if (!state.IsConnected)
        {
            return new(SwitchOutcome.AlreadyReleased, 0, "Not connected to this PC.");
        }

        if (!await _gate.WaitAsync(0, ct))
        {
            return new(SwitchOutcome.Busy, 0, "A switch is already in progress.");
        }

        try
        {
            var attempts = 0;
            for (var i = 0; i < 2; i++)
            {
                attempts++;
                await connector.RequestDisconnectAsync(ct);
                if (await WaitForAsync(connected: false, ct))
                {
                    return new(SwitchOutcome.Released, attempts, "Released. Your other device can connect now.");
                }
            }

            return new(SwitchOutcome.Failed, attempts, "Windows did not release the headphones.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> WaitForAsync(bool connected, CancellationToken ct)
    {
        var deadline = _time.GetUtcNow() + VerifyTimeout;
        while (_time.GetUtcNow() < deadline)
        {
            if (state.IsConnected == connected)
            {
                return true;
            }

            await Task.Delay(VerifyPollInterval, _time, ct);
        }

        return state.IsConnected == connected;
    }
}
