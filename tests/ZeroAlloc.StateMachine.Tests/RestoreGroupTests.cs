namespace ZeroAlloc.StateMachine.Tests;

public class RestoreGroupTests
{
    private static System.Threading.Timer? TimerOf(object group, string edge) =>
        (System.Threading.Timer?)group.GetType()
            .GetField("_timer_" + edge, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(group);

    [Fact]
    public void Restore_sets_every_part_and_fires_no_hooks()
    {
        using var g = RestoreGroup.Restore(new RestoreGroup.GroupState(RgOpState.Running, RgLinkState.Up));

        Assert.Equal(RgOpState.Running, g.OpCurrent);
        Assert.Equal(RgLinkState.Up, g.LinkCurrent);
        Assert.Empty(g.Log);
        Assert.True(g.TryFireOp(OpT.Stop));
        Assert.Equal(new[] { "exit:Op.Running" }, g.Log, StringComparer.Ordinal);
    }

    [Fact]
    public void CurrentState_and_Restore_round_trip()
    {
        using var original = new RestoreGroup();
        Assert.True(original.TryFireOp(OpT.Start));
        Assert.True(original.TryFireLink(ConnT.Connect));
        var saved = original.CurrentState;

        using var copy = RestoreGroup.Restore(saved);

        Assert.Equal(new RestoreGroup.GroupState(RgOpState.Running, RgLinkState.Up), saved);
        Assert.Equal(saved, copy.CurrentState);
    }

    [Fact]
    public void Restore_rejects_a_part_state_that_is_not_declared()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => RestoreGroup.Restore(new RestoreGroup.GroupState((RgOpState)42, RgLinkState.Up)));
        Assert.Equal("state", ex.ParamName);
        Assert.Contains("part Op", ex.Message, StringComparison.Ordinal);

        ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => RestoreGroup.Restore(new RestoreGroup.GroupState(RgOpState.Idle, (RgLinkState)(-1))));
        Assert.Contains("part Link", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Restore_rejects_a_part_state_unreachable_from_the_initial_state()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => RestoreGroup.Restore(new RestoreGroup.GroupState(RgOpState.Ghost, RgLinkState.Up)));
        Assert.Contains("part Op", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Restore_arms_the_timers_of_each_restored_state()
    {
        using var g = RestoreGroup.Restore(new RestoreGroup.GroupState(RgOpState.Running, RgLinkState.Down));

        Assert.NotNull(TimerOf(g, "Op_Running_Fault"));
        Assert.NotNull(TimerOf(g, "Link_Down_Connect"));
    }

    [Fact]
    public void Restore_never_arms_the_timer_of_a_part_initial_state_it_leaves()
    {
        using var g = RestoreGroup.Restore(new RestoreGroup.GroupState(RgOpState.Idle, RgLinkState.Up));

        Assert.Null(TimerOf(g, "Link_Down_Connect"));
        Assert.Null(TimerOf(g, "Op_Running_Fault"));
    }

    [Fact]
    public void A_group_built_with_new_still_arms_the_initial_timers()
    {
        using var g = new RestoreGroup();

        Assert.NotNull(TimerOf(g, "Link_Down_Connect"));
    }
}
