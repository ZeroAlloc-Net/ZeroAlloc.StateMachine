namespace ZeroAlloc.StateMachine.Tests;

public class RuntimeTests
{
    [Fact]
    public void TryFire_ValidTransition_ReturnsTrueAndUpdatesState()
    {
        var machine = new OrderMachine();
        machine.TryFire(OrderTrigger.Submit).Should().BeTrue();
        machine.Current.Should().Be(OrderState.Pending);
    }

    [Fact]
    public void TryFire_InvalidTransition_ReturnsFalse()
    {
        var machine = new OrderMachine();
        machine.TryFire(OrderTrigger.Pay).Should().BeFalse();
        machine.Current.Should().Be(OrderState.Idle);
    }

    [Fact]
    public void TryFire_GuardReturnsFalse_TransitionBlocked()
    {
        var machine = new OrderMachine();
        machine.SetCanSubmit(false);
        machine.TryFire(OrderTrigger.Submit).Should().BeFalse();
        machine.Current.Should().Be(OrderState.Idle);
    }

    [Fact]
    public void TryFire_GuardReturnsTrue_TransitionAllowed()
    {
        var machine = new OrderMachine();
        machine.SetCanSubmit(true);
        machine.TryFire(OrderTrigger.Submit).Should().BeTrue();
        machine.Current.Should().Be(OrderState.Pending);
    }

    [Fact]
    public void TryFire_EntryAndExitHooksFire_InCorrectOrder()
    {
        var machine = new OrderMachine();
        machine.TryFire(OrderTrigger.Submit);   // Idle → Pending (OnEnterPending fires)
        machine.TryFire(OrderTrigger.Pay);      // Pending → Processing (OnExitPending fires first)

        machine.Log[0].Should().Be("enter:Pending from:Idle");
        machine.Log[1].Should().Be("exit:Pending on:Pay");
    }

    [Fact]
    public void TryFire_FullSequence_ReachesTerminalState()
    {
        var machine = new OrderMachine();
        machine.TryFire(OrderTrigger.Submit).Should().BeTrue();
        machine.TryFire(OrderTrigger.Pay).Should().BeTrue();
        machine.TryFire(OrderTrigger.Ship).Should().BeTrue();
        machine.Current.Should().Be(OrderState.Shipped);
    }

    [Fact]
    public void TryFire_Cancel_ReachesCancelledState()
    {
        var machine = new OrderMachine();
        machine.TryFire(OrderTrigger.Submit);
        machine.TryFire(OrderTrigger.Cancel).Should().BeTrue();
        machine.Current.Should().Be(OrderState.Cancelled);
    }

    [Fact]
    public void ConcurrentMachine_InitialState_IsClosed()
    {
        var cb = new CircuitBreakerFsm();
        cb.Current.Should().Be(CbState.Closed);
    }

    [Fact]
    public void ConcurrentMachine_Trip_OpensCircuit()
    {
        var cb = new CircuitBreakerFsm();
        cb.TryFire(CbTrigger.Trip).Should().BeTrue();
        cb.Current.Should().Be(CbState.Open);
    }

    [Fact]
    public void ConcurrentMachine_MultithreadedTrip_StateNeverCorrupted()
    {
        var cb = new CircuitBreakerFsm();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var threads = Enumerable.Range(0, 20).Select(_ => new System.Threading.Thread(() =>
        {
            try
            {
                cb.TryFire(CbTrigger.Trip);
                var s = cb.Current;
                if (!Enum.IsDefined(typeof(CbState), s))
                    throw new InvalidOperationException($"Invalid state: {s}");
            }
            catch (Exception ex) { exceptions.Add(ex); }
        })).ToList();

        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        exceptions.Should().BeEmpty();
        cb.Current.Should().Be(CbState.Open);
    }
}
