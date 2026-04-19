---
id: entry-exit-actions
title: Entry and Exit Actions
sidebar_position: 2
---

# Entry and Exit Actions

Entry and exit actions let you observe — and react to — every state crossing without polluting the transition declaration. They are implemented as `partial void` methods, so unimplemented stubs compile away entirely.

---

## Generated stubs

For every unique `From` state in your transitions the generator emits:

```csharp
partial void OnExit{StateName}(TTrigger on);
```

For every unique `To` state:

```csharp
partial void OnEnter{StateName}(TState from);
```

Example — given:

```csharp
[Transition<State, Trigger>(From = State.Idle,    On = Trigger.Submit, To = State.Pending)]
[Transition<State, Trigger>(From = State.Pending, On = Trigger.Pay,   To = State.Done)]
```

The generated stubs are:

```csharp
partial void OnExitIdle(Trigger on);       // Idle appears as From
partial void OnExitPending(Trigger on);    // Pending appears as From
partial void OnEnterPending(State from);   // Pending appears as To
partial void OnEnterDone(State from);      // Done appears as To
```

---

## Implementing hooks

Add your implementations to the other part of the `partial` class:

```csharp
public partial class OrderMachine
{
    partial void OnExitIdle(Trigger on)
        => Console.WriteLine($"Order submitted, trigger was {on}");

    partial void OnEnterPending(State from)
        => _logger.LogInformation("Order entered Pending from {From}", from);

    partial void OnEnterDone(State from)
    {
        _metrics.RecordOrderComplete();
        _emailService.SendConfirmation(OrderId);
    }
}
```

Leave any stub you do not need — the compiler removes empty stubs at zero cost.

---

## Execution order

For a transition `A → B` on trigger `T`:

1. `OnExitA(T)` — fires while `Current` is still `A`
2. `_state = B` — state field written
3. `OnEnterB(A)` — fires while `Current` is already `B`

This ordering means:
- `OnExit` can safely read `Current` and see the old state.
- `OnEnter` can safely read `Current` and see the new state.
- The `from` parameter in `OnEnter` tells you where you came from without needing to store it yourself.

---

## The `from` and `on` parameters

| Hook | `on` param | `from` param |
|------|-----------|-------------|
| `OnExit{State}(TTrigger on)` | The trigger that caused the exit | — |
| `OnEnter{State}(TState from)` | — | The state that was just left |

Use `from` in `OnEnter` to implement context-dependent entry logic:

```csharp
partial void OnEnterPending(State from)
{
    if (from == State.Idle)
        _auditLog.RecordNewOrder(OrderId);
    else
        _auditLog.RecordResubmit(OrderId); // came back from a failed payment attempt
}
```

---

## Self-loops

If a state transitions to itself, both hooks fire:

```csharp
[Transition<State, Trigger>(From = State.Active, On = Trigger.Refresh, To = State.Active)]
```

`OnExitActive` fires first (with `on = Trigger.Refresh`), then `_state = Active`, then `OnEnterActive` fires (with `from = State.Active`). This is useful for counting refresh calls or resetting a timeout.

---

## Hooks in concurrent mode

In concurrent mode, hooks fire **after** the CAS succeeds, outside any synchronisation. Two consequences:

1. Multiple threads may be inside hooks simultaneously.
2. `Current` may have advanced to a third state by the time a hook runs on another thread.

Design hooks to be thread-safe or idempotent, and rely on the `from`/`on` parameters rather than re-reading `Current`.

---

## Async in hooks

`partial void` methods are synchronous. If you need async work (e.g. sending a message to a channel), use fire-and-forget with `_ = Task.Run(...)`, or enqueue to a `Channel<T>` and process asynchronously:

```csharp
partial void OnEnterShipped(State from)
    => _outbox.Enqueue(new OrderShippedEvent(OrderId));
```

Avoid `async void` — it swallows exceptions. Prefer a synchronous enqueue that an async consumer processes.

---

## Testing hooks

See [Testing](../testing.md#testing-entry-and-exit-hooks) for patterns to observe hook invocations in unit tests.
