namespace ZeroAlloc.StateMachine.Tests;

[StateMachine(InitialState = nameof(RState.Idle))]
[Transition<RState, RSubTrigger>(From = RState.Idle,    On = RSubTrigger.Next, To = RState.Working)]
[Transition<RState, RSubTrigger>(From = RState.Working, On = RSubTrigger.Next, To = RState.Done)]
[CompositeState<RState>(State = RState.Working, SubMachine = typeof(RestoreChild))]
[Terminal<RState>(State = RState.Done)]
public partial class RestoreParent { }
