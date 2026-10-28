namespace ZeroAlloc.StateMachine.Tests;

[StateMachine(InitialState = nameof(RSubState.Fetching))]
[Transition<RSubState, RSubTrigger>(From = RSubState.Fetching, On = RSubTrigger.Next, To = RSubState.Parsing)]
[Terminal<RSubState>(State = RSubState.Parsing)]
public partial class RestoreChild { }
