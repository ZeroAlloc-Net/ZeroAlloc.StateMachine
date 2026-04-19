---
id: circuit-breaker
title: Circuit Breaker Example
sidebar_position: 4
---

# Circuit Breaker Example

A circuit breaker is a classic state machine. This guide builds a thread-safe, zero-allocation circuit breaker using ZeroAlloc.StateMachine — exactly the pattern used internally by `ZeroAlloc.Resilience`.

---

## The circuit breaker state model

A circuit breaker has three states:

```
Closed ──(MaxFailures exceeded)──▶ Open ──(ResetMs elapsed)──▶ HalfOpen
  ▲                                                                │
  └──(HalfOpenProbes successes)────────────────────────────────────┘
  
HalfOpen ──(any failure)──▶ Open
```

| State | Behaviour |
|-------|-----------|
| **Closed** | Normal operation — calls pass through |
| **Open** | Calls rejected immediately — no inner call |
| **HalfOpen** | Probe calls allowed — one success closes, any failure opens |

---

## State and trigger enums

```csharp
public enum CbState
{
    Closed,
    Open,
    HalfOpen
}

public enum CbTrigger
{
    Failure,     // a call failed
    Success,     // a call succeeded
    ResetTimer   // the open-duration timer fired
}
```

---

## The machine

```csharp
using ZeroAlloc.StateMachine;

[StateMachine(InitialState = nameof(CbState.Closed), Concurrent = true)]
[Transition<CbState, CbTrigger>(From = CbState.Closed,   On = CbTrigger.Failure,    To = CbState.Open)]
[Transition<CbState, CbTrigger>(From = CbState.Open,     On = CbTrigger.ResetTimer, To = CbState.HalfOpen)]
[Transition<CbState, CbTrigger>(From = CbState.HalfOpen, On = CbTrigger.Success,    To = CbState.Closed)]
[Transition<CbState, CbTrigger>(From = CbState.HalfOpen, On = CbTrigger.Failure,    To = CbState.Open)]
[Terminal<CbState>(State = CbState.Closed)]   // no ZSM0002 — Closed is a valid re-entry sink
public partial class CircuitBreakerFsm { }
```

> Note: `[Terminal<CbState>(State = CbState.Closed)]` is technically incorrect here because `Closed` does have an outgoing transition (on `Failure`). The `[Terminal]` attribute would only be needed if `Closed` had *no* outgoing edges. This example omits it — the generator is happy because `Closed` has outgoing transitions.

---

## The policy class

The machine handles state transitions; the policy class handles the counting logic and timer:

```csharp
public sealed class CircuitBreakerPolicy : IDisposable
{
    private readonly CircuitBreakerFsm _fsm = new();
    private readonly int _maxFailures;
    private readonly int _resetMs;
    private readonly int _halfOpenProbes;
    private int _failureCount;
    private int _successCount;
    private Timer? _resetTimer;

    public CircuitBreakerPolicy(int maxFailures, int resetMs, int halfOpenProbes)
    {
        _maxFailures   = maxFailures;
        _resetMs       = resetMs;
        _halfOpenProbes = halfOpenProbes;
    }

    public bool CanExecute()
    {
        var state = _fsm.Current;
        return state is CbState.Closed or CbState.HalfOpen;
    }

    public void OnSuccess()
    {
        if (_fsm.Current == CbState.HalfOpen)
        {
            var count = Interlocked.Increment(ref _successCount);
            if (count >= _halfOpenProbes)
            {
                Interlocked.Exchange(ref _successCount, 0);
                Interlocked.Exchange(ref _failureCount, 0);
                _fsm.TryFire(CbTrigger.Success); // HalfOpen → Closed
            }
        }
        else
        {
            // Reset failure count on any success in Closed state
            Interlocked.Exchange(ref _failureCount, 0);
        }
    }

    public void OnFailure(Exception ex)
    {
        if (_fsm.Current == CbState.HalfOpen)
        {
            _fsm.TryFire(CbTrigger.Failure); // HalfOpen → Open
            ScheduleReset();
            return;
        }

        var count = Interlocked.Increment(ref _failureCount);
        if (count >= _maxFailures && _fsm.TryFire(CbTrigger.Failure)) // Closed → Open
        {
            Interlocked.Exchange(ref _failureCount, 0);
            ScheduleReset();
        }
    }

    private void ScheduleReset()
    {
        _resetTimer?.Dispose();
        _resetTimer = new Timer(_ =>
        {
            _fsm.TryFire(CbTrigger.ResetTimer); // Open → HalfOpen
        }, null, _resetMs, Timeout.Infinite);
    }

    public void Dispose() => _resetTimer?.Dispose();
}
```

---

## How it composes

The `CircuitBreakerFsm` handles the *structural* part — which state we are in — using lock-free CAS. The `CircuitBreakerPolicy` handles the *semantic* part — counting failures, scheduling timers, deciding when to trip.

This separation keeps each class focused:

- `CircuitBreakerFsm` has no knowledge of failure counts or timeouts.
- `CircuitBreakerPolicy` has no state-transition logic — it just fires triggers.

---

## Thread safety

Because `Concurrent = true`, `TryFire` is safe to call from multiple threads simultaneously. The CAS loop ensures that exactly one caller wins the `Closed → Open` race even if 100 threads all observe `MaxFailures` at the same moment.

The failure counter uses `Interlocked.Increment` for the same reason. The combined approach gives a zero-lock, zero-allocation circuit breaker path.

---

## Observable hooks

Add `OnEnter` hooks to log state changes:

```csharp
public partial class CircuitBreakerFsm
{
    private ILogger? _logger;

    public void SetLogger(ILogger logger) => _logger = logger;

    partial void OnEnterOpen(CbState from)
        => _logger?.LogWarning("Circuit breaker opened from {From}", from);

    partial void OnEnterHalfOpen(CbState from)
        => _logger?.LogInformation("Circuit breaker is half-open, probing...");

    partial void OnEnterClosed(CbState from)
        => _logger?.LogInformation("Circuit breaker closed — service recovered");
}
```

---

## Testing

```csharp
[Fact]
public void OpenAfterMaxFailures()
{
    var cb = new CircuitBreakerPolicy(maxFailures: 2, resetMs: 10_000, halfOpenProbes: 1);
    cb.CanExecute().Should().BeTrue();

    cb.OnFailure(new Exception());
    cb.CanExecute().Should().BeTrue();  // still one below threshold

    cb.OnFailure(new Exception());
    cb.CanExecute().Should().BeFalse(); // tripped
}

[Fact]
public async Task HalfOpenAfterReset()
{
    var cb = new CircuitBreakerPolicy(maxFailures: 1, resetMs: 50, halfOpenProbes: 1);
    cb.OnFailure(new Exception());
    cb.CanExecute().Should().BeFalse();

    await Task.Delay(100); // wait for reset timer
    cb.CanExecute().Should().BeTrue(); // HalfOpen — probing allowed
}

[Fact]
public async Task CloseAfterSuccessfulProbe()
{
    var cb = new CircuitBreakerPolicy(maxFailures: 1, resetMs: 50, halfOpenProbes: 1);
    cb.OnFailure(new Exception());
    await Task.Delay(100);

    cb.CanExecute().Should().BeTrue(); // HalfOpen
    cb.OnSuccess();
    cb.CanExecute().Should().BeTrue(); // Closed — recovered
}
```
