namespace ZeroAlloc.StateMachine.Tests;

/// <summary>The canonical circuit breaker, generated in concurrent mode.</summary>
[StateMachine(InitialState = nameof(CbState.Closed), Concurrent = true)]
[Transition<CbState, CbTrigger>(From = CbState.Closed,   On = CbTrigger.Trip,  To = CbState.Open)]
[Transition<CbState, CbTrigger>(From = CbState.Open,     On = CbTrigger.Probe, To = CbState.HalfOpen)]
[Transition<CbState, CbTrigger>(From = CbState.HalfOpen, On = CbTrigger.Reset, To = CbState.Closed)]
[Transition<CbState, CbTrigger>(From = CbState.HalfOpen, On = CbTrigger.Trip,  To = CbState.Open)]
public partial class CircuitBreakerFsm { }
