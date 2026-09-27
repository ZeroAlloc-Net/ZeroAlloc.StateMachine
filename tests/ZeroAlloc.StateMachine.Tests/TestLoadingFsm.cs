namespace ZeroAlloc.StateMachine.Tests;

/// <summary>The sub-machine of <see cref="TestHierMachine"/>'s Loading state.</summary>
[StateMachine(InitialState = nameof(HSubState.Fetching))]
[Transition<HSubState, HTrigger>(From = HSubState.Fetching, On = HTrigger.DataReceived, To = HSubState.Parsing)]
[Terminal<HSubState>(State = HSubState.Parsing)]
public partial class TestLoadingFsm { }
