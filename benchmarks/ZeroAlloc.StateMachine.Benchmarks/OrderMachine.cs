namespace ZeroAlloc.StateMachine.Benchmarks;

[StateMachine(InitialState = nameof(OrderState.Idle))]
[Transition<OrderState, OrderTrigger>(From = OrderState.Idle,       On = OrderTrigger.Submit, To = OrderState.Pending)]
[Transition<OrderState, OrderTrigger>(From = OrderState.Pending,    On = OrderTrigger.Pay,    To = OrderState.Processing)]
[Transition<OrderState, OrderTrigger>(From = OrderState.Processing, On = OrderTrigger.Ship,   To = OrderState.Shipped)]
[Terminal<OrderState>(State = OrderState.Shipped)]
public partial class OrderMachine { }
