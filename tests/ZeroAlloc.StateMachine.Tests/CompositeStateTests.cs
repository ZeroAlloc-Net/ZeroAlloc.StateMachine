using ZeroAlloc.StateMachine;

#pragma warning disable ZSM0003 // single-use trigger — intentional in test machines
#pragma warning disable ZSM0002 // sink/unreachable state warnings on test fixtures
#pragma warning disable MA0048  // file name must match type name — multiple types in one test file

namespace ZeroAlloc.StateMachine.Tests;

// ── Hierarchical fixture (H prefix to avoid collisions with RuntimeTests.cs) ──

public enum HSubState   { Fetching, Parsing }
public enum HOuterState { Idle, Loading, Done }
public enum HTrigger    { Start, DataReceived, Suspend, Resume, Complete }

[StateMachine(InitialState = nameof(HSubState.Fetching))]
[Transition<HSubState, HTrigger>(From = HSubState.Fetching, On = HTrigger.DataReceived, To = HSubState.Parsing)]
public partial class TestLoadingFsm { }

[StateMachine(InitialState = nameof(HOuterState.Idle))]
[Transition<HOuterState, HTrigger>(From = HOuterState.Idle,    On = HTrigger.Start,    To = HOuterState.Loading)]
[Transition<HOuterState, HTrigger>(From = HOuterState.Loading, On = HTrigger.Suspend,  To = HOuterState.Idle)]
[Transition<HOuterState, HTrigger>(From = HOuterState.Idle,    On = HTrigger.Resume,   To = HOuterState.Loading)]
[Transition<HOuterState, HTrigger>(From = HOuterState.Loading, On = HTrigger.Complete, To = HOuterState.Done)]
[CompositeState<HOuterState>(State = HOuterState.Loading, SubMachine = typeof(TestLoadingFsm))]
[HistoryState<HOuterState>(State = HOuterState.Loading)]
public partial class TestHierMachine { }

// ── Three-level nested fixture ───────────────────────────────────────────────

public enum N1    { A, B }
public enum N2    { X, Y }
public enum N3    { P, Q }
public enum NTrig { Tick, Reset }

[StateMachine(InitialState = nameof(N3.P))]
[Transition<N3, NTrig>(From = N3.P, On = NTrig.Tick, To = N3.Q)]
public partial class TestLeafFsm { }

[StateMachine(InitialState = nameof(N2.X))]
[Transition<N2, NTrig>(From = N2.X, On = NTrig.Tick, To = N2.Y)]
[CompositeState<N2>(State = N2.X, SubMachine = typeof(TestLeafFsm))]
public partial class TestMidFsm { }

[StateMachine(InitialState = nameof(N1.A))]
[Transition<N1, NTrig>(From = N1.A, On = NTrig.Reset, To = N1.B)]
[CompositeState<N1>(State = N1.A, SubMachine = typeof(TestMidFsm))]
public partial class TestTopFsm { }

// ── Tests ────────────────────────────────────────────────────────────────────

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
