namespace ZeroAlloc.StateMachine.Tests;

/// <summary>A watchdog that dies unless it gets a heartbeat.</summary>
[StateMachine(InitialState = nameof(WdState.Idle), Concurrent = true)]
[Terminal<WdState>(State = WdState.Dead)]
[Transition<WdState, WdTrigger>(From = WdState.Idle,    On = WdTrigger.Start,    To = WdState.Working)]
[Transition<WdState, WdTrigger>(From = WdState.Working, On = WdTrigger.Heartbeat, To = WdState.Working)]
// AfterMs is set generously (500ms) so the negative-case test
// (User_fire_before_timer_disarms_cleanly) has a wide stability margin
// against ThreadPool / Task.Delay scheduling jitter on busy CI hosts.
[Transition<WdState, WdTrigger>(From = WdState.Working, On = WdTrigger.Timeout,   To = WdState.Dead, AfterMs = 500)]
public partial class Watchdog { }
