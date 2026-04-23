using ZeroAlloc.StateMachine;

namespace ZeroAlloc.StateMachine.AotSmoke;

#pragma warning disable MA0048 // co-locating the state/trigger enums with the machine is fine for a sample
public enum CbState { Closed, Open, HalfOpen }
public enum CbTrigger { Trip, Probe, Reset }
#pragma warning restore MA0048

// Concurrent=true generates a volatile long + Interlocked.CompareExchange version.
// Guarantees we exercise both the plain and the concurrent emission shapes under AOT.
[StateMachine(InitialState = nameof(CbState.Closed), Concurrent = true)]
[Transition<CbState, CbTrigger>(From = CbState.Closed,   On = CbTrigger.Trip,  To = CbState.Open)]
[Transition<CbState, CbTrigger>(From = CbState.Open,     On = CbTrigger.Probe, To = CbState.HalfOpen)]
[Transition<CbState, CbTrigger>(From = CbState.HalfOpen, On = CbTrigger.Reset, To = CbState.Closed)]
[Transition<CbState, CbTrigger>(From = CbState.HalfOpen, On = CbTrigger.Trip,  To = CbState.Open)]
public partial class CircuitBreaker { }
