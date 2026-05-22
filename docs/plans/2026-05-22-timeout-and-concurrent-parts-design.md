# Timeout transitions + concurrent state parts (backlog B3 + B5)

Date: 2026-05-22
Status: Approved
Backlog: graduates `ZeroAlloc.StateMachine` items B3 + B5

## Problem

v1.3 of `ZeroAlloc.StateMachine` ships flat + composite/history FSMs. Two
remaining backlog items address orthogonal real-world friction:

- **B3: Timeout transitions.** A transition that auto-fires after N
  milliseconds in the source state — useful for retry timers, idle
  shutoffs, watchdog edges. Today consumers have to wire their own
  `Timer` + `TryFire` plumbing on every `OnEnter`/`OnExit`.
- **B5: Per-trigger granularity for concurrent mode.** A class that
  models two or more independent state variables (e.g. an IoT device
  with an `Operational` lifecycle AND a `Connection` lifecycle, each
  evolving on different triggers). v1's `Concurrent = true` only
  covers a single state field with thread-safe CAS — it can't model
  multiple parallel state machines in the same object.

They ship as a pair because the natural way to declare a timed edge
inside a multi-part class is via the same `Part` discriminator B5
introduces, and both touch the same generator code paths
(`StateMachineWriter`, diagnostics, public API surface).

## Goals

- Declarative timeout edges that compose with existing transition
  attributes (no separate "timer" surface).
- Multiple independent concurrent state fields in one class, each with
  its own state + trigger enum, CAS-safe `TryFire<Name>`, and
  per-part `OnEnter`/`OnExit` partial hooks.
- Zero-allocation steady state — timers allocated lazily on first arm;
  no per-trigger allocations elsewhere.
- AOT-friendly, generator-driven, no reflection.

## Decisions

Locked during brainstorming. Each pinned via Q&A in the session.

| Question | Decision |
|---|---|
| Timed transitions require concurrent? | Yes — `AfterMs` only valid with `Concurrent = true` (or inside a `[StateMachinePart]`, which is always concurrent). |
| Timer field shape | One `System.Threading.Timer?` field per timed edge, allocated lazily on first arm, reused via `Timer.Change(...)`. |
| Cleanup | Generated class implements `IDisposable`; `Dispose()` disposes every timer field. |
| Part declaration | Class-level `[StateMachinePart<TState, TTrigger>(Name = "...", InitialState = ...)]`, AllowMultiple. |
| `[StateMachine]` + `[StateMachineGroup]` | Mutually exclusive. Diagnose if both declared. Parts are always concurrent. |
| Part discriminator for timed edges | Reuse the `Part = "..."` tag on `[Transition]`. Timer callback dispatches to that part's `TryFire<Name>`. |
| Composite states inside parts | Out of scope; diagnose. |

## Design

### New / extended public attributes

```csharp
namespace ZeroAlloc.StateMachine;

// EXTENDED — new optional properties
public sealed class TransitionAttribute<TState, TTrigger> : Attribute
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    // ... existing From / On / To / When ...
    public int AfterMs { get; init; }       // 0 = no timer (default)
    public string? Part { get; init; }      // null = top-level (single-machine or group-unscoped)
}

// NEW — declares a class as a group of concurrent state machines
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class StateMachineGroupAttribute : Attribute { }

// NEW — declares one machine inside a group
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class StateMachinePartAttribute<TState, TTrigger> : Attribute
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    public required string Name { get; init; }
    public required TState InitialState { get; init; }
}
```

Notes:
- `Part` is `null` when the transition belongs to a single-machine
  class (`[StateMachine]`) — preserves v1 behaviour.
- `Part` is required (non-null) when the class is `[StateMachineGroup]`
  — diagnosed via `ZSM0016` otherwise.
- `[StateMachineGroup]` is mutually exclusive with `[StateMachine]`
  (`ZSM0014`).

### Generator emit — timed edges

For every transition with `AfterMs > 0`, emit:

```csharp
private System.Threading.Timer? _timer_{Part?}_{From}_{On};
```

Field-name suffix uses the `Part` name (omitted for single-machine
classes), the `From` state name, and the `On` trigger name — safe
across multiple timed edges sharing a `From` or `On`.

Inside the part's (or class's) `Fire` method, on entering the source
state of a timed edge:

```csharp
if (to == {st}.{From})
{
    var t = _timer_{prefix}_{From}_{On};
    if (t is null)
    {
        t = new System.Threading.Timer(
            static s => ((Self){{s!}}).TryFire{Part?}({tr}.{On}),
            this,
            {AfterMs},
            System.Threading.Timeout.Infinite);
        _timer_{prefix}_{From}_{On} = t;
    }
    else
    {
        t.Change({AfterMs}, System.Threading.Timeout.Infinite);
    }
}
```

