---
id: timeout-transitions
title: Timeout Transitions
sidebar_position: 5
---

# Timeout Transitions

A **timeout transition** auto-fires after a fixed number of milliseconds in
its source state. Use it to model retry timers, idle shutoffs, or watchdog
edges without wiring your own `Timer` plumbing.

---

## Why

Plenty of state machines have time-bounded states: a circuit breaker that
re-probes after 30 seconds, a connection retry that backs off after 5
seconds, a "thinking" state that gives up after 10 seconds. Without
declarative support, every such edge becomes a `Timer` field, an
`OnEnter` arm, and a matching disarm in every other `OnExit`. Easy to get
wrong, easy to leak.

`AfterMs` collapses all of that into the existing transition attribute.

---

## How

A timeout edge is an ordinary `[Transition]` with `AfterMs` set:

```csharp
using ZeroAlloc.StateMachine;

public enum WatchState   { Idle, Armed, Tripped }
public enum WatchTrigger { Arm, Tick, Reset, Timeout }

[StateMachine(InitialState = nameof(WatchState.Idle), Concurrent = true)]
[Transition<WatchState, WatchTrigger>(From = WatchState.Idle,    On = WatchTrigger.Arm,     To = WatchState.Armed)]
[Transition<WatchState, WatchTrigger>(From = WatchState.Armed,   On = WatchTrigger.Tick,    To = WatchState.Armed)]
[Transition<WatchState, WatchTrigger>(From = WatchState.Armed,   On = WatchTrigger.Reset,   To = WatchState.Idle)]
[Transition<WatchState, WatchTrigger>(From = WatchState.Armed,   On = WatchTrigger.Timeout, To = WatchState.Tripped, AfterMs = 5000)]
public partial class Watchdog { }
```

The last edge auto-fires `Timeout` after 5 seconds in `Armed`. Any other
trigger that leaves `Armed` (here `Tick` keeps it armed; `Reset` leaves
for `Idle`) cleanly disarms the pending timer.

Usage:

```csharp
using var w = new Watchdog();
w.TryFire(WatchTrigger.Arm);    // Idle → Armed; timer armed for 5000ms
// ... if no Reset within 5s ...
// Timer callback fires WatchTrigger.Timeout; Armed → Tripped.
```

---

## Constraints

- `AfterMs` requires `Concurrent = true` on the enclosing `[StateMachine]`,
  or that the edge lives inside a `[StateMachinePart]` (which is always
  concurrent). Otherwise the generator emits [ZSM0012](../diagnostics/ZSM0012.md).
- `AfterMs` must be strictly positive. `AfterMs = 0` or negative emits
  [ZSM0013](../diagnostics/ZSM0013.md).
- Timers are **one-shot per arm**. Each entry into the source state arms
  the timer; each exit disarms it. There is no interval / repeat mode.

---

## Generator emit

For every transition with `AfterMs > 0`, the generator emits:

1. **A lazy timer field** — one `System.Threading.Timer?` per `(Part, From, On)`
   tuple. Allocated on first arm, reused via `Timer.Change(...)` thereafter.
2. **Arm-on-enter** — when the dispatcher writes the source state, it either
   constructs the timer (first time) or re-arms the existing one to
   `AfterMs` milliseconds.
3. **Disarm-on-exit** — when a transition leaves the source state, it calls
   `Timer.Change(Timeout.Infinite, Timeout.Infinite)` on the corresponding
   field. The timer object is reused, not disposed.
4. **Race-safe callback** — the timer callback calls the same public
   `TryFire` that user code calls, so the CAS loop handles any
   user-vs-timer interleaving. If state has already moved by callback time,
   the `(Current, trigger)` switch falls through and the callback is a no-op.
5. **`IDisposable`** — any class with at least one timed edge implements
   `IDisposable`. `Dispose()` disposes every timer field and calls
   `GC.SuppressFinalize(this)`.

---

## Dispose contract

A class with timed edges holds live `Timer` instances. To prevent them
from outliving the machine, dispose the instance:

```csharp
using var w = new Watchdog();
// ... use w ...
// Dispose() runs at end of scope; in-flight callbacks see disposed timers
// and the CAS loop falls through harmlessly.
```

The generator **owns** `Dispose` on any class with at least one timed
edge: it unconditionally emits `public void Dispose()`. There is no
`partial void` hook or other extension point — any user-declared
`Dispose` on the same class either trips
[ZSM0019](../diagnostics/ZSM0019.md) (wrong signature) or `CS0111` (exact
signature collision). If you need extra cleanup, expose it as a separate
method (e.g. `Shutdown()`) and call it explicitly; don't try to route it
through `Dispose`.

There is **no finalizer** — timers are managed resources and a missed
`Dispose` will leak them until the next AppDomain teardown. The
recommended pattern is `using` or owning the machine inside a longer-lived
disposable.

---

## Related

- [Transitions](transitions.md) — the underlying `TryFire` switch model
- [Concurrent Mode](concurrent-mode.md) — required by `AfterMs`
- [Concurrent Parts](concurrent-parts.md) — multi-machine classes with their own timed edges
- [Attribute Reference — `AfterMs`](../attributes.md#transition-afterms)
- [ZSM0012](../diagnostics/ZSM0012.md), [ZSM0013](../diagnostics/ZSM0013.md), [ZSM0019](../diagnostics/ZSM0019.md)
