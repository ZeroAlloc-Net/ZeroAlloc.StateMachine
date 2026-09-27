namespace ZeroAlloc.StateMachine.Tests;

/// <summary>The innermost machine of the three-level nesting fixture.</summary>
[StateMachine(InitialState = nameof(N3.P))]
[Transition<N3, NTrig>(From = N3.P, On = NTrig.Tick, To = N3.Q)]
[Terminal<N3>(State = N3.Q)]
public partial class TestLeafFsm { }
