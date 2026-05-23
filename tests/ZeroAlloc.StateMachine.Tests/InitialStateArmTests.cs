#pragma warning disable MA0048   // file holds multiple top-level types
#pragma warning disable ZSM0002  // sink states are intentional

namespace ZeroAlloc.StateMachine.Tests;

using System.Threading.Tasks;
using Xunit;
using ZeroAlloc.StateMachine;

public enum WatchState { Working, Dead }
public enum WatchTrigger { Timeout }

[StateMachine(InitialState = "Working", Concurrent = true)]
[Transition<WatchState, WatchTrigger>(From = WatchState.Working, On = WatchTrigger.Timeout, To = WatchState.Dead, AfterMs = 500)]
[Terminal<WatchState>(State = WatchState.Dead)]
public partial class InitialArmWatchdog { }

public class InitialStateArmTests
{
    [Fact]
    public async Task Constructor_arms_initial_state_timer()
    {
        using var w = new InitialArmWatchdog();
        Assert.Equal(WatchState.Working, w.Current);

        // Give the timer time to fire — no user TryFire call.
        await Task.Delay(1000);
        Assert.Equal(WatchState.Dead, w.Current);
    }

    [Fact]
    public async Task Reset_rearms_initial_state_timer()
    {
        using var w = new InitialArmWatchdog();
        await Task.Delay(1000);
        Assert.Equal(WatchState.Dead, w.Current);

        // Reset puts state back to Working; should re-arm the timer.
        // Reset is internal — access via reflection.
        var resetMethod = typeof(InitialArmWatchdog).GetMethod("Reset",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(resetMethod);
        resetMethod!.Invoke(w, null);
        Assert.Equal(WatchState.Working, w.Current);

        await Task.Delay(1000);
        Assert.Equal(WatchState.Dead, w.Current);
    }
}
