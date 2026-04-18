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
