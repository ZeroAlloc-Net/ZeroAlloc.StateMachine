---
id: attributes
title: Attribute Reference
sidebar_position: 3
---

# Attribute Reference

ZeroAlloc.StateMachine exposes three attributes. All live in the `ZeroAlloc.StateMachine` namespace.

---

## `[StateMachine]`

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class StateMachineAttribute : Attribute
```

Marks a `partial` class or struct as a source-generated state machine. The generator emits `TryFire`, `Current`, `Fire`, `OnExit` / `OnEnter` dispatchers, and partial method stubs.

### Properties

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `InitialState` | `string` | yes | — | The name of the initial state. **Always use `nameof(...)`** to keep it refactor-safe. |
| `Concurrent` | `bool` | no | `false` | When `true`, state is stored as `volatile long` and transitions use `Interlocked.CompareExchange`. Safe for concurrent callers. Guards are not generated in this mode. |

### Examples

```csharp
// Minimal
[StateMachine(InitialState = nameof(State.Idle))]
public partial class SimpleMachine { }

// Thread-safe
[StateMachine(InitialState = nameof(WorkerState.Idle), Concurrent = true)]
public partial class WorkerMachine { }

// Struct (eliminates instance heap allocation; Concurrent = true not allowed)
[StateMachine(InitialState = nameof(State.Off))]
public partial struct LightSwitch { }
```

### Constraints

- The target type must be declared `partial`.
- `Concurrent = true` on a `partial struct` produces diagnostic **ZSM0004** (error).
- `InitialState` must match the name of a value in the state enum — the generator validates this and emits **ZSM0001** / **ZSM0002** warnings if states are unreachable or have no outgoing edges.

---

## `[Transition<TState, TTrigger>]`

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public sealed class TransitionAttribute<TState, TTrigger> : Attribute
    where TState   : struct, Enum
    where TTrigger : struct, Enum
```

Declares a single directed edge in the state graph. Stack multiple attributes to define the complete graph.

### Properties

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `From` | `TState` | yes | — | Source state. The machine must be in this state for the trigger to fire. |
| `On` | `TTrigger` | yes | — | The trigger value that activates this edge. |
| `To` | `TState` | yes | — | Destination state after the transition fires. |
| `When` | `bool` | no | `false` | When `true`, the generator emits a `private partial bool Guard{TriggerName}(TState from, TTrigger on)` stub and adds a `when` clause to the switch arm. The transition fires only if the guard returns `true`. Ignored when `Concurrent = true`. |

### Examples

```csharp
// Simple edge
[Transition<OrderState, Trigger>(From = OrderState.Idle, On = Trigger.Submit, To = OrderState.Pending)]

// Edge with guard
[Transition<OrderState, Trigger>(From = OrderState.Pending, On = Trigger.Pay, To = OrderState.Paid, When = true)]

// Self-loop (same From and To)
[Transition<State, Trigger>(From = State.Active, On = Trigger.Ping, To = State.Active)]

// Multiple transitions from the same state on different triggers
[Transition<State, Trigger>(From = State.Idle, On = Trigger.Start,  To = State.Running)]
[Transition<State, Trigger>(From = State.Idle, On = Trigger.Cancel, To = State.Cancelled)]
```

### Multiple triggers from the same state

You can have any number of transitions leaving a given state, as long as each `(From, On)` pair is unique. The generated switch arm matches on `(Current, trigger)`, so two transitions from the same state on different triggers are independent arms.

### Type parameters

Both `TState` and `TTrigger` must be `enum` types. The generator reads the enum member names at compile time to emit the switch arms — no reflection at runtime.

---

## `[Terminal<TState>]`

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public sealed class TerminalAttribute<TState> : Attribute
    where TState : struct, Enum
```

Marks a state as an intentional sink — a state with no outgoing transitions. This silences diagnostic **ZSM0002** ("state has no outgoing transitions") for that state. It has no effect on the generated code.

### Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `State` | `TState` | yes | The terminal state value. |

### Examples

```csharp
// Single terminal
[Terminal<OrderState>(State = OrderState.Done)]

// Multiple terminals
[Terminal<OrderState>(State = OrderState.Shipped)]
[Terminal<OrderState>(State = OrderState.Cancelled)]
[Terminal<OrderState>(State = OrderState.Refunded)]
public partial class OrderMachine { }
```

### When to use

Use `[Terminal]` for every state that is a deliberate end state (no outgoing edges by design). Without it, the generator emits a warning on every such state asking you to confirm the omission was intentional.

---

## Attribute placement

All three attributes target `Class` and `Struct`:

```csharp
// Class
[StateMachine(InitialState = nameof(S.A))]
[Transition<S, T>(From = S.A, On = T.X, To = S.B)]
public partial class MyMachine { }

// Struct
[StateMachine(InitialState = nameof(S.A))]
[Transition<S, T>(From = S.A, On = T.X, To = S.B)]
public partial struct MyMachine { }
```

Nested types and generic types are not currently supported.
