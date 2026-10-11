---
id: performance
title: Performance
sidebar_position: 7
---

# Performance

ZeroAlloc.StateMachine is designed so that `TryFire` allocates **zero bytes** on the managed heap on every call. All benchmarks are measured with [BenchmarkDotNet](https://benchmarkdotnet.org/) (v0.15.8, .NET 10.0.12, Release mode, i9-12900HK).

## Head-to-head vs Stateless

<!-- BENCH:START -->
_Last refreshed: 2026-10-11_

[Stateless](https://github.com/dotnet-state-machine/stateless) is the de-facto state-machine library in .NET — class-based, fluent configuration, runtime trigger dispatch. ZA's source-generated switch dispatch dominates every comparable scenario.

| Operation | Stateless | ZA.StateMachine | Speedup |
|---|---:|---:|---:|
| Fire valid (3-step cycle) | 4,476 ns / 7,600 B | **15 ns / 0 B** | **~300× faster, 0 B alloc** |
| Fire invalid | 23 ns / 24 B | **1.6 ns / 0 B** | **~15× faster, 0 B alloc** |
| Guard allowed | 2,430 ns / 4,336 B | **< 1 ns / 0 B** | **> 1,000× faster, 0 B alloc** |
| Guard blocked | 422 ns / 768 B | **< 1 ns / 0 B** | **> 400× faster, 0 B alloc** |

ZA's rows allocate nothing: the benchmark puts the machine back in its initial state with the generated `Reset()`, which writes the state field in place. Rows under 1 ns are below what BenchmarkDotNet can tell apart from an empty method, so their speedups are lower bounds. The Stateless row's 7,600 B is two things combined: each `Fire` walks a `Dictionary<TTrigger, StateRepresentation>` and constructs trigger/transition info objects, **and** the per-iteration `BuildStatelessOrder()` reset rebuilds the configuration (`new StateMachine<,>(initial)` plus three `Configure().Permit()` calls — each one allocating dictionary entries).

This is the apples-to-apples comparison for cyclic state machines — a per-request handler in a web app, or a fresh circuit-breaker per stream. Both libraries pay a "reset" cost in this workload. ZA's reset is a single field write because configuration is resolved at compile time; Stateless's includes the full fluent-configuration rebuild because configuration is runtime. The dispatch portion alone (excluding the rebuild) is roughly an order of magnitude smaller on the Stateless side but still measurably slower than ZA's `switch`-expression `TryFire`.
<!-- BENCH:END -->

## Self-benchmark

| Benchmark | Mean | Allocated |
|---|---:|---:|
| TryFire – valid (non-concurrent) | 8.1 ns | 0 B |
| TryFire – invalid (non-concurrent) | 1.9 ns | 0 B |
| TryFire – guard allowed (non-concurrent) | 1.0 ns | 0 B |
| TryFire – guard blocked (non-concurrent) | ~0 ns ‡ | 0 B |
| TryFire – CAS, no contention (concurrent) | 37.5 ns | 0 B |

The valid and guard-allowed benchmarks end in a terminal state, so each operation resets the machine with the generated `Reset()`. That is a field write and allocates nothing, so every row shows what `TryFire` itself costs. Timings this short move by a few nanoseconds between runs.

‡ BenchmarkDotNet reports a `ZeroMeasurement` warning — the blocked-guard path is too fast to measure accurately. The duration is indistinguishable from an empty method call.

## What drives each result

**Valid transition (non-concurrent)** — hits the `(State, Trigger) switch` arm, calls `Fire` (state field write + `OnExit`/`OnEnter` dispatch), returns `true`. The benchmark cycles through all three transitions and resets the machine in place with `Reset()`, so nothing is allocated.

**Invalid trigger** — falls through to the `_ => false` default arm immediately. No state write, no hook dispatch. At 1.4 ns it is close to the minimum measurable overhead of a switch expression.

**Guard allowed** — same path as a valid transition, plus one additional virtual-free partial method call (the guard predicate). Allocates nothing; the benchmark resets in place, as in the valid case.

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
