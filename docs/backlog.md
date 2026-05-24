# ZeroAlloc.StateMachine — Backlog

The original post-v1 graduation backlog (B1–B5) has fully shipped. New items can be added below as real-world friction surfaces.

---

## B1 — Hierarchical / nested states — ✅ shipped (PR #25)

**Shipped:** sub-machines embedded within a parent state via `[CompositeState<TState>(State = X, SubMachine = typeof(SubFsm))]`. When entering a composite state, the generator delegates `TryFire` to the sub-machine first; sub-machine exit propagates the trigger back to the parent. See `CompositeStateAttribute.cs`, `CompositeStateModel.cs`, and `CompositeStateTests.cs`.

---

## B2 — History states — ✅ shipped (PR #25)

**Shipped:** shallow-history support via `[HistoryState<TState>(State = X)]`. The generator stores the last active sub-state before exit and restores it on re-entry. See `HistoryStateAttribute.cs` and `HistoryStateModel.cs`.

---

## B3 — Timeout transitions — ✅ shipped (PR #27)

**Shipped:** automatic trigger firing after a configurable duration via `[Transition<S,T>(From = X, On = Y, To = Z, AfterMs = 5000)]`. The generator emits a race-safe `System.Threading.Timer` field with `Interlocked.CompareExchange` lazy init, started on `OnEnter{State}`, cancelled on `OnExit{State}`. See `TimedTransitionTests.cs` for coverage including concurrent firing and disposal semantics.

---

## B4 — Visual diagram export — ✅ shipped (PR #29)

**Shipped:** Mermaid state diagram emitted alongside `.g.cs` as `{TypeName}.mermaid`, opt-in via `[StateMachine(Diagram = true)]`. The companion fix in the same PR closed an initial-state arm gap (initial state's `OnEnter` arm now runs on construction). See `MermaidDiagramWriter.cs` and `InitialStateArmTests.cs`.

---

## B5 — Per-trigger granularity for concurrent mode — ✅ shipped (PR #27)

**Shipped:** multiple independent state variables within one machine via `[StateMachineGroup]` + `[StateMachinePart]`. The generator emits one `long` field per part with independent CAS loops, each carrying its own transition table. See `StateMachineGroupAttribute.cs`, `StateMachinePartAttribute.cs`, `StateMachineGroupWriter.cs`, and `StateMachineGroupTests.cs`.

---

## Out of scope (for now)

The original B1–B5 set covered the immediate post-v1 graduation candidates. Future entries graduate from this section once real-world friction surfaces a concrete value/cost tradeoff. Speculative additions are deliberately omitted — file a new backlog item with the friction narrative when one emerges.
