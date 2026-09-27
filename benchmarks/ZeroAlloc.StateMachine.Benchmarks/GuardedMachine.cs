namespace ZeroAlloc.StateMachine.Benchmarks;

[StateMachine(InitialState = nameof(GuardedState.Idle))]
[Transition<GuardedState, GuardedTrigger>(From = GuardedState.Idle, On = GuardedTrigger.Start, To = GuardedState.Active, When = true)]
[Terminal<GuardedState>(State = GuardedState.Active)]
public partial class GuardedMachine
{
    private bool _allow;
    public void SetAllow(bool v) => _allow = v;
    private partial bool GuardStart(GuardedState from, GuardedTrigger on) => _allow;
}
