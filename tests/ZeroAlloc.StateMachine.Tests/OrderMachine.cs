namespace ZeroAlloc.StateMachine.Tests;

[StateMachine(InitialState = nameof(OrderState.Idle))]
[Transition<OrderState, OrderTrigger>(From = OrderState.Idle,       On = OrderTrigger.Submit, To = OrderState.Pending,    When = true)]
[Transition<OrderState, OrderTrigger>(From = OrderState.Pending,    On = OrderTrigger.Pay,    To = OrderState.Processing)]
[Transition<OrderState, OrderTrigger>(From = OrderState.Processing, On = OrderTrigger.Ship,   To = OrderState.Shipped)]
[Transition<OrderState, OrderTrigger>(From = OrderState.Pending,    On = OrderTrigger.Cancel, To = OrderState.Cancelled)]
[Terminal<OrderState>(State = OrderState.Shipped)]
[Terminal<OrderState>(State = OrderState.Cancelled)]
public partial class OrderMachine
{
    private readonly List<string> _log = new();
    private bool _canSubmit = true;

    public IReadOnlyList<string> Log => _log;

    public void SetCanSubmit(bool value) => _canSubmit = value;
    private partial bool GuardSubmit(OrderState from, OrderTrigger on) => _canSubmit;

    partial void OnEnterPending(OrderState from)  => _log.Add($"enter:Pending from:{from}");
    partial void OnExitPending(OrderTrigger on)   => _log.Add($"exit:Pending on:{on}");
}
