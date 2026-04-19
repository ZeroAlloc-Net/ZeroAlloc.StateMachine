---
id: transitions
title: Transitions
sidebar_position: 2
---

# Transitions

A **transition** is a directed edge in the state graph: it says "when in state *From*, if trigger *On* fires and the optional guard passes, move to state *To*".

---

## Declaring transitions

Each transition is one `[Transition<TState, TTrigger>]` attribute on the machine type:

```csharp
[StateMachine(InitialState = nameof(State.Idle))]
[Transition<State, Trigger>(From = State.Idle,    On = Trigger.Submit, To = State.Pending)]
[Transition<State, Trigger>(From = State.Pending, On = Trigger.Pay,   To = State.Done)]
public partial class OrderMachine { }
```

The attribute is `AllowMultiple = true` — stack as many as the graph requires.

---

## How TryFire works

`TryFire(trigger)` evaluates `(Current, trigger)` against a `switch` expression. Each `[Transition]` becomes one switch arm:

```
(State.Idle,    Trigger.Submit) => Fire(State.Idle,    State.Pending, trigger)
(State.Pending, Trigger.Pay)   => Fire(State.Pending, State.Done,    trigger)
_ => false
```

`Fire` calls `OnExit(from)`, writes `_state = to`, calls `OnEnter(to)`, then returns `true`. If no arm matches, `TryFire` returns `false` and the state is unchanged.

`TryFire` never throws. Unrecognised (From, On) pairs are the `_ => false` default arm.

---

## Multiple transitions from one state

A state can have any number of outgoing transitions, as long as each `(From, On)` pair is unique:

```csharp
[Transition<State, Trigger>(From = State.Idle, On = Trigger.Submit, To = State.Pending)]
[Transition<State, Trigger>(From = State.Idle, On = Trigger.Cancel, To = State.Cancelled)]
```

The same trigger on the same state twice would produce a duplicate switch arm — the compiler will emit a warning or error. The generator does not deduplicate; make each edge explicit.

---

## Multiple transitions to one state

Multiple edges may lead to the same state:

```csharp
[Transition<State, Trigger>(From = State.Pending,   On = Trigger.Cancel, To = State.Cancelled)]
[Transition<State, Trigger>(From = State.Processing, On = Trigger.Cancel, To = State.Cancelled)]
```

The `OnEnterCancelled(State from)` hook receives the `from` argument so you can distinguish which state was left.

---

## Self-loops

A state can transition to itself:

```csharp
[Transition<State, Trigger>(From = State.Active, On = Trigger.Refresh, To = State.Active)]
```

`Fire` is still called — `OnExit` and `OnEnter` fire, and `_state` is written (to the same value). This is useful for logging or resetting internal counters on each refresh.

---

## Guards on transitions

Set `When = true` to enable a runtime guard check. The generator emits a `when` clause:

```csharp
[Transition<State, Trigger>(From = State.Pending, On = Trigger.Pay, To = State.Done, When = true)]
```

Generated arm:
```csharp
(State.Pending, Trigger.Pay) when GuardPay(State.Pending, Trigger.Pay)
    => Fire(State.Pending, State.Done, trigger),
```

If `GuardPay` returns `false`, the arm is not selected and `TryFire` returns `false`. See [Guards](../guides/guards.md) for the full guide.

---

## Transition ordering

The generator emits switch arms in declaration order. For non-overlapping `(From, On)` pairs this makes no difference — each pair matches exactly one arm. If you accidentally declare two transitions with the same `(From, On)`, the switch will always take the first one; the second is dead code.

---

## Exit and entry order

For each firing transition:

1. `OnExit{From}(trigger)` — called before the state changes
2. `_state = to` — state written
3. `OnEnter{To}(from)` — called after the state changes

Hooks see a consistent snapshot: `OnExit` fires while `Current` is still the old state; `OnEnter` fires while `Current` is already the new state.

---

## Graph validation

The generator validates the declared graph and emits diagnostics:

| Diagnostic | Condition |
|------------|-----------|
| ZSM0001 | A `From` state has no incoming edges and is not `InitialState` |
| ZSM0002 | A `To` state (or `InitialState`) has no outgoing edges and is not `[Terminal]` |