On leaving the source state (any transition out):

```csharp
if (from == {st}.{From})
{
    _timer_{prefix}_{From}_{On}?.Change(
        System.Threading.Timeout.Infinite,
        System.Threading.Timeout.Infinite);
}
```

The callback's `TryFire`/`TryFire<Part>` is itself the public CAS
loop — if state has already moved by callback time, the
`(Current, trigger)` switch falls through, no transition, no harm.
This is the race-safety contract: concurrent CAS handles user vs.
timer interleaving without per-edge locking.

### Generator emit — `IDisposable`

Every class with at least one timed transition (single-machine or
group) implements `IDisposable`:

```csharp
public void Dispose()
{
    _timer_..._A?.Dispose();
    _timer_..._B?.Dispose();
    // ... one line per timed-edge field
    System.GC.SuppressFinalize(this);
}
```

If the user already declares `IDisposable` on the partial, the
generator detects this and skips emitting the interface; the
generated `Dispose()` becomes a `partial void` they're expected to
call. Diagnostic `ZSM0019` if the user-supplied `Dispose` signature
doesn't match.

### Generator emit — concurrent state parts

For a class declared with
`[StateMachineGroup]` + `[StateMachinePart<OpState,OpTrigger>(Name="Operational", InitialState=OpState.Idle)]`:

```csharp
// Per part:
private volatile long _state_Operational = (long)OpState.Idle;

public OpState OperationalCurrent
    => (OpState)System.Threading.Volatile.Read(ref _state_Operational);

public bool TryFireOperational(OpTrigger trigger)
{
    while (true)
    {
        var current = (OpState)System.Threading.Volatile.Read(ref _state_Operational);
        var next = (current, trigger) switch
        {
            // (OpState.X, OpTrigger.Y) => OpState.Z,
            _ => current,
        };
        if (EqualityComparer<OpState>.Default.Equals(next, current)) return false;
        var prev = System.Threading.Interlocked.CompareExchange(
            ref _state_Operational, (long)next, (long)current);
        if (prev == (long)current)
        {
            OnExitOperational(current, trigger);
            OnEnterOperational(next, current);
            // armed/disarmed timer logic (per timed edge in this part)
            return true;
        }
    }
}

partial void OnEnterOperational(OpState to, OpState from);
partial void OnExitOperational(OpState from, OpTrigger trigger);
```

Each part is fully independent — no shared state field, no shared
trigger enum, no shared partial hook. The class-level partial may
declare per-part `OnEnter<Part>` / `OnExit<Part>` as needed.

### `Part` routing in dispatch

The generator buckets each `[Transition]` by its `Part` value. Within
each bucket, dispatch is the existing `(state, trigger) switch`. The
buckets never interfere; two transitions from different parts may
share `From` state or `On` trigger names without conflict because each
part has its own `TState` / `TTrigger`.

For timed edges, the callback selects the right `TryFire<Part>` —
the field name itself encodes the part, so the closure-free `static`
delegate captures the correct target via the field's setter site.

### Composites + parts (out of scope)

`[CompositeState]` is already disallowed under `Concurrent = true`
(`ZSM0005`). Since parts are always concurrent, the same rule
applies — a new diagnostic `ZSM0018` fires if `[CompositeState]`
appears on a `[StateMachineGroup]`-marked class.

## Diagnostics (sequential after `ZSM0011`)

| ID | Severity | Condition |
|---|---|---|
| `ZSM0012` | Error | `[Transition(... AfterMs = N)]` on a class without `Concurrent = true` and not inside a `[StateMachinePart]`. |
| `ZSM0013` | Error | `[Transition(... AfterMs = N)]` with `N <= 0`. |
| `ZSM0014` | Error | Class declares BOTH `[StateMachine]` and `[StateMachineGroup]`. |
| `ZSM0015` | Error | Two `[StateMachinePart]` attributes share the same `Name`. |
| `ZSM0016` | Error | `[Transition(Part = "X")]` references a `Name` not declared by any `[StateMachinePart]` on the class. Also fires if a transition in a `[StateMachineGroup]` class has `Part = null`. |
| `ZSM0017` | Error | `[StateMachineGroup]` declared with zero `[StateMachinePart]` attributes. |
| `ZSM0018` | Error | `[CompositeState]` declared on a `[StateMachineGroup]` class. |
| `ZSM0019` | Error | User-supplied `Dispose()` on the partial has an incompatible signature with the generator's emit. |

