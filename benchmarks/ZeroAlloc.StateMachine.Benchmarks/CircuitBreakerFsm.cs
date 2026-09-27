namespace ZeroAlloc.StateMachine.Benchmarks;

// The benchmark cycles Closed -> Open -> HalfOpen -> Closed, so the machine declares exactly
// those three edges.
[StateMachine(InitialState = nameof(CbState.Closed), Concurrent = true)]
[Transition<CbState, CbTrigger>(From = CbState.Closed,   On = CbTrigger.Trip,  To = CbState.Open)]
[Transition<CbState, CbTrigger>(From = CbState.Open,     On = CbTrigger.Probe, To = CbState.HalfOpen)]
[Transition<CbState, CbTrigger>(From = CbState.HalfOpen, On = CbTrigger.Reset, To = CbState.Closed)]
public partial class CircuitBreakerFsm { }
