namespace ZeroAlloc.StateMachine.Tests;

/// <summary>Concurrent machine with a timed edge out of Working.</summary>
[StateMachine(InitialState = nameof(RState.Idle), Concurrent = true)]
[Transition<RState, RTrigger>(From = RState.Idle,    On = RTrigger.Go,      To = RState.Working)]
[Transition<RState, RTrigger>(From = RState.Working, On = RTrigger.Timeout, To = RState.Done, AfterMs = 200)]
[Terminal<RState>(State = RState.Done)]
public partial class RestoreTimedMachine { }
