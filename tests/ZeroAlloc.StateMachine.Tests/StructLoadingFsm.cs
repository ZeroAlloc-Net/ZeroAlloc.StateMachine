namespace ZeroAlloc.StateMachine.Tests;

/// <summary>A struct machine used as the sub-machine of a composite state.</summary>
[StateMachine(InitialState = nameof(StructSubState.Fetching))]
[Transition<StructSubState, StructTrigger>(From = StructSubState.Fetching, On = StructTrigger.DataReceived, To = StructSubState.Parsing)]
[Terminal<StructSubState>(State = StructSubState.Parsing)]
public partial struct StructLoadingFsm { }
