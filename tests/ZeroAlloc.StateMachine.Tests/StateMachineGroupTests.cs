using ZeroAlloc.StateMachine;

#pragma warning disable ZSM0003 // single-use trigger — intentional in test machines
#pragma warning disable ZSM0002 // sink/unreachable state warnings on test fixtures
#pragma warning disable MA0048  // file name must match type name — multiple types in one test file

namespace ZeroAlloc.StateMachine.Tests;

using System.Threading.Tasks;
using Xunit;

public enum OpS { Idle, Running, Faulted }
public enum OpT { Start, Stop, Fault }
public enum ConnS { Disconnected, Connected }
public enum ConnT { Connect, Disconnect }

[StateMachineGroup]
[StateMachinePart<OpS,   OpT>(Name = "Op",   InitialState = OpS.Idle)]
[StateMachinePart<ConnS, ConnT>(Name = "Conn", InitialState = ConnS.Disconnected)]
[Transition<OpS,   OpT>(From = OpS.Idle,    On = OpT.Start, To = OpS.Running, Part = "Op")]
[Transition<OpS,   OpT>(From = OpS.Running, On = OpT.Stop,  To = OpS.Idle,    Part = "Op")]
[Transition<OpS,   OpT>(From = OpS.Running, On = OpT.Fault, To = OpS.Faulted, Part = "Op", AfterMs = 100)]
[Transition<ConnS, ConnT>(From = ConnS.Disconnected, On = ConnT.Connect,    To = ConnS.Connected,    Part = "Conn")]
[Transition<ConnS, ConnT>(From = ConnS.Connected,    On = ConnT.Disconnect, To = ConnS.Disconnected, Part = "Conn")]
public partial class Device { }

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
