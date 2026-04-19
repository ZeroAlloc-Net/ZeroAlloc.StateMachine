---
id: getting-started
title: Getting Started
sidebar_position: 2
---

# Getting Started

This guide takes you from zero to a working state machine in about five minutes.

---

## 1. Install

```bash
dotnet add package ZeroAlloc.StateMachine
```

The package ships the runtime attributes and the Roslyn source generator as a single NuGet. No separate analyzer package is needed.

---

## 2. Define your enums

State machines need two enums: one for states, one for triggers (events that cause transitions).

```csharp
public enum OrderState
{
    Idle,
    Pending,
    Paid,
    Shipped,
    Cancelled
}

public enum OrderTrigger
{
    Submit,
    Pay,
    Ship,
    Cancel
}
```

Enums can live anywhere — same file, separate file, nested inside a class.

---

## 3. Annotate a partial class

Add `[StateMachine]` to set the initial state, then stack one `[Transition]` attribute per edge.

```csharp
using ZeroAlloc.StateMachine;

[StateMachine(InitialState = nameof(OrderState.Idle))]
[Transition<OrderState, OrderTrigger>(From = OrderState.Idle,    On = OrderTrigger.Submit, To = OrderState.Pending)]
[Transition<OrderState, OrderTrigger>(From = OrderState.Pending, On = OrderTrigger.Pay,    To = OrderState.Paid)]
[Transition<OrderState, OrderTrigger>(From = OrderState.Paid,    On = OrderTrigger.Ship,   To = OrderState.Shipped)]
[Transition<OrderState, OrderTrigger>(From = OrderState.Idle,    On = OrderTrigger.Cancel, To = OrderState.Cancelled)]
[Transition<OrderState, OrderTrigger>(From = OrderState.Pending, On = OrderTrigger.Cancel, To = OrderState.Cancelled)]
[Terminal<OrderState>(State = OrderState.Shipped)]
[Terminal<OrderState>(State = OrderState.Cancelled)]
public partial class OrderMachine { }
```

The class must be `partial` — the generator fills in the other half.

---

## 4. Use it

```csharp
var order = new OrderMachine();

Console.WriteLine(order.Current);          // OrderState.Idle

order.TryFire(OrderTrigger.Submit);        // true
Console.WriteLine(order.Current);          // OrderState.Pending

order.TryFire(OrderTrigger.Pay);           // true
Console.WriteLine(order.Current);          // OrderState.Paid

order.TryFire(OrderTrigger.Submit);        // false — no transition from Paid on Submit
Console.WriteLine(order.Current);          // OrderState.Paid — unchanged
```

`TryFire` returns `true` if the transition fired, `false` if no matching edge exists (or a guard blocked it). It never throws.

---

## 5. Add entry/exit hooks (optional)

The generator emits `partial void` stubs for every state that appears as a `From` (exit) or `To` (enter) in your transitions. Implement the ones you care about:

```csharp
public partial class OrderMachine
{
    partial void OnExitIdle(OrderTrigger on)
        => Console.WriteLine($"Order submitted via {on}");

    partial void OnEnterPaid(OrderState from)
        => Console.WriteLine($"Payment received, was in {from}");

    partial void OnEnterShipped(OrderState from)
        => Console.WriteLine("Package is on its way!");
}
```

Unimplemented stubs compile away to nothing — zero overhead.

---

## 6. Add a guard (optional)

Guards let you block a transition at runtime. Set `When = true` on a transition to get a generated `Guard{Trigger}` partial stub:

```csharp
[Transition<OrderState, OrderTrigger>(
    From = OrderState.Pending,
    On   = OrderTrigger.Pay,
    To   = OrderState.Paid,
    When = true)]
public partial class OrderMachine
{
    public decimal BalanceDue { get; set; }

    // The generator emits: private partial bool GuardPay(OrderState from, OrderTrigger on);
    private partial bool GuardPay(OrderState from, OrderTrigger on)
        => BalanceDue <= 0;
}
```

If `GuardPay` returns `false`, `TryFire(OrderTrigger.Pay)` returns `false` and the state does not change.

---

## 7. Dependency injection

State machines are plain classes. Register them with your DI container the same way as any other service:

```csharp
// Transient — each consumer gets its own machine
services.AddTransient<OrderMachine>();

// Scoped — one machine per HTTP request
services.AddScoped<OrderMachine>();
```

For use-cases where the machine is shared across threads, enable concurrent mode:

```csharp
[StateMachine(InitialState = nameof(OrderState.Idle), Concurrent = true)]
// ... transitions ...
public partial class OrderMachine { }

// Register as singleton — thread-safe
services.AddSingleton<OrderMachine>();
```

---

## What's generated

The generator emits a single file named `{ClassName}.StateMachine.g.cs` in your project's intermediate output folder. You can inspect it in Visual Studio by expanding the **Analyzers** node in Solution Explorer, or via the **Go to Definition** command on any generated member.

See [Source Generator](source-generator.md) for the full annotated output.

---

## Next steps

| Topic | Link |
|-------|------|
| All attribute properties | [Attribute Reference](attributes.md) |
| Concurrent / thread-safe machines | [Concurrent Mode](core-concepts/concurrent-mode.md) |
| Guards in depth | [Guards](guides/guards.md) |
| Entry and exit hooks | [Entry/Exit Actions](guides/entry-exit-actions.md) |
| Terminal states | [Terminal States](guides/terminal-states.md) |
| Real-world example (circuit breaker) | [Circuit Breaker](guides/circuit-breaker.md) |
| Compiler warnings | [Diagnostics](diagnostics/ZSM0001.md) |
