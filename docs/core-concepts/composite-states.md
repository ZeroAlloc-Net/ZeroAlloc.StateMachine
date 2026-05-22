---
id: composite-states
title: Composite States
sidebar_position: 4
---

# Composite States

A **composite state** owns a sub-FSM. While the parent machine is in that state, triggers
are dispatched to the sub-machine first — the parent's own transitions only fire if the
sub-machine does not consume the trigger.

This is the standard UML statechart *hierarchical state* model. Use it when you have a
group of related sub-states that all live inside one logical parent state.

---

## Quick example

A document loader has a `Loading` phase that internally cycles through `Fetching` and
`Parsing`. From the outside, callers only care about `Idle`, `Loading`, `Ready`, and
`Failed`; the `Fetching`/`Parsing` split is an implementation detail.

```csharp
using ZeroAlloc.StateMachine;

public enum DocState  { Idle, Loading, Ready, Failed }
public enum LoadStep  { Fetching, Parsing }
public enum DocTrig   { Begin, Chunk, Parsed, Fail, Reset }

// Sub-machine — same TTrigger as the parent (DocTrig), independent TState (LoadStep).
[StateMachine(InitialState = nameof(LoadStep.Fetching))]
[Transition<LoadStep, DocTrig>(From = LoadStep.Fetching, On = DocTrig.Chunk,  To = LoadStep.Fetching)]
[Transition<LoadStep, DocTrig>(From = LoadStep.Fetching, On = DocTrig.Parsed, To = LoadStep.Parsing)]
[Terminal<LoadStep>(State = LoadStep.Parsing)]
public partial class LoadingFsm { }

// Parent — Loading is a composite that delegates to LoadingFsm.
[StateMachine(InitialState = nameof(DocState.Idle))]
[Transition<DocState, DocTrig>(From = DocState.Idle,    On = DocTrig.Begin,  To = DocState.Loading)]
[Transition<DocState, DocTrig>(From = DocState.Loading, On = DocTrig.Parsed, To = DocState.Ready)]
[Transition<DocState, DocTrig>(From = DocState.Loading, On = DocTrig.Fail,   To = DocState.Failed)]
[Transition<DocState, DocTrig>(From = DocState.Failed,  On = DocTrig.Reset,  To = DocState.Idle)]
[Terminal<DocState>(State = DocState.Ready)]
[CompositeState<DocState>(State = DocState.Loading, SubMachine = typeof(LoadingFsm))]
public partial class DocMachine { }
```

Usage:

```csharp
var doc = new DocMachine();
doc.TryFire(DocTrig.Begin);   // Idle → Loading (sub-FSM resets to Fetching)
doc.TryFire(DocTrig.Chunk);   // sub-FSM consumes; parent stays in Loading
doc.TryFire(DocTrig.Chunk);   // ditto
doc.TryFire(DocTrig.Parsed);  // sub-FSM: Fetching → Parsing (consumed); parent stays in Loading
doc.TryFire(DocTrig.Parsed);  // sub-FSM has no arm; parent: Loading → Ready
```

Note the two `Parsed` firings: the first is consumed by the sub-machine's
`Fetching → Parsing` arm, the second falls through to the parent's
`Loading → Ready` arm. The trigger value is the same; the dispatcher routes it based
on whether the active sub-FSM has a matching transition.

---

## Declaring a composite

```csharp
[CompositeState<TState>(State = X, SubMachine = typeof(InnerFsm))]
```

- `TState` — the **parent's** state enum.
- `State` — the parent state that owns the sub-machine.
- `SubMachine` — the `Type` of the sub-machine class.

**Contract for the sub-machine** (enforced by the generator):

| Requirement | Violation |
|---|---|
| `SubMachine` is a `partial class` with `[StateMachine]` | ZSM0006 |
| `SubMachine`'s `TTrigger` matches the parent's `TTrigger` | ZSM0007 |
| `State` is a declared member of the parent's `TState` enum | ZSM0008 |
| Each parent state is declared composite at most once | ZSM0009 |
| The same state is not also `[Terminal]` | ZSM0011 |
| The parent is not `[StateMachine(Concurrent = true)]` | ZSM0005 |

The sub-machine's own `TState` is independent — it can be any enum type, named however
makes sense at that level of the hierarchy.