All `Error` severity — broken FSM declarations; refuse to generate.

## Files touched

- MOD: `src/ZeroAlloc.StateMachine/TransitionAttribute.cs` (add `AfterMs`, `Part`)
- NEW: `src/ZeroAlloc.StateMachine/StateMachineGroupAttribute.cs`
- NEW: `src/ZeroAlloc.StateMachine/StateMachinePartAttribute.cs`
- MOD: `src/ZeroAlloc.StateMachine/PublicAPI.Unshipped.txt`
- MOD: `src/ZeroAlloc.StateMachine.Generator/StateMachineModel.cs` (extend with timed-edge + part metadata)
- MOD: `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs` (collect new attributes; dispatch new analyzers)
- MOD: `src/ZeroAlloc.StateMachine.Generator/StateMachineWriter.cs` (emit timer fields, arm/disarm blocks, per-part dispatch, `Dispose`)
- MOD: `src/ZeroAlloc.StateMachine.Generator/StateMachineDiagnostics.cs` (add `ZSM0012`–`ZSM0019`)
- NEW: `tests/ZeroAlloc.StateMachine.Tests/TimedTransitionTests.cs`
- NEW: `tests/ZeroAlloc.StateMachine.Tests/StateMachineGroupTests.cs`
- NEW: `tests/ZeroAlloc.StateMachine.Generator.Tests/TimedTransitionGeneratorTests.cs`
- NEW: `tests/ZeroAlloc.StateMachine.Generator.Tests/StateMachineGroupGeneratorTests.cs`
- NEW: `docs/core-concepts/timeout-transitions.md`
- NEW: `docs/core-concepts/concurrent-parts.md`
- MOD: `docs/attributes.md` (entries for `AfterMs`, `Part`, `[StateMachineGroup]`, `[StateMachinePart]`)
- NEW: `docs/diagnostics/...` for each new `ZSMnnnn`

## Testing

`TimedTransitionTests.cs`:
- Timer arms on enter, fires after `AfterMs`, transitions correctly.
- User `TryFire` before timer expiry disarms cleanly.
- Timer callback after state has moved is a no-op (CAS fails harmlessly).
- Multiple timed edges in one machine don't cross-interfere.
- `Dispose()` cancels in-flight timers; no callbacks after dispose.
- Lazy allocation: timer field is `null` until first arm of that edge.

`StateMachineGroupTests.cs`:
- Two parts evolve independently under concurrent dispatch.
- Per-part `OnEnter<Part>` / `OnExit<Part>` fire correctly.
- `Current<Part>` reflects per-part state.
- Trigger collision across parts (same trigger enum value, different parts) routes correctly.
- Timed edge inside a part arms/disarms scoped to that part.

`TimedTransitionGeneratorTests.cs` + `StateMachineGroupGeneratorTests.cs`:
- One positive snapshot per emit shape:
  - `Timed_SingleEdge_Snapshot.verified.cs`
  - `Timed_MultipleEdges_Snapshot.verified.cs`
  - `Group_TwoParts_Snapshot.verified.cs`
  - `Group_TwoParts_OneTimedEdge_Snapshot.verified.cs`
- One negative test per new diagnostic (`ZSM0012`–`ZSM0019`) asserting
  the diagnostic fires AND no code is emitted.

## Out of scope

- Repeating / interval timers (every edge fires at most once per arm).
- Deep history across parts (history is already shallow-only).
- Cross-part transitions (a transition in part A cannot move part B —
  parts are by definition independent; consumer composes manually).
- `[CompositeState]` inside a `[StateMachinePart]` — diagnosed away
  via `ZSM0018`.
- Cancellation tokens on `TryFire` — consumer can race their own
  cancellation; FSM dispatch is already O(1).
- `IAsyncDisposable` — `System.Threading.Timer` doesn't need async
  disposal; revisit if a consumer asks.

## Backward compatibility

Strictly additive:
- `[Transition]` gains two optional properties (`AfterMs`, `Part`);
  default values preserve v1 behaviour.
- Two new public attribute types (`[StateMachineGroup]`,
  `[StateMachinePart<,>]`) — no existing class is affected.
- Generated `IDisposable` only appears on classes that have at least
  one timed edge. Existing consumers of single-machine classes
  without timers see zero generated-API changes.
- No changes to `PublicAPI.Shipped.txt`; everything new lands in
  `PublicAPI.Unshipped.txt`.

No SemVer break. Lands as a `feat:` commit, minor bump (1.3.x → 1.4.0).
