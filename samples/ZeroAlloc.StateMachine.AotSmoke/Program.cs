using System;
using ZeroAlloc.StateMachine.AotSmoke;

// Exercise both generator emission shapes under PublishAot=true:
//   * OrderStateMachine — plain switch-expression transition path
//   * CircuitBreaker    — Concurrent=true: volatile long + Interlocked.CompareExchange

var order = new OrderStateMachine();
if (order.Current != OrderState.Idle) return Fail($"OrderStateMachine.Current expected Idle, got {order.Current}");

// Happy path
if (!order.TryFire(OrderTrigger.Submit)) return Fail("TryFire(Submit) rejected on Idle");
if (order.Current != OrderState.Pending) return Fail($"After Submit, expected Pending, got {order.Current}");
if (!order.TryFire(OrderTrigger.Pay))    return Fail("TryFire(Pay) rejected on Pending");
if (!order.TryFire(OrderTrigger.Ship))   return Fail("TryFire(Ship) rejected on Processing");
if (order.Current != OrderState.Shipped) return Fail($"After Ship, expected Shipped, got {order.Current}");

// Invalid trigger on terminal must be rejected cleanly (not crash)
if (order.TryFire(OrderTrigger.Cancel))
    return Fail("TryFire(Cancel) incorrectly accepted on Shipped (terminal)");

// Concurrent variant
var cb = new CircuitBreaker();
if (cb.Current != CbState.Closed) return Fail($"CircuitBreaker.Current expected Closed, got {cb.Current}");
if (!cb.TryFire(CbTrigger.Trip))  return Fail("CB TryFire(Trip) rejected on Closed");
if (cb.Current != CbState.Open)   return Fail($"After Trip, expected Open, got {cb.Current}");
if (!cb.TryFire(CbTrigger.Probe)) return Fail("CB TryFire(Probe) rejected on Open");
if (!cb.TryFire(CbTrigger.Reset)) return Fail("CB TryFire(Reset) rejected on HalfOpen");
if (cb.Current != CbState.Closed) return Fail($"After Reset, expected Closed, got {cb.Current}");

Console.WriteLine("AOT smoke: PASS");
return 0;

static int Fail(string message)
{
    Console.Error.WriteLine($"AOT smoke: FAIL — {message}");
    return 1;
}
