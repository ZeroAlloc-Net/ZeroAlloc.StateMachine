namespace ZeroAlloc.StateMachine.Tests;

/// <summary>The outermost machine of the three-level nesting fixture.</summary>
[StateMachine(InitialState = nameof(N1.A))]
[Transition<N1, NTrig>(From = N1.A, On = NTrig.Reset, To = N1.B)]
[CompositeState<N1>(State = N1.A, SubMachine = typeof(TestMidFsm))]
[Terminal<N1>(State = N1.B)]
public partial class TestTopFsm { }
