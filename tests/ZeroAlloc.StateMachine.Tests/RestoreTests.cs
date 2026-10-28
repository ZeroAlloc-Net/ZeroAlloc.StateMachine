namespace ZeroAlloc.StateMachine.Tests;

public class RestoreTests
{
    [Fact]
    public void Class_Restore_sets_the_state_and_fires_no_hooks()
    {
        var m = RestoreClassMachine.Restore(RState.Working);

        Assert.Equal(RState.Working, m.Current);
        Assert.Empty(m.Log);
        Assert.True(m.TryFire(RTrigger.Go));
        Assert.Equal(RState.Done, m.Current);
        Assert.Equal(new[] { "exit:Working", "enter:Done" }, m.Log, StringComparer.Ordinal);
    }

    [Fact]
    public void Class_Restore_to_the_initial_state_works()
    {
        Assert.Equal(RState.Idle, RestoreClassMachine.Restore(RState.Idle).Current);
    }

    [Fact]
    public void Struct_Restore_sets_the_state()
    {
        var m = RestoreStructMachine.Restore(RState.Working);

        Assert.Equal(RState.Working, m.Current);
        Assert.True(m.TryFire(RTrigger.Go));
        Assert.Equal(RState.Done, m.Current);
    }

    [Fact]
    public void Concurrent_Restore_sets_the_state()
    {
        var m = RestoreConcurrentMachine.Restore(RState.Working);

        Assert.Equal(RState.Working, m.Current);
    }

    [Theory]
    [InlineData((RState)42)]
    [InlineData((RState)(-1))]
    public void Restore_rejects_a_value_that_is_not_a_declared_state(RState state)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => RestoreClassMachine.Restore(state));
        Assert.Equal("state", ex.ParamName);
        Assert.Throws<ArgumentOutOfRangeException>(() => RestoreStructMachine.Restore(state));
        Assert.Throws<ArgumentOutOfRangeException>(() => RestoreConcurrentMachine.Restore(state));
    }

    [Fact]
    public void Restore_rejects_a_state_unreachable_from_the_initial_state()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => RestoreClassMachine.Restore(RState.Orphan));
        Assert.Equal("state", ex.ParamName);
        Assert.Throws<ArgumentOutOfRangeException>(() => RestoreStructMachine.Restore(RState.Orphan));
        Assert.Throws<ArgumentOutOfRangeException>(() => RestoreConcurrentMachine.Restore(RState.Orphan));
    }

    // Timer fields are private, and a timer is created the first time it is armed. Reading the
    // field shows, without any waiting, whether a timer was ever armed for that edge.
    private static System.Threading.Timer? TimerOf(object machine, string edge) =>
        (System.Threading.Timer?)machine.GetType()
            .GetField("_timer_" + edge, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(machine);

    [Fact]
    public void Concurrent_Restore_arms_the_timers_of_the_restored_state()
    {
        using var m = RestoreTimedMachine.Restore(RState.Working);

        Assert.NotNull(TimerOf(m, "Working_Timeout"));
    }

    [Fact]
    public void Concurrent_Restore_never_arms_the_timer_of_the_initial_state()
    {
        // Restore builds the machine through a constructor that arms nothing, so no timer of
        // the initial state exists that could fire before the restored state is set.
        using var m = RestoreConcurrentMachine.Restore(RState.Working);

        Assert.Null(TimerOf(m, "Idle_Timeout"));
    }

    [Fact]
    public void Concurrent_Restore_to_the_initial_state_arms_its_timer()
    {
        using var m = RestoreConcurrentMachine.Restore(RState.Idle);

        Assert.NotNull(TimerOf(m, "Idle_Timeout"));
    }

    [Fact]
    public void A_machine_built_with_new_still_arms_the_initial_state_timer()
    {
        using var m = new RestoreConcurrentMachine();

        Assert.NotNull(TimerOf(m, "Idle_Timeout"));
    }

    [Fact]
    public void Composite_Restore_resets_the_sub_machine_to_its_initial_state()
    {
        var m = RestoreParent.Restore(RState.Working);

        Assert.Equal(RState.Working, m.Current);
        Assert.True(m.TryFire(RSubTrigger.Next));      // sub: Fetching -> Parsing, parent stays
        Assert.Equal(RState.Working, m.Current);
    }

    [Fact]
    public void Composite_Restore_with_a_sub_state_restores_the_sub_machine()
    {
        var m = RestoreParent.Restore(RState.Working, RSubState.Parsing);

        Assert.Equal(RState.Working, m.Current);
        Assert.True(m.TryFire(RSubTrigger.Next));      // sub is at its end, so the parent takes it
        Assert.Equal(RState.Done, m.Current);
    }

    [Fact]
    public void Composite_Restore_rejects_a_sub_state_the_sub_machine_cannot_reach()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => RestoreParent.Restore(RState.Working, RSubState.Unlinked));
        Assert.Equal("subState", ex.ParamName);
        Assert.Throws<ArgumentOutOfRangeException>(() => RestoreParent.Restore(RState.Working, (RSubState)42));
    }

    [Fact]
    public void Composite_Restore_rejects_a_parent_state_that_has_no_sub_machine()
    {
        var ex = Assert.Throws<ArgumentException>(() => RestoreParent.Restore(RState.Idle, RSubState.Fetching));
        Assert.Equal("state", ex.ParamName);
        Assert.Throws<ArgumentOutOfRangeException>(() => RestoreParent.Restore(RState.Orphan, RSubState.Fetching));
    }
}
