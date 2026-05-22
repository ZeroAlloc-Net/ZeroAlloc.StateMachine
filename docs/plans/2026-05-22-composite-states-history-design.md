# Composite states + shallow history (backlog B1 + B2)

Date: 2026-05-22
Status: Approved
Backlog: graduates `ZeroAlloc.StateMachine` items B1 + B2

## Problem

v1 of `ZeroAlloc.StateMachine` models a flat finite-state machine: one state
enum, one trigger enum, a stack of `[Transition<TState, TTrigger>]`
attributes declaring the edges. Real-world FSMs frequently want to group
related states under a composite — e.g. a `Loading` state that contains
its own `Fetching` → `Parsing` → `Validating` sub-flow. Two backlog items
together cover the natural unit of work:

- **B1: Hierarchical / nested states.** A composite state owns a sub-FSM
  that handles triggers while the parent is in that composite. Sub-FSM has
  its own state enum but shares the parent's trigger enum.
- **B2: Shallow history.** When a composite is re-entered, optionally
  restore the sub-FSM to the leaf state it was in when last exited.

B2 is meaningless without B1; they ship as a unit.

## Goals

- One canonical model for composite states + history that fits within v1's
  zero-allocation, AOT-friendly, generator-driven contract.
- Arbitrary nesting depth — a sub-FSM may itself contain composites.
- Shallow history only (per backlog text and YAGNI: deep history is
  speculative until a real consumer needs cross-level restore).
- Sequential mode only — composite states are diagnosed as incompatible
  with `[StateMachine(Concurrent = true)]`.

## Decisions

Locked during brainstorming. Each pinned via Q&A in the session.

| Question | Decision |
|---|---|
| Composite model | Separate `[StateMachine]` partial sub-class referenced via `SubMachine = typeof(...)` |
| Trigger enum | Sub-FSM shares the parent's `TTrigger` |
| Dispatch order | Sub-FSM first (UML default — innermost active state wins) |
| Sub-FSM lifecycle | Eager construction in parent ctor; `Reset()` / `ResetTo(TState)` mechanics |
| Nesting depth | Arbitrary; shallow history per level |
| Concurrent + composite | Diagnose at compile time (`ZSM0005`) — refuse to generate |

## Design

### New public attributes

```csharp
namespace ZeroAlloc.StateMachine;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public sealed class CompositeStateAttribute<TState> : Attribute where TState : struct, Enum
{
    public required TState State { get; init; }
    public required Type SubMachine { get; init; }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public sealed class HistoryStateAttribute<TState> : Attribute where TState : struct, Enum
{
    public required TState State { get; init; }
}
```

`SubMachine` must point at a `[StateMachine]` partial class whose
`TTrigger` matches the parent's `TTrigger`. `[HistoryState(State = X)]`
must accompany a `[CompositeState(State = X)]` on the same class —
diagnostics enforce both.

### New generator-emitted methods (uniform across all `[StateMachine]` classes)

```csharp
internal void Reset();                  // sets _state = InitialState
internal void ResetTo(TState state);    // sets _state = state
```

Emitted on every `[StateMachine]` class so any class can be a sub-FSM.
`internal` so only same-assembly generated code (the parent's composite
emit path) can reach them. Neither fires `OnExit`/`OnEnter` — they are
state-population mechanics, not transitions.

For classes that themselves contain composites, `ResetTo(state)` ALSO
resets any sub-FSM bound to a composite that matches the target state:

```csharp
internal void ResetTo({st} state)
{
    _state = state;
    switch (state)
    {
        case {st}.StateA: _subFsm_StateA.Reset(); break;  // shallow history contract
        case {st}.StateB: _subFsm_StateB.Reset(); break;
        default: break;
    }
}
```

This guarantees the shallow-history contract: each level remembers its
own direct sub-state only; nested sub-FSMs always start at their initial
state when the upper level restores history.

### Parent-class additions

For a parent declared with `[CompositeState<TState>(State = X, SubMachine = typeof(InnerFsm))]`:

```csharp
private readonly InnerFsm _subFsm_X = new();              // one per composite
private InnerFsm.TState _history_X;                       // only if [HistoryState(State = X)]
private bool _hasHistory_X;                               // only if [HistoryState(State = X)]
```

Field-name suffix is the composite state name; safe across multiple
composites on the same parent.

### `TryFire` becomes two-step

```csharp
public bool TryFire({tr} trigger)
{
    if (TryFireSubMachine(trigger)) return true;
    return (Current, trigger) switch
    {
        // existing parent-level transitions unchanged
        _ => false
    };
}

private bool TryFireSubMachine({tr} trigger) => _state switch
{
    {st}.X => _subFsm_X.TryFire(trigger),
    {st}.Y => _subFsm_Y.TryFire(trigger),
    _ => false
};
```

When the parent isn't in any composite, `TryFireSubMachine` falls to `_ => false` and the outer `TryFire` proceeds straight to the parent's table — zero overhead beyond one enum compare.

### `Fire` (entry/exit) extended

```csharp
private bool Fire({st} from, {st} to, {tr} trigger)
{
    OnExit(from, trigger);

    // Capture history before forgetting sub-state.
    if (from == {st}.X /* has [HistoryState] */)
    {
        _history_X = _subFsm_X.Current;
        _hasHistory_X = true;
    }

    _state = to;

    // Position sub-FSM on entering a composite.
    if (to == {st}.X)
    {
        if (_hasHistory_X) _subFsm_X.ResetTo(_history_X);
        else               _subFsm_X.Reset();
    }

    OnEnter(to, from);
    return true;
}
```

The `if` chains unroll per-composite-state. Code is sized to the number
of composites + history pairs, not to the cross-product.

