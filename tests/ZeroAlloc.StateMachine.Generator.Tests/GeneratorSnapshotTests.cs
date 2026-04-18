using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.StateMachine.Generator.Tests;

public class GeneratorSnapshotTests
{
    [Fact]
    public Task BasicMachine_GeneratesExpectedCode()
    {
        var source = """
            using ZeroAlloc.StateMachine;

            namespace MyApp;

            public enum State   { Idle, Pending, Done }
            public enum Trigger { Submit, Complete }

            [StateMachine(InitialState = nameof(State.Idle))]
            [Transition<State, Trigger>(From = State.Idle,    On = Trigger.Submit,   To = State.Pending)]
            [Transition<State, Trigger>(From = State.Pending, On = Trigger.Complete, To = State.Done)]
            [Terminal<State>(State = State.Done)]
            public partial class OrderMachine { }
            """;

        return TestHelper.Verify<StateMachineGenerator>(source);
    }

    [Fact]
    public Task MachineWithGuard_EmitsGuardStubAndWhenClause()
    {
        var source = """
            using ZeroAlloc.StateMachine;

            namespace MyApp;

            public enum State   { Idle, Pending, Done }
            public enum Trigger { Submit, Complete }

            [StateMachine(InitialState = nameof(State.Idle))]
            [Transition<State, Trigger>(From = State.Idle,    On = Trigger.Submit,   To = State.Pending, When = true)]
            [Transition<State, Trigger>(From = State.Pending, On = Trigger.Complete, To = State.Done)]
            [Terminal<State>(State = State.Done)]
            public partial class GuardedMachine { }
            """;

        return TestHelper.Verify<StateMachineGenerator>(source);
    }

    [Fact]
    public Task ConcurrentMachine_EmitsInterlockedCompareExchange()
    {
        var source = """
            using ZeroAlloc.StateMachine;

            namespace Resilience;

            public enum CbState   { Closed, Open, HalfOpen }
            public enum CbTrigger { Trip, Probe, Reset }

            [StateMachine(InitialState = nameof(CbState.Closed), Concurrent = true)]
            [Transition<CbState, CbTrigger>(From = CbState.Closed,   On = CbTrigger.Trip,  To = CbState.Open)]
            [Transition<CbState, CbTrigger>(From = CbState.Open,     On = CbTrigger.Probe, To = CbState.HalfOpen)]
            [Transition<CbState, CbTrigger>(From = CbState.HalfOpen, On = CbTrigger.Reset, To = CbState.Closed)]
            [Transition<CbState, CbTrigger>(From = CbState.HalfOpen, On = CbTrigger.Trip,  To = CbState.Open)]
            public partial class CircuitBreakerFsm { }
            """;

        return TestHelper.Verify<StateMachineGenerator>(source);
    }
}
