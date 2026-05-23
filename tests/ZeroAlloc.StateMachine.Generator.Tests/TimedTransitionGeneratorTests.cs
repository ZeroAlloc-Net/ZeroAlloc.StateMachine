namespace ZeroAlloc.StateMachine.Generator.Tests;

public class TimedTransitionGeneratorTests
{
    [Fact]
    public Task SingleTimedEdge()
    {
        var source = """
            using ZeroAlloc.StateMachine;

            namespace MyApp;

            public enum WdState { Idle, Working, Dead }
            public enum WdTrigger { Start, Heartbeat, Timeout }

            [StateMachine(InitialState = "Idle", Concurrent = true)]
            [Transition<WdState, WdTrigger>(From = WdState.Idle,    On = WdTrigger.Start,    To = WdState.Working)]
            [Transition<WdState, WdTrigger>(From = WdState.Working, On = WdTrigger.Heartbeat, To = WdState.Working)]
            [Transition<WdState, WdTrigger>(From = WdState.Working, On = WdTrigger.Timeout,   To = WdState.Dead, AfterMs = 5000)]
            [Terminal<WdState>(State = WdState.Dead)]
            public partial class Watchdog { }
            """;

        return TestHelper.Verify<StateMachineGenerator>(source);
    }

    [Fact]
    public Task MultipleTimedEdges()
    {
        var source = """
            using ZeroAlloc.StateMachine;

            namespace MyApp;

            public enum MS { A, B, C }
            public enum MT { ToB, ToC, Reset }

            [StateMachine(InitialState = "A", Concurrent = true)]
            [Transition<MS, MT>(From = MS.A, On = MT.ToB,   To = MS.B, AfterMs = 1000)]
            [Transition<MS, MT>(From = MS.B, On = MT.ToC,   To = MS.C, AfterMs = 2000)]
            [Transition<MS, MT>(From = MS.C, On = MT.Reset, To = MS.A)]
            public partial class Multi { }
            """;

        return TestHelper.Verify<StateMachineGenerator>(source);
    }
}
