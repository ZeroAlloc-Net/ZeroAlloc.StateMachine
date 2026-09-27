namespace ZeroAlloc.StateMachine.Tests;

/// <summary>The middle machine of the three-level nesting fixture.</summary>
[StateMachine(InitialState = nameof(N2.X))]
[Transition<N2, NTrig>(From = N2.X, On = NTrig.Tick, To = N2.Y)]
[CompositeState<N2>(State = N2.X, SubMachine = typeof(TestLeafFsm))]
[Terminal<N2>(State = N2.Y)]
public partial class TestMidFsm { }