The sub-FSM is held as an instance field of the parent and constructed lazily on first
entry to the composite state. There is no boxing or reflection on the trigger path.

---

## Dispatch order

When the parent is in a composite state, `TryFire(trigger)` runs two steps:

1. Forward `trigger` to the sub-FSM's `TryFire`. If it returns `true`, the parent
   returns `true` immediately — the trigger was consumed at the inner level.
2. Otherwise, evaluate the parent's own switch over `(Current, trigger)`. If a matching
   arm exists, fire it (running parent `OnExit` + `OnEnter` as usual). If not, return
   `false`.

This is the UML default — "innermost handler wins". A trigger that is meaningful at
both levels (like `Cancel`) can be intercepted by the sub-machine for cleanup, or fall
through to the parent for an outright abort, depending on which level declares an arm
for it.

```csharp
// Parent's TryFire, generated when Loading is a composite (simplified):
public bool TryFire(DocTrig trigger)
{
    if (_state == DocState.Loading && _subFsm_Loading.TryFire(trigger))
        return true;  // sub-machine consumed it

    // ... parent's own (Current, trigger) switch ...
}
```

---

## Sub-FSM lifecycle on parent transitions

When the parent **enters** a composite state, the sub-FSM is positioned at its initial
state (or its last leaf state, if `[HistoryState]` is declared — see below).

When the parent **exits** a composite state, the sub-FSM is left at whatever state it
was in. If `[HistoryState]` is declared, that state is captured for the next entry.

