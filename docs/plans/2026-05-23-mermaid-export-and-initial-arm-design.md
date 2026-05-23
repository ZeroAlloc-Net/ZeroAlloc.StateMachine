# Mermaid diagram export + initial-state timer arm (backlog B4 + B3 follow-up)

Date: 2026-05-23
Status: Approved
Backlog: graduates `ZeroAlloc.StateMachine` item B4; closes the v1.4 follow-up flagged in `docs/core-concepts/timeout-transitions.md#caveats`

## Problem

v1.4 of `ZeroAlloc.StateMachine` shipped B3 (timeout transitions) + B5
(concurrent state parts). Two loose ends remain in the StateMachine
roadmap:

- **B4: Visual diagram export.** A state machine's transition table is a
  graph, and graphs read better as diagrams than as `[Transition]`
  attribute stacks. Mermaid's `stateDiagram-v2` syntax renders inline
  in GitHub READMEs, Mermaid Live Editor, and most docs sites — so the
  generator can ship a ready-to-paste diagram for every FSM.
- **B3 follow-up: initial-state arm gap.** v1.4's emit arms timers only
  inside `TryFire`'s post-CAS success block. If a user declares
  `InitialState = "Working"` with a `[Transition(From = Working, ...,
  AfterMs = 5000)]`, the `Working → Dead` timer never arms until
  something transitions INTO `Working` from elsewhere. Documented as a
  caveat at v1.4 ship; this lifts it into actual behavior.

The two are independent on code paths but ship together because both
land in `StateMachineWriter` / `StateMachineGroupWriter` and share a
release cycle.

## Goals

- Opt-in `MermaidDiagram` const on the generated partial for every
  `[StateMachine]` / `[StateMachineGroup]` class with `Diagram = true`.
- Full-fidelity rendering: composites nested, parts grouped, timed
  edges annotated, guards labeled, initial + terminal markers, history
  pseudo-states.
- Zero-allocation steady state preserved — `MermaidDiagram` is a
  `const string`, not a property or method.
- AOT-friendly, generator-driven, no reflection.
- Initial-state timer arm at construction (and at `Reset()` /
  `ResetTo(state)`) so the documented behavior matches the implemented
  behavior.

## Decisions

Locked during brainstorming. Each pinned via Q&A in the session.

| Question | Decision |
|---|---|
| Diagram format | Mermaid only (PlantUML deferred until a real consumer asks) |
| Delivery mechanism | `public const string MermaidDiagram` on the generated partial (no filesystem output, no MSBuild target) |
| Opt-in vs always-on | Opt-in via `[StateMachine(Diagram = true)]` + `[StateMachineGroup(Diagram = true)]`; default off |
| Rendering scope | Full feature parity — initial, terminal, composite-nested, group-bucketed, timed-annotated, guard-labeled, history pseudo-state |
| Group diagrams | One combined diagram per group; each part wrapped in `state {Name} { ... }` at top level |
| Initial-arm scope | Constructor + `Reset()` + `ResetTo(state)` all arm via a shared `ArmInitialStateTimers()` helper |
| User-declared ctor collision | Diagnose with `ZSM0021`; tell user to call the generated `HookConstructor()` partial hook |

## Design

### B4 — New / extended public attributes

```csharp
namespace ZeroAlloc.StateMachine;

// EXTENDED — new optional property
public sealed class StateMachineAttribute : Attribute
{
    // ... existing InitialState / Concurrent ...
    public bool Diagram { get; init; } = false;
}

// EXTENDED — new optional property
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class StateMachineGroupAttribute : Attribute
{
    public bool Diagram { get; init; } = false;
}
```

When `Diagram = true`, the generator emits:

```csharp
partial class Watchdog
{
    /// <summary>Mermaid stateDiagram-v2 representation of this state machine.</summary>
    public const string MermaidDiagram = """
        stateDiagram-v2
            [*] --> Idle
            Idle --> Working: Start
            Working --> Working: Heartbeat
            Working --> Dead: Timeout (after 5000ms)
            Dead --> [*]
        """;
}
```

