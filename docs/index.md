---
id: index
title: ZeroAlloc.StateMachine
slug: /
description: Source-generated, zero-allocation finite state machines for .NET. Add [StateMachine] and [Transition] attributes to a partial class; the generator emits a TryFire switch expression with no heap allocation on the transition path. AOT-safe.
sidebar_position: 1
---

# ZeroAlloc.StateMachine

Source-generated, zero-allocation finite state machines for .NET.

Add `[StateMachine]` and `[Transition<TState, TTrigger>]` to a `partial` class or struct. The generator emits a `TryFire(TTrigger)` method as a `switch` expression over `(TState, TTrigger)` tuples — no dictionary, no delegate dispatch, no heap allocation on the transition path.

---

## Quick Example

```csharp
[StateMachine(InitialState = nameof(State.Idle))]
[Transition<State, Trigger>(From = State.Idle,    On = Trigger.Submit, To = State.Pending)]
[Transition<State, Trigger>(From = State.Pending, On = Trigger.Pay,   To = State.Done)]
[Terminal<State>(State = State.Done)]
public partial class OrderMachine { }

var machine = new OrderMachine();
machine.TryFire(Trigger.Submit); // true — Idle → Pending
machine.Current;                 // Pending
```

---

## Contents

| Page | Description |
|---|---|
| [Getting Started](getting-started.md) | Install and define your first machine |
| [Attributes](attributes.md) | `[StateMachine]`, `[Transition]`, `[Terminal]` reference |
| [Source Generator](source-generator.md) | What the generator emits — input/output examples |
| [Testing](testing.md) | Unit-test state machines without mocking |
| [AOT & Trimming](aot.md) | Native AOT compatibility |
| [Performance](performance.md) | Benchmark results and allocation profile |

### Core Concepts

| Page | Description |
|---|---|
| [States and Triggers](core-concepts/states-and-triggers.md) | Enums as states and triggers, naming conventions |
| [Transitions](core-concepts/transitions.md) | Directed edges, `TryFire`, ordering, entry/exit contract |
| [Concurrent Mode](core-concepts/concurrent-mode.md) | CAS loop, `Volatile.Read`, hook ordering, guard restrictions |
| [Composite States](core-concepts/composite-states.md) | Hierarchical sub-FSMs, dispatch order, shallow history |
| [Timeout Transitions](core-concepts/timeout-transitions.md) | `AfterMs` edges, lazy timers, race-safe CAS, `IDisposable` |
| [Concurrent Parts](core-concepts/concurrent-parts.md) | Multiple independent FSMs in one class via `[StateMachineGroup]` |
| [Diagram Export](core-concepts/diagram-export.md) | `Diagram = true` emits a Mermaid `stateDiagram-v2` `const string` |

### Guides

| Page | Description |
|---|---|
| [Guards](guides/guards.md) | Block transitions at runtime with `When = true` |
| [Entry and Exit Actions](guides/entry-exit-actions.md) | React to state crossings with `partial void` hooks |
| [Terminal States](guides/terminal-states.md) | Intentional sinks and the `[Terminal]` attribute |
| [Circuit Breaker Example](guides/circuit-breaker.md) | Real-world use: thread-safe circuit breaker |

### Diagnostics

| ID | Severity | Description |
|---|---|---|
| [ZSM0001](diagnostics/ZSM0001.md) | Warning | Unreachable state |
| [ZSM0002](diagnostics/ZSM0002.md) | Warning | Unintentional sink state |
| [ZSM0003](diagnostics/ZSM0003.md) | Warning | Single-use trigger (possible typo) |
| [ZSM0004](diagnostics/ZSM0004.md) | Error | Concurrent mode on a struct |
| [ZSM0005](diagnostics/ZSM0005.md) | Error | Composite state on a concurrent machine |
| [ZSM0006](diagnostics/ZSM0006.md) | Error | Sub-machine is not a `[StateMachine]` |
| [ZSM0007](diagnostics/ZSM0007.md) | Error | Sub-machine trigger type mismatch |
| [ZSM0008](diagnostics/ZSM0008.md) | Error | Composite state value not in `TState` |
| [ZSM0009](diagnostics/ZSM0009.md) | Error | Duplicate composite state |
| [ZSM0010](diagnostics/ZSM0010.md) | Error | History state without composite |
| [ZSM0011](diagnostics/ZSM0011.md) | Error | Composite state cannot also be terminal |
| [ZSM0012](diagnostics/ZSM0012.md) | Error | Timeout transition requires concurrent mode |
| [ZSM0013](diagnostics/ZSM0013.md) | Error | `AfterMs` must be positive |
| [ZSM0014](diagnostics/ZSM0014.md) | Error | `[StateMachine]` and `[StateMachineGroup]` are mutually exclusive |
| [ZSM0015](diagnostics/ZSM0015.md) | Error | Duplicate state machine part name |
| [ZSM0016](diagnostics/ZSM0016.md) | Error | Transition `Part` mismatch |
| [ZSM0017](diagnostics/ZSM0017.md) | Error | `[StateMachineGroup]` declared with no parts |
| [ZSM0018](diagnostics/ZSM0018.md) | Error | Composite state inside a group |
| [ZSM0019](diagnostics/ZSM0019.md) | Error | Incompatible user-supplied `Dispose` |
| [ZSM0020](diagnostics/ZSM0020.md) | Warning | `Diagram = true` on a class with no transitions |
| [ZSM0021](diagnostics/ZSM0021.md) | Error | User constructor must call `HookConstructor()` |
