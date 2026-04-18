---
id: performance
title: Performance
sidebar_position: 7
---

# Performance

ZeroAlloc.StateMachine is designed so that `TryFire` allocates **zero bytes** on the managed heap on every call. All benchmarks are measured with [BenchmarkDotNet](https://benchmarkdotnet.org/) (v0.14.0, .NET 10, Release mode).

## Results

| Benchmark | Mean | Allocated |
|---|---:|---:|
| TryFire – valid (non-concurrent) | 19.7 ns | 24 B † |
| TryFire – invalid (non-concurrent) | 1.4 ns | 0 B |
| TryFire – guard allowed (non-concurrent) | 8.3 ns | 24 B † |
| TryFire – guard blocked (non-concurrent) | ~0 ns ‡ | 0 B |
| TryFire – CAS, no contention (concurrent) | 38.3 ns | 0 B |

† The 24 B allocation comes from `new OrderMachine()` in the benchmark reset path, **not** from `TryFire` itself. `TryFire` allocates 0 bytes; the constructor allocates one object per full cycle.

‡ BenchmarkDotNet reports a `ZeroMeasurement` warning — the blocked-guard path is too fast to measure accurately. The duration is indistinguishable from an empty method call.

## What drives each result

**Valid transition (non-concurrent)** — hits the `(State, Trigger) switch` arm, calls `Fire` (state field write + `OnExit`/`OnEnter` dispatch), returns `true`. The benchmark also resets the machine via `new OrderMachine()` each iteration, which accounts for the 24 B.

**Invalid trigger** — falls through to the `_ => false` default arm immediately. No state write, no hook dispatch. At 1.4 ns it is close to the minimum measurable overhead of a switch expression.

**Guard allowed** — same path as a valid transition, plus one additional virtual-free partial method call (the guard predicate). Allocates 24 B for the reset constructor, same as the valid case.

**Guard blocked** — the guard predicate returns `false`, so the switch arm is selected but `Fire` is never called. No state write, no allocation. Duration is in the noise floor.

**Concurrent, no contention** — uses `Volatile.Read` + `Interlocked.CompareExchange`. The CAS succeeds on the first attempt (single caller), so there is no spin. The additional overhead vs. the non-concurrent path comes entirely from the memory-fence semantics of `Volatile.Read` and `CompareExchange`. Allocates 0 bytes — the nullable `TState?` intermediate is a value-type stack slot.

## Running the benchmarks yourself

```bash
cd benchmarks/ZeroAlloc.StateMachine.Benchmarks
dotnet run -c Release
```

BenchmarkDotNet will print a full results table including median, standard deviation, and the allocation column from `[MemoryDiagnoser]`.

To run a specific benchmark:

```bash
dotnet run -c Release --filter "*TryFire_Valid*"
```

## Design invariants

- **No boxing** — state and trigger are `enum` values stored as their underlying integer type. The concurrent path stores them as `long` to satisfy `Interlocked.CompareExchange`; the cast is a no-op at the CPU level.
- **No closures, no delegates** — `TryFire` is a plain switch expression. Nothing is captured, nothing is allocated.
- **No LINQ on the hot path** — the generated `switch` is a compile-time constant. All branching is resolved by the JIT to a direct jump table where the enum range permits it.
- **Struct machines** — declaring a `partial struct` instead of `partial class` eliminates the heap allocation for the machine itself. Useful for embedded/pooling scenarios. (Concurrent mode is not available on structs; the compiler will emit ZSM0004.)
