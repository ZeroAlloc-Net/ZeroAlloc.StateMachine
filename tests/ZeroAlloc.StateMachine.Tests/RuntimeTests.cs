using ZeroAlloc.StateMachine;

#pragma warning disable ZSM0003 // single-use trigger — intentional in test machines
#pragma warning disable MA0048  // file name must match type name — multiple types in one test file
#pragma warning disable MA0016  // prefer collection abstraction — List<string> is fine in tests

namespace ZeroAlloc.StateMachine.Tests;

// ── Test machines declared directly (generator compiles them) ───────────────

public enum OrderState   { Idle, Pending, Processing, Shipped, Cancelled }
public enum OrderTrigger { Submit, Pay, Ship, Cancel }

[StateMachine(InitialState = nameof(OrderState.Idle))]
[Transition<OrderState, OrderTrigger>(From = OrderState.Idle,       On = OrderTrigger.Submit, To = OrderState.Pending,    When = true)]
[Transition<OrderState, OrderTrigger>(From = OrderState.Pending,    On = OrderTrigger.Pay,    To = OrderState.Processing)]
[Transition<OrderState, OrderTrigger>(From = OrderState.Processing, On = OrderTrigger.Ship,   To = OrderState.Shipped)]
[Transition<OrderState, OrderTrigger>(From = OrderState.Pending,    On = OrderTrigger.Cancel, To = OrderState.Cancelled)]
[Terminal<OrderState>(State = OrderState.Shipped)]
[Terminal<OrderState>(State = OrderState.Cancelled)]
public partial class OrderMachine
{
    private bool _canSubmit = true;
    public void SetCanSubmit(bool value) => _canSubmit = value;
    private partial bool GuardSubmit(OrderState from, OrderTrigger on) => _canSubmit;

    public List<string> Log { get; } = new();
    partial void OnEnterPending(OrderState from)  => Log.Add($"enter:Pending from:{from}");
    partial void OnExitPending(OrderTrigger on)   => Log.Add($"exit:Pending on:{on}");
}

// Concurrent circuit breaker
public enum CbState   { Closed, Open, HalfOpen }
public enum CbTrigger { Trip, Probe, Reset }

[StateMachine(InitialState = nameof(CbState.Closed), Concurrent = true)]
[Transition<CbState, CbTrigger>(From = CbState.Closed,   On = CbTrigger.Trip,  To = CbState.Open)]
[Transition<CbState, CbTrigger>(From = CbState.Open,     On = CbTrigger.Probe, To = CbState.HalfOpen)]
[Transition<CbState, CbTrigger>(From = CbState.HalfOpen, On = CbTrigger.Reset, To = CbState.Closed)]
[Transition<CbState, CbTrigger>(From = CbState.HalfOpen, On = CbTrigger.Trip,  To = CbState.Open)]
public partial class CircuitBreakerFsm { }

// ── Tests ────────────────────────────────────────────────────────────────────

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