The sub-FSM's `OnExit`/`OnEnter` hooks fire normally as it transitions internally. They
do **not** fire when the parent enters or exits the composite — that boundary is a
state-population event, not an internal transition. See [Internal
mechanics](#internal-mechanics-for-the-curious) below.

---

## Shallow history with `[HistoryState]`

By default, every entry into a composite state resets the sub-FSM to its initial state.
To make the sub-FSM resume where it left off, pair the composite with `[HistoryState]`:

```csharp
[CompositeState<DocState>(State = DocState.Loading, SubMachine = typeof(LoadingFsm))]
[HistoryState<DocState>(State = DocState.Loading)]
public partial class DocMachine { }
```

With history:

```csharp
var doc = new DocMachine();
doc.TryFire(DocTrig.Begin);   // Idle → Loading; sub-FSM at Fetching
doc.TryFire(DocTrig.Parsed);  // sub-FSM: Fetching → Parsing
doc.TryFire(DocTrig.Fail);    // parent: Loading → Failed; sub-FSM left at Parsing
doc.TryFire(DocTrig.Reset);   // Failed → Idle
doc.TryFire(DocTrig.Begin);   // Idle → Loading; sub-FSM RESUMES at Parsing (not Fetching)
```

Without `[HistoryState]`, the final `Begin` would reset the sub-FSM to `Fetching`.

### Shallow only

History is *shallow*: each composite remembers its direct sub-state only. When a
sub-machine is itself a composite and it is restored from history, its **own**
sub-machines are reset to their initial states regardless of whether they declare
history. There is no deep-history mode.

If you need deep restoration, model the full leaf state explicitly in the outer level's
history field — or open an issue describing the use case.

### Pairing rule

`[HistoryState(State = X)]` requires a matching `[CompositeState(State = X, ...)]` on
the same class. A bare `[HistoryState]` emits **ZSM0010**.

---

## Nesting

Sub-machines can themselves be composites. Depth is unbounded — every level just has
its own pair of attributes:

```csharp
[StateMachine(InitialState = nameof(Inner.A))]
[Transition<Inner, T>(From = Inner.A, On = T.X, To = Inner.B)]
[Terminal<Inner>(State = Inner.B)]
public partial class DeepestFsm { }

[StateMachine(InitialState = nameof(Mid.P))]
[Transition<Mid, T>(From = Mid.P, On = T.Y, To = Mid.Q)]
[Terminal<Mid>(State = Mid.Q)]
[CompositeState<Mid>(State = Mid.P, SubMachine = typeof(DeepestFsm))]
public partial class MiddleFsm { }

[StateMachine(InitialState = nameof(Outer.Working))]
[Transition<Outer, T>(From = Outer.Working, On = T.Done, To = Outer.Idle)]
[Terminal<Outer>(State = Outer.Idle)]
[CompositeState<Outer>(State = Outer.Working, SubMachine = typeof(MiddleFsm))]
public partial class OuterMachine { }
```

Dispatch cascades inside-out: the deepest active sub-FSM gets the trigger first, then
its parent, then its grandparent. The first level that has a matching arm consumes it.

### Pragmatic limit

Two or three levels are normal. Beyond that, the state space tends to be easier to
read as a flat enum with descriptive names (`Working_Middle_Deep_A`) than as a
multi-file hierarchy. Composite states are a tool for *logical grouping*, not deep
taxonomy.

---

## Concurrent mode

Composite states are **not supported** with `[StateMachine(Concurrent = true)]`. Both
attributes on the same class is a compile error (**ZSM0005**).

The fundamental issue is atomicity. Concurrent mode uses a CAS loop over a single
`long` to make every transition observable as either fully-done or not-started. A
composite transition is not one CAS: it requires reading the parent's state, dispatching
to a sub-FSM (which performs its own CAS), and updating either the sub-FSM or the
parent — possibly both, possibly with history bookkeeping. There is no way to make
that multi-step process atomic with a single CAS, and stacking CASes per level invites
ABA hazards and torn reads of history fields.

### Workarounds

- **Flatten the hierarchy.** Encode the inner states directly in the parent's enum and
  declare flat transitions. Concurrent mode imposes no limit on the number of states.
- **Drop concurrent mode and use an external lock.** A single `lock` on the machine's
  `TryFire` is simpler than reasoning about hierarchical CAS, and lets you keep both
  guards and composite states.

---

## Diagnostics

| ID | Severity | Trigger |
|---|---|---|
| [ZSM0005](../diagnostics/ZSM0005.md) | Error | `[CompositeState]` on a `[StateMachine(Concurrent = true)]` class |
| [ZSM0006](../diagnostics/ZSM0006.md) | Error | `SubMachine` type is not a `[StateMachine]` partial class |
| [ZSM0007](../diagnostics/ZSM0007.md) | Error | Sub-machine's `TTrigger` differs from the parent's |
| [ZSM0008](../diagnostics/ZSM0008.md) | Error | `State` value is not a member of the parent's `TState` enum |
| [ZSM0009](../diagnostics/ZSM0009.md) | Error | The same `State` is declared composite more than once |
| [ZSM0010](../diagnostics/ZSM0010.md) | Error | `[HistoryState]` without a matching `[CompositeState]` |
| [ZSM0011](../diagnostics/ZSM0011.md) | Error | The same state is declared both `[CompositeState]` and `[Terminal]` |

All composite-state diagnostics are errors. The generator refuses to emit code for a
malformed hierarchy because the result would be ambiguous at runtime.

---

## Internal mechanics (for the curious) {#internal-mechanics-for-the-curious}

The generator emits two helpers on every `[StateMachine]` class:

```csharp
internal void Reset();              // restore to InitialState
internal void ResetTo(TState s);    // restore to a specific state
```

Both **set the state field directly without firing `OnExit`/`OnEnter`**. They are
state-population mechanics — used by the parent's generated code when it enters a
composite state — not transitions. They cascade: `Reset` on a parent that owns
composites recursively `Reset`s each sub-FSM.

When the parent enters a composite state `X`:

- If `[HistoryState(State = X)]` is declared and history was captured on a prior exit,
  the parent calls `_subFsm_X.ResetTo(_history_X)`.
- Otherwise, the parent calls `_subFsm_X.Reset()`.

When the parent exits `X` (a parent-level transition fires `From = X`):

- If `[HistoryState(State = X)]` is declared, the parent captures `_history_X =
  _subFsm_X.Current` before the exit.
- Otherwise, no history is recorded.

`Reset` and `ResetTo` are marked `internal` because consumer code should not be
positioning state machines outside of declared transitions. If you find yourself
wanting to call them from application code, your model probably has a missing
transition — file an issue describing the case before reaching for them via
`InternalsVisibleTo`.

---

## Related

- [States and Triggers](states-and-triggers.md) — the underlying enum-based model
- [Transitions](transitions.md) — how `TryFire` evaluates the parent's switch arms
- [Concurrent Mode](concurrent-mode.md) — why composites are incompatible
- [Attribute Reference](../attributes.md) — `[CompositeState]` and `[HistoryState]` signatures