### Dispatch scenarios (informative)

**Sub handles trigger.** Parent in composite; sub's switch returns true;
parent's `_state` unchanged; parent's `OnExit/OnEnter` do NOT fire.

**Sub rejects, parent handles.** Parent's transition table fires;
history captured at `OnExit` boundary; parent transitions to a new
state, possibly another composite (in which case that composite's
sub-FSM is reset or restored).

**Nested.** `parent.TryFire` → `_subFsm.TryFire` → its `_subFsm.TryFire`
all the way down. Each level captures its own history independently.
Shallow contract holds because `ResetTo` recursively `Reset`s inner
sub-FSMs.

## Diagnostics (sequential after `ZSM0004`)

| ID | Severity | Condition |
|---|---|---|
| `ZSM0005` | Error | `[CompositeState]` declared on a `[StateMachine(Concurrent = true)]` class. |
| `ZSM0006` | Error | `[CompositeState]`'s `SubMachine` type is not a `[StateMachine]` partial class. |
| `ZSM0007` | Error | `[CompositeState]`'s `SubMachine` declares a different `TTrigger` than the parent. |
| `ZSM0008` | Error | `[CompositeState]`'s `State` value doesn't match any state declared in the parent's `TState`. |
| `ZSM0009` | Error | Two `[CompositeState]` attributes target the same `State` value (duplicate). |
| `ZSM0010` | Error | `[HistoryState(State = X)]` declared without matching `[CompositeState(State = X)]` on the same class. |
| `ZSM0011` | Error | `[CompositeState]` and `[Terminal]` both target the same state. |

All `Error` severity — these are all "the FSM declaration is broken;
refuse to generate" conditions.

## Files touched

- NEW: `src/ZeroAlloc.StateMachine/CompositeStateAttribute.cs`
- NEW: `src/ZeroAlloc.StateMachine/HistoryStateAttribute.cs`
- MOD: `src/ZeroAlloc.StateMachine/PublicAPI.Unshipped.txt` (add the two new public types + members)
- MOD: `src/ZeroAlloc.StateMachine.Generator/StateMachineModel.cs` (extend with composite + history metadata)
- MOD: `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs` (collect new attributes; diagnostic dispatch)
- MOD: `src/ZeroAlloc.StateMachine.Generator/StateMachineWriter.cs` (emit sub-FSM fields, `Reset`/`ResetTo`, modified `TryFire`, history)
- MOD: `src/ZeroAlloc.StateMachine.Generator/StateMachineDiagnostics.cs` (add `ZSM0005`–`ZSM0011`)
- NEW: `tests/ZeroAlloc.StateMachine.Tests/CompositeStateTests.cs` (runtime scenarios)
- NEW: `tests/ZeroAlloc.StateMachine.Generator.Tests/CompositeStateGeneratorTests.cs` (diagnostics + snapshots)
- NEW: `docs/core-concepts/composite-states.md`
- MOD: `docs/attributes.md` (entries for the two new attributes)
- NEW or MOD: `docs/diagnostics/...` for each new `ZSMnnnn` (follow existing diagnostics-doc convention)

## Testing

`CompositeStateTests.cs` covers the runtime behaviour scenarios from
brainstorming Section 3:

- Sub-FSM handles trigger when parent in composite.
- Sub-FSM rejects → parent falls through.
- History captured on exit, restored on re-enter.
- No history on first enter → starts at sub's initial.
- Nested composite three levels deep dispatches correctly.
- Shallow history resets inner sub-FSMs to initial.
- `Reset()` does not fire `OnExit`/`OnEnter`.
- `ResetTo()` does not fire `OnExit`/`OnEnter`.
- Parent `OnExit`/`OnEnter` fire only on parent-level transitions.

`CompositeStateGeneratorTests.cs` covers each new diagnostic with a
positive (declaration triggers expected `ZSM00nn`) and matching
snapshot tests under `Snapshots/` for the new emit shapes:

- `Compose_Basic_Snapshot.verified.cs` (parent + one sub, no history)
- `Compose_WithHistory_Snapshot.verified.cs` (parent + one sub + history)
- `Compose_Nested_Snapshot.verified.cs` (parent + sub-as-parent + leaf)
- `Compose_MultipleComposites_Snapshot.verified.cs` (parent with two
  composite states each pointing at a different sub-FSM)

## Out of scope

- Deep history (recursive restore of nested sub-FSM state).
- Composite states with `[StateMachine(Concurrent = true)]` — diagnosed
  away via `ZSM0005`.
- Composite-specific entry/exit hooks (e.g. `OnEnterCompositeX` vs
  `OnEnterStateX`). The existing partials are sufficient at each level.
- Public API for snapshotting/restoring an entire hierarchy's state
  (consumer-driven persistence) — fields stay private; revisit if a
  consumer asks.
- Generator-emitted Mermaid output for hierarchical machines — that's
  backlog B4 (diagram export), independent and may be paired in a
  future release.

## Backward compatibility

Strictly additive:
- Two new public attribute types — no signature changes to existing
  attributes.
- Two new `internal` methods (`Reset` / `ResetTo`) emitted on EVERY
  `[StateMachine]` class even when no composite is in play. Existing
  consumers don't see them (internal) and gain nothing they can break.
- `TryFire`'s two-step body is identical to v1 when no composites are
  declared (the new `TryFireSubMachine` falls through immediately and
  the original switch fires).
- No changes to `PublicAPI.Shipped.txt`; everything new lands in
  `PublicAPI.Unshipped.txt`.

No SemVer break. Lands as a `feat:` commit, minor bump (e.g. 1.1.2 →
1.2.0 if release-please follows conventional-commits).
