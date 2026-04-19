---
id: concurrent-mode
title: Concurrent Mode
sidebar_position: 3
---

# Concurrent Mode

By default, `TryFire` is **not thread-safe** — it writes `_state` without any synchronisation. This is intentional: the non-concurrent path is branchless and allocation-free, which suits the common case where a state machine is owned by a single thread or protected by an external lock.

When you genuinely need concurrent callers, set `Concurrent = true`.

---

## Enabling concurrent mode

```csharp
[StateMachine(InitialState = nameof(WorkerState.Idle), Concurrent = true)]
[Transition<WorkerState, WorkerTrigger>(From = WorkerState.Idle,    On = WorkerTrigger.Start, To = WorkerState.Running)]
[Transition<WorkerState, WorkerTrigger>(From = WorkerState.Running, On = WorkerTrigger.Stop,  To = WorkerState.Idle)]
public partial class WorkerMachine { }
```

---

## What changes

### State field

Non-concurrent uses the state enum type directly:

```csharp
private OrderState _state = OrderState.Idle;
```

Concurrent uses `long`:

```csharp
private long _state = (long)WorkerState.Idle;
```

`long` is the type accepted by `Interlocked.CompareExchange`. The cast to/from `long` is a no-op at the CPU level for any enum whose underlying type fits in 64 bits.

### `Current`

Non-concurrent: plain field read.

Concurrent: `Volatile.Read` — ensures that a thread reading `Current` sees the most recently committed value, even across CPU cores.

```csharp
public WorkerState Current
    => (WorkerState)System.Threading.Volatile.Read(ref _state);
```

### `TryFire`

Non-concurrent: direct field write inside a switch arm.

Concurrent: CAS loop:

```csharp
public bool TryFire(WorkerTrigger trigger)
{
    while (true)
    {
        var current = (WorkerState)Volatile.Read(ref _state);
        WorkerState? next = (current, trigger) switch
        {
            (WorkerState.Idle,    WorkerTrigger.Start) => (WorkerState?)WorkerState.Running,
            (WorkerState.Running, WorkerTrigger.Stop)  => (WorkerState?)WorkerState.Idle,
            _ => null
        };

        if (next is null) return false;

        if (Interlocked.CompareExchange(ref _state, (long)next.Value, (long)current) == (long)current)
        {
            OnExit(current, trigger);
            OnEnter(next.Value, current);
            return true;
        }
        // Lost race — another thread changed _state between our read and CAS.
        // Loop and retry with the fresh value.
    }
}
```

The CAS is atomic: exactly one thread "wins" the race when multiple callers fire the same trigger simultaneously from the same state.

---

## Guarantees

| Guarantee | Detail |
|-----------|--------|
| Exactly-once transition | Only one caller's CAS succeeds; losers retry or return `false` |
| No torn reads | `Volatile.Read` gives a coherent `long` on all architectures |
| No deadlocks | No locks are held — pure CAS |
| Allocation-free | The `WorkerState?` intermediate is a value-type stack slot |

---

## Hook ordering in concurrent mode

Hooks (`OnExit*`, `OnEnter*`) fire **after** the CAS succeeds, outside any lock. Two important consequences:

1. **Hooks are not synchronised against each other.** If two transitions fire in rapid succession on different threads, their hooks may interleave. Design hooks to be either idempotent or externally synchronised.

2. **`Current` may have advanced** by the time a hook runs. Inside `OnExitRunning`, another thread might have already moved the machine to a third state. Read `from` and `trigger` — not `Current` — to know what just happened.

---

## Guards and concurrent mode

Guards are **not generated** when `Concurrent = true`. The generator silently omits guard stubs and `when` clauses in concurrent mode.

The reason is a classic TOCTOU race: checking a guard and then performing a CAS are two separate operations. Between the guard check and the CAS, another thread could change any condition the guard depends on, making the guard result stale by the time the transition fires. Emitting a guard that is inherently broken would be worse than omitting it silently.

**Alternatives:**

- Use an external lock (makes the machine non-concurrent in practice).
- Encode the guard condition as a state: split `Pending` into `PendingWithBalance` and `PendingWithoutBalance` and only add the `Pay` transition from `PendingWithBalance`.
- Move the guard check to the caller and only call `TryFire` when the condition holds.

---

## Struct machines and concurrent mode

`Concurrent = true` on a `partial struct` is an error (ZSM0004). A struct stored on the stack or copied by value cannot provide the stable `ref _state` location that `Interlocked.CompareExchange` requires. Use a `partial class` for concurrent machines.

---

## Performance

See the [Performance](../performance.md) page for benchmark numbers. The concurrent path is roughly 2× slower than the non-concurrent path due to the memory fence, but still allocates zero bytes.

---

## When to use concurrent mode

Use concurrent mode when:
- The machine is registered as a **singleton** and called from multiple threads (e.g. a circuit breaker, a connection pool manager).
- The machine is held in a shared data structure accessed from a thread pool.

Do **not** use concurrent mode when:
- The machine is scoped to a single request or a single thread.
- You already protect access with a lock or channel.
- You need guards — use non-concurrent with an external lock instead.
