using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.StateMachine.Tests;

// TryFire and the generated Reset allocate nothing, on every path the benchmarks measure.
// The machines here have no hooks that allocate, so any byte counted comes from generated code.
public class AllocationTests
{
    private const int Iterations = 10_000;

    [Fact]
    public void TryFire_ValidCycle_WithReset_AllocatesNothing()
    {
        var machine = new GatedOrderMachine();
        machine.SetAllow(true);
        AllocationGate.AssertBudget(0, Iterations, () =>
        {
            machine.TryFire(GatedTrigger.Submit);
            machine.TryFire(GatedTrigger.Pay);
            machine.TryFire(GatedTrigger.Ship);
            machine.Reset();
        }, "TryFire valid cycle + Reset");
        machine.Current.Should().Be(GatedState.Idle);
    }

    [Fact]
    public void TryFire_Invalid_AllocatesNothing()
    {
        var machine = new GatedOrderMachine();
        AllocationGate.AssertBudget(0, Iterations, () => machine.TryFire(GatedTrigger.Pay), "TryFire invalid");
        machine.Current.Should().Be(GatedState.Idle);
    }

    [Fact]
    public void TryFire_GuardAllowed_WithReset_AllocatesNothing()
    {
        // The README's "Guard allowed" row: the guarded Submit transition fires, then Reset.
        var machine = new GatedOrderMachine();
        machine.SetAllow(true);
        AllocationGate.AssertBudget(0, Iterations, () =>
        {
            machine.TryFire(GatedTrigger.Submit);
            machine.Reset();
        }, "TryFire guard allowed + Reset");
        machine.Current.Should().Be(GatedState.Idle);
    }

    [Fact]
    public void TryFire_GuardBlocked_AllocatesNothing()
    {
        var machine = new GatedOrderMachine();
        machine.SetAllow(false);
        AllocationGate.AssertBudget(0, Iterations, () => machine.TryFire(GatedTrigger.Submit), "TryFire guard blocked");
        machine.Current.Should().Be(GatedState.Idle);
    }

    [Fact]
    public void TryFire_Concurrent_Cycle_AllocatesNothing()
    {
        var machine = new CircuitBreakerFsm();
        AllocationGate.AssertBudget(0, Iterations, () =>
        {
            machine.TryFire(CbTrigger.Trip);
            machine.TryFire(CbTrigger.Probe);
            machine.TryFire(CbTrigger.Reset);
        }, "concurrent TryFire cycle");
        machine.Current.Should().Be(CbState.Closed);
    }
}
