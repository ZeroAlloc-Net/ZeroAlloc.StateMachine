using ZeroAlloc.StateMachine;

namespace ZeroAlloc.StateMachine.AotSmoke;

public enum OrderState { Idle, Pending, Processing, Shipped, Cancelled }
public enum OrderTrigger { Submit, Pay, Ship, Cancel }

[StateMachine(InitialState = nameof(OrderState.Idle))]
[Transition<OrderState, OrderTrigger>(From = OrderState.Idle,       On = OrderTrigger.Submit, To = OrderState.Pending)]
[Transition<OrderState, OrderTrigger>(From = OrderState.Pending,    On = OrderTrigger.Pay,    To = OrderState.Processing)]
[Transition<OrderState, OrderTrigger>(From = OrderState.Processing, On = OrderTrigger.Ship,   To = OrderState.Shipped)]
[Transition<OrderState, OrderTrigger>(From = OrderState.Pending,    On = OrderTrigger.Cancel, To = OrderState.Cancelled)]
[Terminal<OrderState>(State = OrderState.Shipped)]
[Terminal<OrderState>(State = OrderState.Cancelled)]
public partial class OrderStateMachine { }
