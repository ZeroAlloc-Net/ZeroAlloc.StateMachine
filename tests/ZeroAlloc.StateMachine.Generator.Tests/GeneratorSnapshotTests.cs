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
}
