namespace ZeroAlloc.StateMachine.Tests;

[StateMachine(InitialState = nameof(RState.Idle))]
[Transition<RState, RTrigger>(From = RState.Idle,    On = RTrigger.Go, To = RState.Working)]
[Transition<RState, RTrigger>(From = RState.Working, On = RTrigger.Go, To = RState.Done)]
[Terminal<RState>(State = RState.Done)]
public partial struct RestoreStructMachine { }
