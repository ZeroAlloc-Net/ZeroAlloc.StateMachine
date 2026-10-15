# ZeroAlloc.StateMachine

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.StateMachine.svg)](https://www.nuget.org/packages/ZeroAlloc.StateMachine)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

Source-generated, zero-allocation finite state machines for .NET.

Add `[StateMachine]` and `[Transition<TState, TTrigger>]` attributes to a `partial` class or struct. A Roslyn source generator emits a `TryFire(TTrigger)` method as a `switch` expression over `(TState, TTrigger)` tuples — no dictionary, no delegate dispatch, no heap allocation on the transition path. AOT-safe.

---

## Quick start

```bash
dotnet add package ZeroAlloc.StateMachine
```

```csharp
using ZeroAlloc.StateMachine;

public enum State   { Idle, Pending, Done }
public enum Trigger { Submit, Pay }

[StateMachine(InitialState = nameof(State.Idle))]
[Transition<State, Trigger>(From = State.Idle,    On = Trigger.Submit, To = State.Pending)]
[Transition<State, Trigger>(From = State.Pending, On = Trigger.Pay,   To = State.Done)]
[Terminal<State>(State = State.Done)]
public partial class OrderMachine { }

var machine = new OrderMachine();
machine.TryFire(Trigger.Submit); // true — Idle → Pending
var state = machine.Current;     // State.Pending
machine.TryFire(Trigger.Pay);    // true — Pending → Done
machine.TryFire(Trigger.Submit); // false — Done has no outgoing transitions
```

---

## Performance

Head-to-head vs [Stateless](https://github.com/dotnet-state-machine/stateless) 5.20.1 (the de-facto state-machine library in .NET). .NET 10.0.12, BenchmarkDotNet v0.15.8.

| Operation | Stateless | ZA.StateMachine | Speedup |
|---|---:|---:|---:|
| Fire valid (3-step cycle) | 4,476 ns / 7,600 B | **15 ns / 0 B** | **~300× faster, 0 B alloc** |
| Fire invalid | 23 ns / 24 B | **1.6 ns / 0 B** | **~15× faster, 0 B alloc** |
| Guard allowed | 2,430 ns / 4,336 B | **< 1 ns / 0 B** | **> 1,000× faster, 0 B alloc** |
| Guard blocked | 422 ns / 768 B | **< 1 ns / 0 B** | **> 400× faster, 0 B alloc** |

Stateless walks a `Dictionary<TTrigger, StateRepresentation>` on every fire and allocates trigger/transition info objects. ZA emits a `switch` expression over `(State, Trigger)` at compile time — single jump-table lookup, zero allocation on the dispatch path. The Fire-valid row also includes a per-iteration machine reset for both libraries; ZA resets in place with the generated `Reset()` and allocates nothing, because configuration is compile-time, while Stateless's reset includes its fluent `Configure().Permit()` rebuild — see [docs/performance.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/performance.md) for the full breakdown.

Full methodology + self-benchmark: [docs/performance.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/performance.md).

## Features

| Feature | Notes |
|---------|-------|
| Zero allocation on happy path | `TryFire` allocates 0 bytes — the `switch` is a compile-time constant |
| AOT / trimmer safe | Generator emits concrete switch arms; no reflection at runtime |
| Concurrent mode | `Interlocked.CompareExchange` CAS loop, `Volatile.Read` for `Current` |
| Guards | `partial bool Guard{Trigger}(TState, TTrigger)` — block a transition at runtime |
| Entry / exit hooks | `partial void OnEnter{State}` / `partial void OnExit{State}` — observe every crossing |
| Terminal states | `[Terminal<TState>]` silences the "no outgoing transitions" diagnostic |
| Struct support | `partial struct` machines eliminate even the instance heap allocation |
| Composite states | `[CompositeState<TState>]` delegates a state to a sub-machine; `[HistoryState<TState>]` restores its last sub-state on re-entry |
| Timeout transitions | `AfterMs` on a `[Transition]` fires it automatically after a time in the source state |
| Concurrent parts | `[StateMachineGroup]` + `[StateMachinePart<TState, TTrigger>]` host independent machines in one class |
| Diagram export | `Diagram = true` emits a Mermaid `stateDiagram-v2` as a `MermaidDiagram` constant |
| Diagnostics | Compile-time checks such as unreachable state, sink state, a likely trigger typo, and a guard ignored on a concurrent machine |

---

## Attribute overview

### `[StateMachine]`

```csharp
[StateMachine(InitialState = nameof(State.Idle), Concurrent = false)]
public partial class MyMachine { }
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `InitialState` | `string` | required | Name of the initial state enum value. Use `nameof(...)`. |
| `Concurrent` | `bool` | `false` | Enables thread-safe transitions via `Interlocked.CompareExchange`. |
| `Diagram` | `bool` | `false` | Emits a Mermaid diagram of the transitions as a `MermaidDiagram` constant. |

### `[Transition<TState, TTrigger>]`

```csharp
[Transition<State, Trigger>(From = State.Idle, On = Trigger.Submit, To = State.Pending, When = false)]
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `From` | `TState` | required | Source state. |
| `On` | `TTrigger` | required | Trigger that fires the transition. |
| `To` | `TState` | required | Destination state. |
| `When` | `bool` | `false` | Emit a `Guard{Trigger}` partial stub and add a `when` clause. |
| `AfterMs` | `int` | `0` | Fire this transition automatically after this many milliseconds in `From`. |
| `Part` | `string?` | `null` | The `[StateMachinePart]` this transition belongs to, inside a `[StateMachineGroup]`. |

### `[Terminal<TState>]`

```csharp
[Terminal<State>(State = State.Done)]
```

Marks a state as an intentional sink (no outgoing transitions). Silences ZSM0002.

---

## Generated code

For each annotated type the generator emits one file alongside the user's source:

```csharp
// <auto-generated />
partial class OrderMachine
{
    private State _state = State.Idle;
    public State Current => _state;

    public bool TryFire(Trigger trigger)
        => (Current, trigger) switch
        {
            (State.Idle,    Trigger.Submit) => Fire(State.Idle,    State.Pending, trigger),
            (State.Pending, Trigger.Pay)    => Fire(State.Pending, State.Done,    trigger),
            _ => false
        };

    private bool Fire(State from, State to, Trigger trigger) { ... }

    // Partial hook stubs — implement what you need, leave the rest
    partial void OnExitIdle(Trigger on);
    partial void OnExitPending(Trigger on);
    partial void OnEnterPending(State from);
    partial void OnEnterDone(State from);
}
```

---

## Hooks

```csharp
public partial class OrderMachine
{
    partial void OnExitIdle(Trigger on)
        => Console.WriteLine($"Leaving Idle via {on}");

    partial void OnEnterDone(State from)
        => Console.WriteLine($"Order complete, came from {from}");
}
```

---

## Guards

```csharp
[Transition<State, Trigger>(From = State.Pending, On = Trigger.Pay, To = State.Done, When = true)]
public partial class OrderMachine
{
    public bool HasBalance { get; set; }

    // Generator emits: private partial bool GuardPay(State from, Trigger on);
    private partial bool GuardPay(State from, Trigger on) => HasBalance;
}
```

---

## Concurrent mode

```csharp
public enum WorkerState   { Idle, Running }
public enum WorkerTrigger { Start }

[StateMachine(InitialState = nameof(WorkerState.Idle), Concurrent = true)]
[Transition<WorkerState, WorkerTrigger>(From = WorkerState.Idle, On = WorkerTrigger.Start, To = WorkerState.Running)]
[Terminal<WorkerState>(State = WorkerState.Running)]
public partial class WorkerMachine { }
```

State is stored in a `long` field, read with `Volatile.Read` and updated with `Interlocked.CompareExchange`. `TryFire` uses a CAS loop — safe for concurrent callers. Guards are not generated in concurrent mode (TOCTOU risk), so `When = true` there raises warning ZSM0022 and the transition fires unconditionally.

---

## Diagnostics

| ID | Severity | Description |
|----|----------|-------------|
| ZSM0001 | Warning | State is unreachable (no transition leads to it, not `InitialState`) |
| ZSM0002 | Warning | State has no outgoing transitions (use `[Terminal]` to acknowledge) |
| ZSM0003 | Warning | Single-use trigger whose name is close to a reused trigger (possible typo) |
| ZSM0004 | Error | `Concurrent = true` on a `partial struct` (not supported) |
| ZSM0022 | Warning | `When = true` on a concurrent machine is ignored; the transition fires unconditionally |

The full list is in the [diagnostics index](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/index.md#diagnostics).

---

## Documentation

Full docs live in [`docs/`](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/index.md):

- [Getting Started](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/getting-started.md)
- [Attribute Reference](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/attributes.md)
- [Source Generator](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/source-generator.md)
- [Testing](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/testing.md)
- [AOT & Trimming](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/aot.md)
- [Performance](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/performance.md)
- Core concepts: [States & Triggers](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/core-concepts/states-and-triggers.md) · [Transitions](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/core-concepts/transitions.md) · [Concurrent Mode](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/core-concepts/concurrent-mode.md) · [Composite States](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/core-concepts/composite-states.md) · [Timeout Transitions](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/core-concepts/timeout-transitions.md) · [Concurrent Parts](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/core-concepts/concurrent-parts.md) · [Diagram Export](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/core-concepts/diagram-export.md)
- Guides: [Guards](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/guides/guards.md) · [Entry/Exit Actions](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/guides/entry-exit-actions.md) · [Terminal States](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/guides/terminal-states.md) · [Circuit Breaker Example](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/guides/circuit-breaker.md)

---

## License

MIT — see [LICENSE](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/LICENSE).
