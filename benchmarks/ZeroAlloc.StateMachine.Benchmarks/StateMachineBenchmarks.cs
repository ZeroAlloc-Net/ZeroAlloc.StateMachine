using BenchmarkDotNet.Attributes;

namespace ZeroAlloc.StateMachine.Benchmarks;

// ── Benchmarks ────────────────────────────────────────────────────────────────

/// <summary>
/// Measures allocation and throughput of TryFire in various scenarios.
/// All benchmarks target zero allocation on the hot path.
/// </summary>
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class StateMachineBenchmarks
{
    private OrderMachine _machine = null!;
    private GuardedMachine _guarded = null!;
    private CircuitBreakerFsm _cb = null!;

    [GlobalSetup]
    public void Setup()
    {
        _machine = new OrderMachine();
        _guarded = new GuardedMachine();
        _cb      = new CircuitBreakerFsm();
    }

    // ── Non-concurrent: valid transition ─────────────────────────────────────

    /// <summary>
    /// Happy path: fire a valid trigger that advances state.
    /// Machine cycles Idle → Pending → Processing → Shipped, then resets.
    /// </summary>
    [Benchmark(Description = "TryFire – valid (non-concurrent)")]
    public bool TryFire_Valid()
    {
        // Cycle through all three transitions; reset between iterations
        _machine.TryFire(OrderTrigger.Submit);
        _machine.TryFire(OrderTrigger.Pay);
        var result = _machine.TryFire(OrderTrigger.Ship);

        // Reset by reconstructing (avoids measuring ctor cost on the hot path
        // — but for a fair reset we need a new instance each cycle, so we accept
        // that the ctor cost is included. Alternative: expose a Reset() method.)
        _machine = new OrderMachine();
        return result;
    }

    // ── Non-concurrent: invalid transition ───────────────────────────────────

    /// <summary>
    /// Fire a trigger that has no match in the current state.
    /// Exercises the <c>_ =&gt; false</c> switch default arm.
    /// </summary>
    [Benchmark(Description = "TryFire – invalid (non-concurrent)")]
    public bool TryFire_Invalid()
        // Pay is not valid from Idle — hits the default arm immediately
        => _machine.TryFire(OrderTrigger.Pay);

    // ── Non-concurrent: guard allowed ─────────────────────────────────────────

    /// <summary>Guard returns true — transition proceeds.</summary>
    [Benchmark(Description = "TryFire – guard allowed (non-concurrent)")]
    public bool TryFire_Guard_Allowed()
    {
        _guarded.SetAllow(true);
        var result = _guarded.TryFire(GuardedTrigger.Start);
        _guarded = new GuardedMachine(); // reset
        return result;
    }

    // ── Non-concurrent: guard blocked ─────────────────────────────────────────

    /// <summary>Guard returns false — transition is rejected without state change.</summary>
    [Benchmark(Description = "TryFire – guard blocked (non-concurrent)")]
    public bool TryFire_Guard_Blocked()
    {
        _guarded.SetAllow(false);
        return _guarded.TryFire(GuardedTrigger.Start); // stays Idle
    }

    // ── Concurrent: single caller (no contention) ─────────────────────────────

    /// <summary>
    /// Concurrent TryFire with a single caller — CAS succeeds on the first attempt.
    /// Measures the overhead of Volatile.Read + CompareExchange vs the non-concurrent path.
    /// </summary>
    [Benchmark(Description = "TryFire – CAS, no contention (concurrent)")]
    public bool TryFire_Concurrent_NoContention()
    {
        var result = _cb.TryFire(CbTrigger.Trip);      // Closed → Open
        _cb.TryFire(CbTrigger.Probe);                  // Open → HalfOpen
        _cb.TryFire(CbTrigger.Reset);                  // HalfOpen → Closed
        return result;
    }
}
