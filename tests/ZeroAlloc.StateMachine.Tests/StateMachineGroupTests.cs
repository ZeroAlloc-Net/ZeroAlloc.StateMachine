namespace ZeroAlloc.StateMachine.Tests;

using System.Threading.Tasks;
using Xunit;

public class StateMachineGroupTests
{
    [Fact]
    public void Parts_evolve_independently()
    {
        using var d = new Device();
        Assert.Equal(OpS.Idle, d.OpCurrent);
        Assert.Equal(ConnS.Disconnected, d.ConnCurrent);

        Assert.True(d.TryFireOp(OpT.Start));
        Assert.Equal(OpS.Running, d.OpCurrent);
        Assert.Equal(ConnS.Disconnected, d.ConnCurrent); // unchanged

        Assert.True(d.TryFireConn(ConnT.Connect));
        Assert.Equal(OpS.Running, d.OpCurrent);          // unchanged
        Assert.Equal(ConnS.Connected, d.ConnCurrent);
    }

    [Fact]
    public void TryFire_unknown_trigger_returns_false_no_state_change()
    {
        using var d = new Device();
        Assert.False(d.TryFireOp(OpT.Stop)); // not valid from Idle
        Assert.Equal(OpS.Idle, d.OpCurrent);
    }

    [Fact]
    public async Task Timed_edge_in_part_arms_disarms_independent_of_other_part()
    {
        using var d = new Device();
        Assert.True(d.TryFireOp(OpT.Start));        // → Op:Running, 100ms Fault timer armed
        Assert.True(d.TryFireConn(ConnT.Connect));  // unrelated; should not affect Op timer
        await Task.Delay(250);
        Assert.Equal(OpS.Faulted, d.OpCurrent);
        Assert.Equal(ConnS.Connected, d.ConnCurrent);
    }
}
