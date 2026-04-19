---
id: states-and-triggers
title: States and Triggers
sidebar_position: 1
---

# States and Triggers

A state machine models a system that is always in exactly one *state*, and moves between states in response to *triggers* (also called events or inputs).

---

## States

A **state** represents a distinct mode of being. In ZeroAlloc.StateMachine, states are values of an `enum` type:

```csharp
public enum OrderState
{
    Idle,      // waiting for submission
    Pending,   // submitted, waiting for payment
    Paid,      // payment received
    Shipped,   // package dispatched
    Cancelled  // order cancelled
}
```

### Rules for state enums

- Any `enum` with an underlying integer type works (`int`, `byte`, `long`, etc.).
- The name of the initial state is passed as a `string` to `[StateMachine(InitialState = nameof(...))]`. Use `nameof` — it is validated at compile time.
- State values do not need to be contiguous or start at zero, but dense ranges produce better jump-table output from the JIT.
- The generator warns (ZSM0001) when a state appears as a `From` but nothing leads to it — a sign that a transition was forgotten.
- The generator warns (ZSM0002) when a state has no outgoing transitions — either mark it `[Terminal]` or add the missing edge.

---

## Triggers

A **trigger** is an event or signal that may cause a state transition. Triggers are also `enum` values:

```csharp
public enum OrderTrigger
{
    Submit,
    Pay,
    Ship,
    Cancel
}
```

### Rules for trigger enums

- Same rules as state enums — any integer-backed enum works.
- A trigger is only meaningful when paired with a `From` state in a `[Transition]` attribute. The same trigger value can appear in multiple transitions (different source states).
- Firing a trigger that has no matching transition from the current state returns `false` and leaves the state unchanged.

---

## The machine always has a state

`Current` is never `null` or undefined. The machine starts in `InitialState` and transitions deterministically. Concurrent reads are safe in concurrent mode (see [Concurrent Mode](concurrent-mode.md)).

```csharp
var machine = new OrderMachine();
machine.Current; // OrderState.Idle — always valid
```

---

## Separation from the machine class

States and triggers are intentionally separate from the machine class. This lets you:

- Share the same enum pair across multiple machine types
- Use existing domain enums without creating new types
- Compose machines that handle the same trigger differently based on their role

```csharp
// Both machines use the same enums
[StateMachine(InitialState = nameof(OrderState.Idle))]
public partial class OrderMachine { }

[StateMachine(InitialState = nameof(OrderState.Idle))]
public partial class DraftOrderMachine { }  // different transitions, same enums
```

---

## Choosing enum values

Use descriptive names that read well in code. A common convention:

- **States** — nouns or adjectives: `Idle`, `Running`, `Done`, `Failed`
- **Triggers** — verbs or events: `Start`, `Submit`, `Cancel`, `Timeout`

Self-documenting names make the `[Transition]` declarations readable without comments:

```csharp
[Transition<State, Trigger>(From = State.Idle, On = Trigger.Start, To = State.Running)]
// reads: "from Idle, on Start, go to Running"
```
