---
id: guards
title: Guards
sidebar_position: 1
---

# Guards

A **guard** is a runtime condition that can block a transition even when the trigger is valid. Without a guard, any `TryFire(trigger)` call that matches a `(From, On)` pair always fires. With a guard, the transition fires only if both the pair matches *and* the guard returns `true`.

---

## Enabling a guard

Set `When = true` on the transition:

```csharp
[Transition<OrderState, Trigger>(
    From = OrderState.Pending,
    On   = Trigger.Pay,
    To   = OrderState.Paid,
    When = true)]
public partial class OrderMachine { }
```

The generator emits a `when` clause in the switch arm and adds a `private partial bool` stub:

```csharp
// Generated:
(OrderState.Pending, Trigger.Pay)
    when GuardPay(OrderState.Pending, Trigger.Pay)
    => Fire(OrderState.Pending, OrderState.Paid, trigger),

private partial bool GuardPay(OrderState from, Trigger on);
```

---

## Implementing the guard

Add the implementing declaration to the other part of your `partial` class:

```csharp
public partial class OrderMachine
{
    public decimal Balance { get; set; }

    private partial bool GuardPay(OrderState from, Trigger on)
        => Balance >= RequiredAmount;
}
```

If you leave the stub unimplemented, the compiler emits **CS8795** — a useful reminder that you have an unenforced guard.

---

## Guard method signature

The generated stub always has this shape:

```
private partial bool Guard{TriggerName}(TState from, TTrigger on);
```

- `{TriggerName}` is the name of the trigger enum value (e.g. `Pay` → `GuardPay`).
- `from` is the source state.
- `on` is the trigger that was fired.
- Return `true` to allow the transition; `false` to block it.

---

## Multiple guarded transitions

You can guard multiple transitions independently. Each guard is a separate partial method named after its trigger:

```csharp
[Transition<State, Trigger>(From = State.Pending, On = Trigger.Pay,    To = State.Paid,      When = true)]
[Transition<State, Trigger>(From = State.Paid,    On = Trigger.Ship,   To = State.Shipped,   When = true)]
[Transition<State, Trigger>(From = State.Idle,    On = Trigger.Submit, To = State.Pending)]   // no guard
public partial class OrderMachine
{
    private partial bool GuardPay(State from, Trigger on)  => HasBalance;
    private partial bool GuardShip(State from, Trigger on) => HasShippingAddress;
}
```

---

## Guards and return value

When a guard blocks a transition, `TryFire` returns `false` — the same as when no matching transition exists. The caller cannot distinguish between "wrong state" and "guard blocked". If you need to distinguish them, check `Current` and `HasSufficientBalance` independently before firing.

---

## Guards in concurrent mode

Guards are **silently ignored** when `Concurrent = true`. The generator does not emit guard stubs or `when` clauses in concurrent mode — the TOCTOU risk makes guards fundamentally unsafe in that context (the guard could pass, then another thread could change the relevant condition before the CAS fires). See [Concurrent Mode](../core-concepts/concurrent-mode.md#guards-and-concurrent-mode) for the reason and alternatives.

---

## Pattern: injected dependency in guard

Guards run in the context of the machine instance. You can inject dependencies through the constructor:

```csharp
public partial class OrderMachine
{
    private readonly IInventoryService _inventory;

    public OrderMachine(IInventoryService inventory)
        => _inventory = inventory;

    private partial bool GuardShip(State from, Trigger on)
        => _inventory.IsInStock(OrderId);
}
```

Register the machine with DI as `Transient` or `Scoped` so each instance gets its own dependency.

---

## Pattern: encode guard state as machine state

Sometimes it is cleaner to encode the guard condition as a separate state rather than a runtime check. For example, instead of:

```csharp
// Guard checks if balance is available
[Transition<State, Trigger>(From = State.Pending, On = Trigger.Pay, To = State.Paid, When = true)]
```

Consider:

```csharp
public enum State { Idle, PendingUnfunded, PendingFunded, Paid }

[Transition<State, Trigger>(From = State.PendingUnfunded, On = Trigger.Fund, To = State.PendingFunded)]
[Transition<State, Trigger>(From = State.PendingFunded,   On = Trigger.Pay,  To = State.Paid)]
```

This approach makes the guard condition visible in the state graph, which can make the machine easier to reason about and test — and avoids the concurrent-mode restriction.
