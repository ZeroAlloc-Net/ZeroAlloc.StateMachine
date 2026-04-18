# ZeroAlloc.StateMachine — Backlog

Items not in v1. Implement once the core is stable and real-world usage patterns are understood.

---

## B1 — Hierarchical / nested states

Sub-machines embedded within a parent state. When entering a composite state, the sub-machine starts at its own initial state. When exiting, the sub-machine is reset.

**Scope:**
- `[CompositeState<TState>(State = X, SubMachine = typeof(SubFsm))]` attribute
- Generator: when entering composite state, delegate TryFire to sub-machine first
- Sub-machine exit propagates trigger back to parent

---

## B2 — History states

Re-enter the last active sub-state when re-entering a composite state (shallow history).

**Scope:**
- `[HistoryState<TState>(State = X)]` attribute
- Generator stores last active sub-state before exit; restores on re-entry

---

## B3 — Timeout transitions

Automatically fire a trigger after a configurable duration without external `TryFire`.

**Scope:**
- `[Transition<S,T>(From = X, On = Y, To = Z, AfterMs = 5000)]`
- Generator emits a `System.Threading.Timer` field, started on `OnEnter{State}`, cancelled on `OnExit{State}`
- Timer callback calls `TryFire` — concurrent-safe

---

## B4 — Visual diagram export

Generate a Mermaid or PlantUML state diagram from the declared transitions as part of the build.

**Scope:**
- Additional generator output: `{TypeName}.mermaid` alongside `.g.cs`
- Opt-in via `[StateMachine(Diagram = true)]`

---

## B5 — Per-trigger granularity for concurrent mode

Support multiple independent state variables within one machine (e.g., a component with both an operational state and a connection state that evolve concurrently).

**Scope:**
- `[StateMachineGroup]` containing multiple `[StateMachinePart]` fields
- Generator emits one `long` field per part with independent CAS loops
