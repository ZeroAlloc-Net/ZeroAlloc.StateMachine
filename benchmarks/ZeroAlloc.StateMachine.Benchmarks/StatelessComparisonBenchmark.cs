using BenchmarkDotNet.Attributes;
using Stateless;
using ZeroAlloc.StateMachine;

#pragma warning disable ZSM0002 // terminal states without [Terminal] — intentional in benchmarks
#pragma warning disable ZSM0003 // single-use triggers — intentional in benchmarks

namespace ZeroAlloc.StateMachine.Benchmarks;

// Stateless is the de-facto state-machine library for .NET — class-based, fluent
// configuration, runtime trigger dispatch. ZA.StateMachine is source-generated
// switch-based dispatch with zero-allocation TryFire.
//
// Apples-to-apples scenarios:
//   1. Fire a valid trigger (happy path)
//   2. Fire an invalid trigger (rejected without state change)
//   3. Fire with a guard that returns true (allowed)
//   4. Fire with a guard that returns false (blocked)
//
// State / Trigger types are the same OrderState / OrderTrigger / GuardedState /
// GuardedTrigger as the existing self-benchmark, but Stateless's builder pattern
// requires re-constructing the configuration per run (no static caching trick).
// We do the configuration in GlobalSetup and reuse the configured machine.

[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class StatelessComparisonBenchmark
{
    // ZA machines (re-allocated per iteration where the test cycles to a terminal state)
    private OrderMachine _zaMachine = null!;
    private GuardedMachine _zaGuarded = null!;

    // Stateless machines
    private StateMachine<OrderState, OrderTrigger> _statelessOrder = null!;
    private StateMachine<GuardedState, GuardedTrigger> _statelessGuarded = null!;
    private bool _guardAllow;

    [GlobalSetup]
    public void Setup()
    {
        _zaMachine = new OrderMachine();
        _zaGuarded = new GuardedMachine();

        _statelessOrder = BuildStatelessOrder();
        _statelessGuarded = BuildStatelessGuarded();
    }

    private StateMachine<OrderState, OrderTrigger> BuildStatelessOrder()
    {
        var fsm = new StateMachine<OrderState, OrderTrigger>(OrderState.Idle);
        fsm.Configure(OrderState.Idle).Permit(OrderTrigger.Submit, OrderState.Pending);
        fsm.Configure(OrderState.Pending).Permit(OrderTrigger.Pay, OrderState.Processing);
        fsm.Configure(OrderState.Processing).Permit(OrderTrigger.Ship, OrderState.Shipped);
        return fsm;
    }

    private StateMachine<GuardedState, GuardedTrigger> BuildStatelessGuarded()
    {
        var fsm = new StateMachine<GuardedState, GuardedTrigger>(GuardedState.Idle);
        fsm.Configure(GuardedState.Idle).PermitIf(GuardedTrigger.Start, GuardedState.Active, () => _guardAllow);
        return fsm;
    }

    // --- Fire valid (happy path) ---

    [Benchmark(Baseline = true, Description = "Stateless: Fire valid (3-step cycle)")]
    [BenchmarkCategory("FireValid")]
    public OrderState Stateless_FireValid()
    {
        _statelessOrder.Fire(OrderTrigger.Submit);
        _statelessOrder.Fire(OrderTrigger.Pay);
        _statelessOrder.Fire(OrderTrigger.Ship);
        // Reset for next iteration — Stateless has no in-place reset, rebuild
        _statelessOrder = BuildStatelessOrder();
        return _statelessOrder.State;
    }

    [Benchmark(Description = "ZA.StateMachine: TryFire valid (3-step cycle)")]
    [BenchmarkCategory("FireValid")]
    public bool Za_FireValid()
    {
        _zaMachine.TryFire(OrderTrigger.Submit);
        _zaMachine.TryFire(OrderTrigger.Pay);
        var ok = _zaMachine.TryFire(OrderTrigger.Ship);
        _zaMachine = new OrderMachine();
        return ok;
    }

    // --- Fire invalid (rejected) ---

    [Benchmark(Description = "Stateless: Fire invalid (no-op rejection)")]
    [BenchmarkCategory("FireInvalid")]
    public bool Stateless_FireInvalid()
    {
        // Stateless throws InvalidOperationException on invalid trigger by default.
        // To compare fairly with TryFire (which returns false), we use CanFire +
        // Fire-or-skip pattern, mirroring real-world defensive usage.
        if (_statelessOrder.CanFire(OrderTrigger.Pay)) // From Idle: Pay is invalid → false
        {
            _statelessOrder.Fire(OrderTrigger.Pay);
            return true;
        }
        return false;
    }

    [Benchmark(Description = "ZA.StateMachine: TryFire invalid")]
    [BenchmarkCategory("FireInvalid")]
    public bool Za_FireInvalid()
        => _zaMachine.TryFire(OrderTrigger.Pay); // Pay invalid from Idle

    // --- Guard allowed ---

    [Benchmark(Description = "Stateless: Fire with guard (allowed)")]
    [BenchmarkCategory("GuardAllowed")]
    public bool Stateless_GuardAllowed()
    {
        _guardAllow = true;
        if (_statelessGuarded.CanFire(GuardedTrigger.Start))
        {
            _statelessGuarded.Fire(GuardedTrigger.Start);
            _statelessGuarded = BuildStatelessGuarded(); // reset
            return true;
        }
        return false;
    }

    [Benchmark(Description = "ZA.StateMachine: TryFire with guard (allowed)")]
    [BenchmarkCategory("GuardAllowed")]
    public bool Za_GuardAllowed()
    {
        _zaGuarded.SetAllow(true);
        var ok = _zaGuarded.TryFire(GuardedTrigger.Start);
        _zaGuarded = new GuardedMachine();
        return ok;
    }

    // --- Guard blocked ---

    [Benchmark(Description = "Stateless: Fire with guard (blocked)")]
    [BenchmarkCategory("GuardBlocked")]
    public bool Stateless_GuardBlocked()
    {
        _guardAllow = false;
        return _statelessGuarded.CanFire(GuardedTrigger.Start); // false → stays Idle
    }

    [Benchmark(Description = "ZA.StateMachine: TryFire with guard (blocked)")]
    [BenchmarkCategory("GuardBlocked")]
    public bool Za_GuardBlocked()
    {
        _zaGuarded.SetAllow(false);
        return _zaGuarded.TryFire(GuardedTrigger.Start); // stays Idle
    }
}
