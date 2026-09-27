namespace ZeroAlloc.StateMachine.Tests;

/// <summary>A machine whose initial state carries a timed transition.</summary>
[StateMachine(InitialState = "Working", Concurrent = true)]
[Transition<WatchState, WatchTrigger>(From = WatchState.Working, On = WatchTrigger.Timeout, To = WatchState.Dead, AfterMs = 500)]
[Terminal<WatchState>(State = WatchState.Dead)]
public partial class InitialArmWatchdog { }
