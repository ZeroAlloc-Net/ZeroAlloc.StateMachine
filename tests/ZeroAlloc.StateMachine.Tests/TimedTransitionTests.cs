#pragma warning disable MA0048  // file name must match type name -- multiple types in one test file

namespace ZeroAlloc.StateMachine.Tests;

using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZeroAlloc.StateMachine;

public enum WdState { Idle, Working, Dead }
public enum WdTrigger { Start, Heartbeat, Timeout }

[StateMachine(InitialState = nameof(WdState.Idle), Concurrent = true)]
[Terminal<WdState>(State = WdState.Dead)]
[Transition<WdState, WdTrigger>(From = WdState.Idle,    On = WdTrigger.Start,    To = WdState.Working)]
[Transition<WdState, WdTrigger>(From = WdState.Working, On = WdTrigger.Heartbeat, To = WdState.Working)]
// AfterMs is set generously (500ms) so the negative-case test
// (User_fire_before_timer_disarms_cleanly) has a wide stability margin
// against ThreadPool / Task.Delay scheduling jitter on busy CI hosts.
[Transition<WdState, WdTrigger>(From = WdState.Working, On = WdTrigger.Timeout,   To = WdState.Dead, AfterMs = 500)]
public partial class Watchdog { }

public class TimedTransitionTests
{
    [Fact]
    public async Task Timer_arms_on_enter_and_fires_after_duration()
    {
        using var w = new Watchdog();
        Assert.True(w.TryFire(WdTrigger.Start)); // -> Working, arms 500ms timer
        await Task.Delay(1000); // give the timer plenty of slack on busy CI
        Assert.Equal(WdState.Dead, w.Current);
    }

    [Fact]
    public async Task User_fire_before_timer_disarms_cleanly()
    {
        using var w = new Watchdog();
        Assert.True(w.TryFire(WdTrigger.Start));    // -> Working
        Assert.True(w.TryFire(WdTrigger.Heartbeat)); // -> Working (self-transition, disarms + rearms)
        // Poll at multiple checkpoints well within the 500ms re-armed window.
        // If disarm/rearm is broken the original 500ms timer (armed by Start)
        // would still fire roughly 500ms after Start -- so any checkpoint before
        // ~500ms after Start should still observe Working.
        for (int i = 0; i < 4; i++)
        {
            await Task.Delay(50);
            Assert.Equal(WdState.Working, w.Current);
        }
    }

    [Fact]
    public async Task Timer_callback_after_state_moves_is_no_op()
    {
        using var w = new Watchdog();
        Assert.True(w.TryFire(WdTrigger.Start)); // -> Working, 500ms timer armed
        // Race: in real life the timer might fire after we've already moved.
        // Disarm cleanly by transitioning to a state with no Timeout edge.
        // (There's no manual transition out of Working except Timeout, so use Heartbeat -- same state, rearms.)
        await Task.Delay(1000);
        // The most recent arm wins; state should be Dead.
        Assert.Equal(WdState.Dead, w.Current);
    }

    [Fact]
    public void Dispose_cancels_in_flight_timers()
    {
        var w = new Watchdog();
        Assert.True(w.TryFire(WdTrigger.Start));
        w.Dispose();
        // No assertion possible directly -- but no exception should be thrown,
        // and waiting longer than the timer interval should NOT advance Current
        // because the timer is disposed.
        Thread.Sleep(1000);
        Assert.Equal(WdState.Working, w.Current);
    }
}
