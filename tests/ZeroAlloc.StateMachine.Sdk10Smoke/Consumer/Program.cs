using ZeroAlloc.StateMachine;

// TryFire exists only if the compiler loaded the generator.
var order = new OrderMachine();
if (!order.TryFire(OrderTrigger.Submit) || order.Current != OrderState.Pending
    || order.TryFire(OrderTrigger.Submit))
{
    Console.Error.WriteLine($"FAIL: unexpected state {order.Current}");
    return 1;
}

Console.WriteLine("SDK 10 consumer: generator loaded, transition fired.");
return 0;

public enum OrderState { Idle, Pending }

public enum OrderTrigger { Submit }

[Terminal<OrderState>(State = OrderState.Pending)]
[StateMachine(InitialState = nameof(OrderState.Idle))]
[Transition<OrderState, OrderTrigger>(From = OrderState.Idle, On = OrderTrigger.Submit, To = OrderState.Pending)]
public sealed partial class OrderMachine
{
}
