---
id: terminal-states
title: Terminal States
sidebar_position: 3
---

# Terminal States

A **terminal state** is a state with no outgoing transitions — once reached, the machine stays there. This is a deliberate design choice in many workflows: an order that is `Shipped` or `Cancelled` has no further lifecycle.

---

## The diagnostic

The generator emits **ZSM0002** (warning) on any state that has no outgoing transitions and is not explicitly marked as terminal. This warns you when a sink state was probably forgotten rather than intentional:

```
warning ZSM0002: State 'Done' has no outgoing transitions.
If this is intentional, add [Terminal<OrderState>(State = OrderState.Done)].
```

---

## Silencing ZSM0002

Add `[Terminal<TState>]` for each intentional sink:

```csharp
[StateMachine(InitialState = nameof(OrderState.Idle))]
[Transition<OrderState, Trigger>(From = OrderState.Idle,    On = Trigger.Submit, To = OrderState.Pending)]
[Transition<OrderState, Trigger>(From = OrderState.Pending, On = Trigger.Pay,   To = OrderState.Done)]
[Terminal<OrderState>(State = OrderState.Done)]      // ← silences ZSM0002
public partial class OrderMachine { }
```

Multiple terminal states each need their own attribute:

```csharp
[Terminal<OrderState>(State = OrderState.Shipped)]
[Terminal<OrderState>(State = OrderState.Cancelled)]
[Terminal<OrderState>(State = OrderState.Refunded)]
public partial class OrderMachine { }
```

---

## Effect on generated code

`[Terminal]` has **no effect on the generated code**. The switch arm for a terminal state simply never appears (there are no `From = terminal` transitions), so `TryFire` from a terminal state always hits the `_ => false` default arm. The attribute is purely a compile-time annotation for the generator.

---

## Detecting terminal states at runtime

The `[Terminal]` attribute is a compile-time declaration, not a runtime interface. If you need to check at runtime whether a state is terminal, use a helper:

```csharp
public static bool IsTerminal(OrderState state)
    => state is OrderState.Shipped or OrderState.Cancelled or OrderState.Refunded;
```

Or let `TryFire` speak for itself — if it returns `false` for every possible trigger, the machine is in a terminal state (or the trigger is wrong for the current state).

---

## Initial state as terminal

If `InitialState` is also terminal (i.e. no outgoing transitions from it), the generator emits ZSM0002 immediately. This is almost always a mistake. If you genuinely need a machine with only one state and no transitions, add a self-loop or reconsider whether a state machine is the right abstraction.

---

## Pattern: completed machine

When a machine reaches a terminal state you may want to signal completion to callers. Use an `OnEnter` hook:

```csharp
public partial class OrderMachine
{
    public event EventHandler? Completed;

    partial void OnEnterShipped(OrderState from) => Completed?.Invoke(this, EventArgs.Empty);
    partial void OnEnterCancelled(OrderState from) => Completed?.Invoke(this, EventArgs.Empty);
}
```

Or use a `TaskCompletionSource<OrderState>`:

```csharp
public partial class OrderMachine
{
    private readonly TaskCompletionSource<OrderState> _tcs = new();
    public Task<OrderState> Completion => _tcs.Task;

    partial void OnEnterShipped(OrderState from)   => _tcs.TrySetResult(OrderState.Shipped);
    partial void OnEnterCancelled(OrderState from) => _tcs.TrySetResult(OrderState.Cancelled);
}

// Awaitable
var finalState = await machine.Completion;
```
