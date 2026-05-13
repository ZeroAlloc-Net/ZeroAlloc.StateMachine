# ZeroAlloc.StateMachine

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.StateMachine.svg)](https://www.nuget.org/packages/ZeroAlloc.StateMachine)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
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
public enum State   { Idle, Pending, Done }
public enum Trigger { Submit, Pay }

[StateMachine(InitialState = nameof(State.Idle))]
[Transition<State, Trigger>(From = State.Idle,    On = Trigger.Submit, To = State.Pending)]
[Transition<State, Trigger>(From = State.Pending, On = Trigger.Pay,   To = State.Done)]
[Terminal<State>(State = State.Done)]
public partial class OrderMachine { }

var machine = new OrderMachine();
machine.TryFire(Trigger.Submit); // true — Idle → Pending
machine.Current;                 // State.Pending
machine.TryFire(Trigger.Pay);    // true — Pending → Done
machine.TryFire(Trigger.Submit); // false — Done has no outgoing transitions
```

---

## Performance

Head-to-head vs [Stateless](https://github.com/dotnet-state-machine/stateless) 5.15 (the de-facto state-machine library in .NET). .NET 10.0.7, BenchmarkDotNet v0.14.0.

| Operation | Stateless | ZA.StateMachine | Speedup |
|---|---:|---:|---:|
| Fire valid (3-step cycle) | 4,495 ns / 7,272 B | **36 ns / 24 B** | **124× faster, 303× less alloc** |
| Fire invalid | 27 ns / 24 B | **1.6 ns / 0 B** | **17× faster, 0 B alloc** |
| Guard allowed | 2,718 ns / 4,160 B | **15 ns / 24 B** | **178× faster, 173× less alloc** |
| Guard blocked | 699 ns / 792 B | **0.3 ns / 0 B** | **2,200× faster, 0 B alloc** |

Stateless walks a `Dictionary<TTrigger, StateRepresentation>` on every fire and allocates trigger/transition info objects. ZA emits a `switch` expression over `(State, Trigger)` at compile time — single jump-table lookup, zero allocation on the dispatch path. The Fire-valid row also includes a per-iteration machine reset for both libraries; ZA's reset is one allocation because configuration is compile-time, while Stateless's reset includes its fluent `Configure().Permit()` rebuild — see [docs/performance.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/performance.md) for the full breakdown.

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
| Diagnostics | ZSM0001–ZSM0004: unreachable state, sink state, concurrent + guard, concurrent + struct |

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
[StateMachine(InitialState = nameof(State.Idle), Concurrent = true)]
[Transition<State, Trigger>(From = State.Idle, On = Trigger.Start, To = State.Running)]
public partial class WorkerMachine { }
```

State is stored as `volatile long`. `TryFire` uses a CAS loop — safe for concurrent callers. Guards are not generated in concurrent mode (TOCTOU risk).

---

## Diagnostics

| ID | Severity | Description |
|----|----------|-------------|
| ZSM0001 | Warning | State is unreachable (no transition leads to it, not `InitialState`) |
| ZSM0002 | Warning | State has no outgoing transitions (use `[Terminal]` to acknowledge) |
| ZSM0003 | Warning | Trigger appears in only one transition (possible typo) |
| ZSM0004 | Error | `Concurrent = true` on a `partial struct` (not supported) |

---

## Documentation

Full docs live in [`docs/`](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/index.md):

- [Getting Started](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/getting-started.md)
- [Attribute Reference](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/attributes.md)
- [Source Generator](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/source-generator.md)
- [Testing](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/testing.md)
- [AOT & Trimming](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/aot.md)
- [Performance](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/performance.md)
- Core concepts: [States & Triggers](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/core-concepts/states-and-triggers.md) · [Transitions](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/core-concepts/transitions.md) · [Concurrent Mode](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/core-concepts/concurrent-mode.md)
- Guides: [Guards](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/guides/guards.md) · [Entry/Exit Actions](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/guides/entry-exit-actions.md) · [Terminal States](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/guides/terminal-states.md) · [Circuit Breaker Example](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/guides/circuit-breaker.md)

---

## License

MIT
