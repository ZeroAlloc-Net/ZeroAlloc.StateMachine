namespace ZeroAlloc.StateMachine.Tests;

/// <summary>A guarded order machine without hooks, so allocation tests count only generated code.</summary>
[StateMachine(InitialState = nameof(GatedState.Idle))]
[Transition<GatedState, GatedTrigger>(From = GatedState.Idle,       On = GatedTrigger.Submit, To = GatedState.Pending, When = true)]
[Transition<GatedState, GatedTrigger>(From = GatedState.Pending,    On = GatedTrigger.Pay,    To = GatedState.Processing)]
[Transition<GatedState, GatedTrigger>(From = GatedState.Processing, On = GatedTrigger.Ship,   To = GatedState.Shipped)]
[Terminal<GatedState>(State = GatedState.Shipped)]
public partial class GatedOrderMachine
{
    private bool _allow;

    public void SetAllow(bool value) => _allow = value;
    private partial bool GuardSubmit(GatedState from, GatedTrigger on) => _allow;
}
