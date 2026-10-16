# ZeroAlloc.StateMachine

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.StateMachine.svg)](https://www.nuget.org/packages/ZeroAlloc.StateMachine)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

Source-generated finite state machines for .NET, a compile-time alternative to Stateless: transitions
are a generated `switch`, 0 B per transition and NativeAOT-ready.

## Why ZeroAlloc.StateMachine

- **0 B per transition, asserted in CI.** `TryFire` allocates nothing on valid, invalid, guarded and
  concurrent transitions; `AllocationTests` fails CI if it does. Stateless allocates 24 B just
  to reject an invalid trigger.
- **About 15× faster than Stateless.** Rejecting an invalid trigger takes 1.6 ns against
  23 ns in the same run, and a three-step cycle with reset is about 300× faster.
- **Works under NativeAOT and trimming.** No reflection; every PR publishes an AOT sample with
  `PublishAot=true`.
- **Compile-time diagnostics instead of runtime surprises.** An unreachable state, a state with no way
  out or a likely trigger typo is reported at build time, from `ZSM0001` to `ZSM0026`.

## Install

```bash
dotnet add package ZeroAlloc.StateMachine
```

## Quick start

```csharp
using ZeroAlloc.StateMachine;

var order = new OrderMachine();
Console.WriteLine(order.TryFire(Trigger.Submit)); // True: Idle -> Pending
Console.WriteLine(order.Current);                 // Pending
Console.WriteLine(order.TryFire(Trigger.Pay));    // True: Pending -> Done
Console.WriteLine(order.TryFire(Trigger.Submit)); // False: Done is terminal

public enum State { Idle, Pending, Done }
public enum Trigger { Submit, Pay }

[StateMachine(InitialState = nameof(State.Idle))]
[Transition<State, Trigger>(From = State.Idle, On = Trigger.Submit, To = State.Pending)]
[Transition<State, Trigger>(From = State.Pending, On = Trigger.Pay, To = State.Done)]
[Terminal<State>(State = State.Done)]
public partial class OrderMachine
{
    partial void OnEnterDone(State from) => Console.WriteLine($"Order complete, came from {from}");
}
```

The generator writes `TryFire` as a `switch` over `(State, Trigger)` and calls the `OnEnter…` and
`OnExit…` hooks you implement.

## What you get

- **Guards** that block a transition at runtime through a generated `partial bool` method.
- **Entry and exit hooks** per state.
- **Concurrent mode**: lock-free, thread-safe transitions with one attribute property.
- **Struct machines** that keep even the machine itself off the heap.
- **Composite and history states**, **timeout transitions**, and **concurrent parts** that host several
  machines in one class.
- **Mermaid diagram export** as a generated constant.

Every attribute and property is in the [attribute reference](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/attributes.md).

## Performance

BenchmarkDotNet, same run against [Stateless](https://github.com/dotnet-state-machine/stateless)
5.20.1. Allocations are exact.

| Operation | Speed-up vs Stateless | Stateless alloc | ZA alloc |
|---|---:|---:|---:|
| Fire valid, 3-step cycle with reset | ~300× | 7,600 B | 0 B |
| Fire invalid | ~15× | 24 B | 0 B |
| Guard blocked | > 400× | 768 B | 0 B |

The cycle row includes each library's reset: a field write for ZeroAlloc, a fluent reconfiguration for
Stateless. The guard row is a lower bound, because ZeroAlloc's time is below what BenchmarkDotNet can
measure. Full numbers and methodology: [docs/performance.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/performance.md).

## Learn more

- Documentation: [statemachine.zeroalloc.net](https://statemachine.zeroalloc.net), including
  [getting started](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/getting-started.md), [guards](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/guides/guards.md),
  [concurrent mode](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/core-concepts/concurrent-mode.md), the
  [generated code](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/source-generator.md) and [diagnostics](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/docs/index.md#diagnostics).
- Related packages: [ZeroAlloc.Saga](https://github.com/ZeroAlloc-Net/ZeroAlloc.Saga) and
  [ZeroAlloc.Scheduling](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling), both built on it.
- Contributing: issues and pull requests are welcome on [GitHub](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/issues).
- [CHANGELOG](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/CHANGELOG.md).

## License

MIT. See [LICENSE](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/blob/main/LICENSE).
