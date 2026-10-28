namespace ZeroAlloc.StateMachine.Tests;

/// <summary>
/// Concurrent machine. Idle has a timed edge to Failed, Working has an untimed Timeout edge to
/// Done, so a timer left armed for the initial state would move a machine restored to Working.
/// </summary>
[StateMachine(InitialState = nameof(RState.Idle), Concurrent = true)]
[Transition<RState, RTrigger>(From = RState.Idle,    On = RTrigger.Go,      To = RState.Working)]
[Transition<RState, RTrigger>(From = RState.Idle,    On = RTrigger.Timeout, To = RState.Failed, AfterMs = 300)]
[Transition<RState, RTrigger>(From = RState.Working, On = RTrigger.Timeout, To = RState.Done)]
[Terminal<RState>(State = RState.Failed)]
[Terminal<RState>(State = RState.Done)]
public partial class RestoreConcurrentMachine { }