(C# 11 raw string literals; falls back to a quoted multi-line string for
`netstandard2.0` source-generator emission. The emit writer chooses
based on the target's language version.)

### B4 — Rendering rules

| Model element | Mermaid emit |
|---|---|
| `InitialState = "Idle"` | `[*] --> Idle` |
| `[Transition(From=A, On=T, To=B)]` | `A --> B: T` |
| `[Transition(... AfterMs = N)]` | `A --> B: T (after Nms)` |
| `[Transition(... When = true)]` | `A --> B: T [guard]` |
| `[Transition(...)]` with both | `A --> B: T (after Nms) [guard]` |
| `[Terminal(State = X)]` | `X --> [*]` |
| `[CompositeState(State = X, SubMachine = typeof(Sub))]` | `state X { …sub-FSM rendered recursively… }` |
| `[HistoryState(State = X)]` | inside the `state X { ... }` block: `state H as History` plus `[*] --> H` |
| `[StateMachineGroup]` with parts P, Q | one diagram with `state P { ... }` + `state Q { ... }` siblings |

Cross-class composite rendering reuses the existing
`ResolveSubMachineSymbol` helper (v1.3); the sub-FSM's transitions are
walked via `GetAttributes()` on the metadata symbol, so cross-assembly
composites render correctly.

### B4 — New diagnostic

| ID | Severity | Condition |
|---|---|---|
| `ZSM0020` | Warning | `[StateMachine(Diagram = true)]` (or `[StateMachineGroup(Diagram = true)]`) on a class with zero transitions — the diagram would be empty |

### B4 — Files touched

- MOD: `src/ZeroAlloc.StateMachine/StateMachineAttribute.cs` (add `Diagram` property)
- MOD: `src/ZeroAlloc.StateMachine/StateMachineGroupAttribute.cs` (add `Diagram` property)
- MOD: `src/ZeroAlloc.StateMachine/PublicAPI.Unshipped.txt`
- MOD: `src/ZeroAlloc.StateMachine.Generator/StateMachineModel.cs` (add `Diagram` flag)
- MOD: `src/ZeroAlloc.StateMachine.Generator/StateMachineGroupModel.cs` (add `Diagram` flag)
- MOD: `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs` (read the new property)
- NEW: `src/ZeroAlloc.StateMachine.Generator/MermaidDiagramWriter.cs` (emit the diagram body; consumed by both `StateMachineWriter` and `StateMachineGroupWriter`)
- MOD: `src/ZeroAlloc.StateMachine.Generator/StateMachineWriter.cs` (call into `MermaidDiagramWriter` when `model.Diagram`)
- MOD: `src/ZeroAlloc.StateMachine.Generator/StateMachineGroupWriter.cs` (same)
- MOD: `src/ZeroAlloc.StateMachine.Generator/StateMachineDiagnostics.cs` (add `ZSM0020`)

### B — Initial-state timer arm

**New generator-emitted helper (per class with at least one timed edge):**

```csharp
private void ArmInitialStateTimers()
{
    var current = Current;
    if (current == WdState.Working)
    {
        // Same race-safe lazy-init pattern as the in-TryFire arm path.
        var __t = _timer_Working_Timeout;
        if (__t is null)
        {
            var __new = new System.Threading.Timer(
                static s => ((Watchdog)s!).TryFire(WdTrigger.Timeout),
                this, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            __t = System.Threading.Interlocked.CompareExchange(ref _timer_Working_Timeout, __new, null) ?? __new;
            if (!System.Object.ReferenceEquals(__t, __new)) __new.Dispose();
        }
        __t.Change(5000, System.Threading.Timeout.Infinite);
    }
    // ... one block per (From, On) timed edge
}
```

**Call sites (all generated):**
1. Constructor: see "User-declared ctor handling" below.
2. `Reset()`: append `ArmInitialStateTimers();` after `_state = InitialState;`.
3. `ResetTo(state)`: append `ArmInitialStateTimers();` after `_state = state;`.

For `[StateMachineGroup]`: one `ArmInitialStateTimers_{PartName}()` helper per part; the group ctor calls each in turn. Groups don't emit `Reset` / `ResetTo`, so the helper is only invoked from the ctor.

### B — User-declared ctor handling

The generator emits `partial void HookConstructor();` plus an
implementing declaration that calls `ArmInitialStateTimers()`:

```csharp
partial class Watchdog
{
    partial void HookConstructor()
    {
        ArmInitialStateTimers();
    }
}
```

If the user has NOT declared their own ctor, the generator additionally
emits a default ctor that calls the hook:

```csharp
public Watchdog() => HookConstructor();
```

If the user HAS declared a ctor, the generator detects this via Roslyn
symbol inspection (`type.InstanceConstructors`) and emits ONLY the
partial hook — the user is responsible for calling
`HookConstructor()` from their own ctor. If they forget, the diagnostic
fires:

| ID | Severity | Condition |
|---|---|---|
| `ZSM0021` | Error | Class declares a user-defined ctor but its body does not invoke `HookConstructor()`. Detection is best-effort syntactic — walk the ctor's `SyntaxNode` for an `HookConstructor()` invocation. |

Detection caveat: `ZSM0021` uses a syntactic walk, so it can miss
indirect invocations (e.g., `HookConstructor()` called from a helper).
Documented as a known limitation; a user who wants the indirect-call
shape can `#pragma warning disable ZSM0021` to silence.

### B — Composite + initial-arm interaction

Composite states are mutually exclusive with concurrent mode
(`ZSM0005`), and timed edges require concurrent (`ZSM0012`). So
composite + timed is transitively impossible. No special-case logic
needed for `[CompositeState]` in the arm helper.

### B — Docs change

- `docs/core-concepts/timeout-transitions.md`: remove the "Caveats"
  section (no longer a caveat); update the body to state that timers
  arm "on construction, on entry, and on `Reset()` / `ResetTo(state)`".

### B — Files touched

- MOD: `src/ZeroAlloc.StateMachine.Generator/StateMachineWriter.cs` (emit `ArmInitialStateTimers` + hook + default ctor; invoke from `Reset` / `ResetTo`)
- MOD: `src/ZeroAlloc.StateMachine.Generator/StateMachineGroupWriter.cs` (per-part arm helpers + group ctor)
- MOD: `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs` (detect user-declared ctor; emit `ZSM0021` if hook not called)
- MOD: `src/ZeroAlloc.StateMachine.Generator/StateMachineDiagnostics.cs` (add `ZSM0021`)
- MOD: `docs/core-concepts/timeout-transitions.md` (remove caveats, update body)

## Testing

`MermaidDiagramGeneratorTests.cs` (new):
- Flat machine — initial, terminal, plain transition, timed-annotated,
  guard-labeled all render correctly.
- Composite — nested `state X { ... }` block contains the sub-FSM.
- Group — combined diagram with two `state {Name} { ... }` siblings.
- `Diagram = false` (or absent) — no `MermaidDiagram` const emitted.
- `ZSM0020` fires when `Diagram = true` and zero transitions.

`InitialStateArmTests.cs` (new, runtime):
- Initial state is the source of a timed edge — timer fires after the
  configured duration without any user `TryFire`.
- `Reset()` re-arms initial-state timers.
- `ResetTo(state)` arms whichever state's timers apply.
- Group with timed-edge in one part — only that part's initial-state
  timer arms; other part is unaffected.

`DiagnosticTests.cs` (modify):
- `ZSM0020` positive (Diagram = true + zero transitions).
- `ZSM0021` positive (user-declared ctor without `HookConstructor()` invocation).
- `ZSM0021` negative (user-declared ctor WITH the invocation — no diagnostic).

## Out of scope

- PlantUML emission (deferred until a real consumer asks).
- Filesystem output (`{TypeName}.mermaid` alongside `.g.cs`). Achievable
  via MSBuild target but adds infrastructure not currently justified.
- Per-part separate `MermaidDiagram_{PartName}` constants. One combined
  diagram per group is more discoverable; per-part can be split out in
  a follow-up if a consumer asks.
- Diagram styling / theming (e.g. Mermaid `classDef` blocks).
- Runtime diagram rendering helper (e.g. `Watchdog.RenderToConsole()`).
  Users have the const string; they can do what they want with it.
- Auto-arming inside the generated `OnEnter` partial hooks. Existing
  emit already covers this via the post-CAS arm path; the initial-arm
  fix only patches the construction + reset corners.

## Backward compatibility

Strictly additive:
- Two new optional properties (`Diagram`) on existing attributes; default
  `false` preserves v1.4 behavior.
- New `MermaidDiagram` const appears only when `Diagram = true`.
- `ArmInitialStateTimers` is a new private method; no public surface
  change.
- Generated ctor only appears when at least one timed edge exists AND
  no user-declared ctor is present.
- The `HookConstructor` partial is a `partial void` — never required
  to be implemented; its existence does not change consumer code paths.
- No changes to `PublicAPI.Shipped.txt`; everything new lands in
  `PublicAPI.Unshipped.txt`.

No SemVer break. Lands as a `feat:` commit, minor bump (1.4.x → 1.5.0).
