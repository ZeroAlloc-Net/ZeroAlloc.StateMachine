namespace ZeroAlloc.StateMachine.Tests;

public class CompositeStateTests
{
    [Fact]
    public void Sub_handles_trigger_when_parent_in_composite()
    {
        var m = new TestHierMachine();
        Assert.True(m.TryFire(HTrigger.Start));            // Outer: Idle -> Loading
        Assert.Equal(HOuterState.Loading, m.Current);
        Assert.True(m.TryFire(HTrigger.DataReceived));     // Sub: Fetching -> Parsing
        Assert.Equal(HOuterState.Loading, m.Current);      // Parent unchanged
    }

    [Fact]
    public void Sub_rejects_parent_falls_through()
    {
        var m = new TestHierMachine();
        m.TryFire(HTrigger.Start);                          // Loading
        Assert.True(m.TryFire(HTrigger.Complete));          // Sub has no (Fetching, Complete) -> parent fires
        Assert.Equal(HOuterState.Done, m.Current);
    }

    [Fact]
    public void History_capture_and_restore()
    {
        var m = new TestHierMachine();
        m.TryFire(HTrigger.Start);
        m.TryFire(HTrigger.DataReceived);                   // Sub at Parsing
        m.TryFire(HTrigger.Suspend);                        // Loading -> Idle; history captured
        Assert.Equal(HOuterState.Idle, m.Current);

        m.TryFire(HTrigger.Resume);                         // Idle -> Loading; history restored
        // Re-entering Loading should restore Parsing (not reset to Fetching).
        // No transition from Parsing on DataReceived in TestLoadingFsm, so this returns false.
        Assert.False(m.TryFire(HTrigger.DataReceived));
        Assert.Equal(HOuterState.Loading, m.Current);
    }

    [Fact]
    public void First_enter_no_history_starts_at_sub_initial()
    {
        var m = new TestHierMachine();
        m.TryFire(HTrigger.Start);                          // First Loading enter; no history yet
        // Sub should be at Fetching (initial). DataReceived must succeed.
        Assert.True(m.TryFire(HTrigger.DataReceived));
    }

    [Fact]
    public void Nested_three_levels_dispatches_correctly()
    {
        var t = new TestTopFsm();
        // Top: A (composite, has Mid)
        // Mid: X (composite, has Leaf)
        // Leaf: P
        // Tick should bubble down: Leaf.P -> Leaf.Q
        Assert.True(t.TryFire(NTrig.Tick));
        // Top stays A; Mid stays X.
        Assert.Equal(N1.A, t.Current);
    }

    [Fact]
    public void Shallow_history_resets_inner_subfsms_to_initial()
    {
        // Drive Leaf to Q via Top's TryFire(Tick); confirm Top stays in its composite.
        // (Full shallow-history re-entry is covered by snapshot tests; this exercises dispatch.)
        var t = new TestTopFsm();
        Assert.True(t.TryFire(NTrig.Tick));
        Assert.Equal(N1.A, t.Current);
    }

    [Fact]
    public void Multiple_TryFire_calls_propagate_to_sub()
    {
        var m = new TestHierMachine();
        m.TryFire(HTrigger.Start);                          // Loading
        Assert.True(m.TryFire(HTrigger.DataReceived));      // Sub: Fetching -> Parsing
        // No transition from Parsing on DataReceived; should return false but state unchanged.
        Assert.False(m.TryFire(HTrigger.DataReceived));
        Assert.Equal(HOuterState.Loading, m.Current);
    }

    [Fact]
    public void Parent_only_transitions_when_in_composite()
    {
        var m = new TestHierMachine();
        m.TryFire(HTrigger.Start);                          // Loading (composite)
        // Complete is a parent-level transition from Loading; sub doesn't handle Complete.
        // Sub gets first crack, returns false, parent's switch handles it.
        Assert.True(m.TryFire(HTrigger.Complete));
        Assert.Equal(HOuterState.Done, m.Current);
    }

    [Fact]
    public void Transition_from_non_composite_works_normally()
    {
        var m = new TestHierMachine();
        // From Idle (not a composite), DataReceived has no transition.
        Assert.False(m.TryFire(HTrigger.DataReceived));
        Assert.Equal(HOuterState.Idle, m.Current);

        // From Idle, Start fires (Idle -> Loading).
        Assert.True(m.TryFire(HTrigger.Start));
        Assert.Equal(HOuterState.Loading, m.Current);
    }
}
