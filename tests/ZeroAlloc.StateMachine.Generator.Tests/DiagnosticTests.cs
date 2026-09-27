using System.Globalization;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.StateMachine.Generator.Tests;

public class DiagnosticTests
{
    [Fact]
    public async Task UnreachableState_ZSM0001_Reported()
    {
        var source = """
            using ZeroAlloc.StateMachine;
            namespace T;
            public enum S { Start, End, Orphan }
            public enum R { Go, Detour }

            [StateMachine(InitialState = nameof(S.Start))]
            [Transition<S, R>(From = S.Start,  On = R.Go,     To = S.End)]
            [Transition<S, R>(From = S.Orphan, On = R.Detour, To = S.End)]
            [Terminal<S>(State = S.End)]
            public partial class TestMachine { }
            """;

        var diagnostics = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        diagnostics.Should().Contain(d => d.Id == "ZSM0001" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task SinkState_ZSM0002_Reported_WhenNoTerminalAnnotation()
    {
        var source = """
            using ZeroAlloc.StateMachine;
            namespace T;
            public enum S { Start, End }
            public enum R { Go }

            [StateMachine(InitialState = nameof(S.Start))]
            [Transition<S, R>(From = S.Start, On = R.Go, To = S.End)]
            public partial class TestMachine { }
            """;

        var diagnostics = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        diagnostics.Should().Contain(d => d.Id == "ZSM0002" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task SinkState_ZSM0002_NotReported_WhenTerminalAnnotated()
    {
        var source = """
            using ZeroAlloc.StateMachine;
            namespace T;
            public enum S { Start, End }
            public enum R { Go }

            [StateMachine(InitialState = nameof(S.Start))]
            [Transition<S, R>(From = S.Start, On = R.Go, To = S.End)]
            [Terminal<S>(State = S.End)]
            public partial class TestMachine { }
            """;

        var diagnostics = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        diagnostics.Should().NotContain(d => d.Id == "ZSM0002");
    }

    [Fact]
    public async Task SingleUseTrigger_ZSM0003_NotReported_ForCanonicalCircuitBreaker()
    {
        // Trip is shared by Closed and HalfOpen; Probe and Reset are each used once
        // because each state has its own exit event. None of them is a typo.
        var source = """
            using ZeroAlloc.StateMachine;
            namespace T;
            public enum CbState   { Closed, Open, HalfOpen }
            public enum CbTrigger { Trip, Probe, Reset }

            [StateMachine(InitialState = nameof(CbState.Closed))]
            [Transition<CbState, CbTrigger>(From = CbState.Closed,   On = CbTrigger.Trip,  To = CbState.Open)]
            [Transition<CbState, CbTrigger>(From = CbState.Open,     On = CbTrigger.Probe, To = CbState.HalfOpen)]
            [Transition<CbState, CbTrigger>(From = CbState.HalfOpen, On = CbTrigger.Reset, To = CbState.Closed)]
            [Transition<CbState, CbTrigger>(From = CbState.HalfOpen, On = CbTrigger.Trip,  To = CbState.Open)]
            public partial class TestMachine { }
            """;

        var diagnostics = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        diagnostics.Should().NotContain(d => d.Id == "ZSM0003");
    }

    [Fact]
    public async Task SingleUseTrigger_ZSM0003_Reported_ForTypoOfReusedTrigger()
    {
        var source = """
            using ZeroAlloc.StateMachine;
            namespace T;
            public enum CbState   { Closed, Open, HalfOpen }
            public enum CbTrigger { Trip, Tirp, Probe, Reset }

            [StateMachine(InitialState = nameof(CbState.Closed))]
            [Transition<CbState, CbTrigger>(From = CbState.Closed,   On = CbTrigger.Trip,  To = CbState.Open)]
            [Transition<CbState, CbTrigger>(From = CbState.Open,     On = CbTrigger.Probe, To = CbState.HalfOpen)]
            [Transition<CbState, CbTrigger>(From = CbState.HalfOpen, On = CbTrigger.Reset, To = CbState.Closed)]
            [Transition<CbState, CbTrigger>(From = CbState.HalfOpen, On = CbTrigger.Trip,  To = CbState.Open)]
            [Transition<CbState, CbTrigger>(From = CbState.Open,     On = CbTrigger.Tirp,  To = CbState.Open)]
            public partial class TestMachine { }
            """;

        var diagnostics = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        var zsm0003 = diagnostics.Where(d => string.Equals(d.Id, "ZSM0003", StringComparison.Ordinal)).ToList();
        zsm0003.Should().ContainSingle();
        zsm0003[0].Severity.Should().Be(DiagnosticSeverity.Warning);
        zsm0003[0].GetMessage(CultureInfo.InvariantCulture)
            .Should().Be("Trigger 'Tirp' on 'TestMachine' is used once; did you mean 'Trip'?");
    }

    [Fact]
    public async Task SingleUseTrigger_ZSM0003_Reported_ForCaseOnlyDifference()
    {
        var source = """
            using ZeroAlloc.StateMachine;
            namespace T;
            public enum S { A, B, C }
            public enum R { Advance, advance }

            [StateMachine(InitialState = nameof(S.A))]
            [Transition<S, R>(From = S.A, On = R.Advance, To = S.B)]
            [Transition<S, R>(From = S.B, On = R.Advance, To = S.C)]
            [Transition<S, R>(From = S.C, On = R.advance, To = S.A)]
            public partial class TestMachine { }
            """;

        var diagnostics = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        var zsm0003 = diagnostics.Where(d => string.Equals(d.Id, "ZSM0003", StringComparison.Ordinal)).ToList();
        zsm0003.Should().ContainSingle();
        zsm0003[0].GetMessage(CultureInfo.InvariantCulture)
            .Should().Be("Trigger 'advance' on 'TestMachine' is used once; did you mean 'Advance'?");
    }

    [Fact]
    public async Task SingleUseTrigger_ZSM0003_Reported_ForTwoEditsOnLongName()
    {
        // Two edits are within reach for names of five or more characters.
        var source = """
            using ZeroAlloc.StateMachine;
            namespace T;
            public enum S { A, B, C }
            public enum R { Submit, Submti, Sbumti }

            [StateMachine(InitialState = nameof(S.A))]
            [Transition<S, R>(From = S.A, On = R.Submit, To = S.B)]
            [Transition<S, R>(From = S.B, On = R.Submit, To = S.C)]
            [Transition<S, R>(From = S.C, On = R.Submti, To = S.A)]
            [Transition<S, R>(From = S.B, On = R.Sbumti, To = S.A)]
            public partial class TestMachine { }
            """;

        var diagnostics = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        diagnostics.Where(d => string.Equals(d.Id, "ZSM0003", StringComparison.Ordinal))
            .Select(d => d.GetMessage(CultureInfo.InvariantCulture))
            .Should().Equal(
                "Trigger 'Submti' on 'TestMachine' is used once; did you mean 'Submit'?",
                "Trigger 'Sbumti' on 'TestMachine' is used once; did you mean 'Submit'?");
    }

    [Fact]
    public async Task SingleUseTrigger_ZSM0003_NotReported_ForTwoEditsOnShortName()
    {
        // Short names only tolerate one edit, so Tap is not treated as a typo of Trip.
        var source = """
            using ZeroAlloc.StateMachine;
            namespace T;
            public enum S { A, B, C }
            public enum R { Trip, Tap }

            [StateMachine(InitialState = nameof(S.A))]
            [Transition<S, R>(From = S.A, On = R.Trip, To = S.B)]
            [Transition<S, R>(From = S.B, On = R.Trip, To = S.C)]
            [Transition<S, R>(From = S.C, On = R.Tap,  To = S.A)]
            public partial class TestMachine { }
            """;

        var diagnostics = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        diagnostics.Should().NotContain(d => d.Id == "ZSM0003");
    }

    [Fact]
    public async Task SingleUseTrigger_ZSM0003_NotReported_ForDistantName()
    {
        // Back is used once and Go twice, but the names are not alike, so Back is not a typo.
        var source = """
            using ZeroAlloc.StateMachine;
            namespace T;
            public enum S { A, B, C }
            public enum R { Go, Back }

            [StateMachine(InitialState = nameof(S.A))]
            [Transition<S, R>(From = S.A, On = R.Go,   To = S.B)]
            [Transition<S, R>(From = S.B, On = R.Go,   To = S.C)]
            [Transition<S, R>(From = S.C, On = R.Back, To = S.A)]
            public partial class TestMachine { }
            """;

        var diagnostics = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        diagnostics.Should().NotContain(d => d.Id == "ZSM0003");
    }

    [Fact]
    public async Task SingleUseTrigger_ZSM0003_NotReported_WhenNoTriggerIsReused()
    {
        // Near names that are each used once have no reused trigger to point at.
        var source = """
            using ZeroAlloc.StateMachine;
            namespace T;
            public enum S { A, B, C }
            public enum R { Trip, Tirp, Stop }

            [StateMachine(InitialState = nameof(S.A))]
            [Transition<S, R>(From = S.A, On = R.Trip, To = S.B)]
            [Transition<S, R>(From = S.B, On = R.Tirp, To = S.C)]
            [Transition<S, R>(From = S.C, On = R.Stop, To = S.A)]
            public partial class TestMachine { }
            """;

        var diagnostics = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        diagnostics.Should().NotContain(d => d.Id == "ZSM0003");
    }

    [Fact]
    public async Task ValidMachine_NoDiagnostics()
    {
        var source = """
            using ZeroAlloc.StateMachine;
            namespace T;
            public enum S { Idle, Running, Done }
            public enum R { Start, Finish }

            [StateMachine(InitialState = nameof(S.Idle))]
            [Transition<S, R>(From = S.Idle,    On = R.Start,  To = S.Running)]
            [Transition<S, R>(From = S.Running, On = R.Finish, To = S.Done)]
            [Terminal<S>(State = S.Done)]
            public partial class TestMachine { }
            """;

        var diagnostics = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Warning || d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task StructConcurrent_ZSM0004_Reported()
    {
        var source = """
            using ZeroAlloc.StateMachine;
            namespace T;
            public enum S { A, B }
            public enum R { Go }

            [StateMachine(InitialState = nameof(S.A), Concurrent = true)]
            [Transition<S, R>(From = S.A, On = R.Go, To = S.B)]
            [Terminal<S>(State = S.B)]
            public partial struct TestMachine { }
            """;

        var diagnostics = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        diagnostics.Should().Contain(d => d.Id == "ZSM0004" && d.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("public TestMachine() { }")]
    [InlineData("public TestMachine(int seed) { _ = seed; }")]
    public async Task StructMachine_Compiles_WithAnyUserConstructorShape(string userCtor)
    {
        var source = $$"""
            using ZeroAlloc.StateMachine;
            namespace T;
            public enum S { A, B }
            public enum R { Go }

            [StateMachine(InitialState = nameof(S.A))]
            [Transition<S, R>(From = S.A, On = R.Go, To = S.B)]
            [Terminal<S>(State = S.B)]
            public partial struct TestMachine
            {
                {{userCtor}}
            }
            """;

        var diagnostics = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ZSM0005_CompositeOnConcurrent_EmitsError()
    {
        var source = """
            using ZeroAlloc.StateMachine;
            namespace MyApp;

            public enum SubState { A, B }
            public enum State    { X, Y }
            public enum Trigger  { Go }

            [StateMachine(InitialState = nameof(SubState.A))]
            [Transition<SubState, Trigger>(From = SubState.A, On = Trigger.Go, To = SubState.B)]
            public partial class SubFsm { }

            [StateMachine(InitialState = nameof(State.X), Concurrent = true)]
            [Transition<State, Trigger>(From = State.X, On = Trigger.Go, To = State.Y)]
            [CompositeState<State>(State = State.X, SubMachine = typeof(SubFsm))]
            public partial class Parent { }
            """;

        var diagnostics = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        diagnostics.Should().Contain(d => d.Id == "ZSM0005");
    }

    [Fact]
    public async Task ZSM0006_SubMachineNotStateMachine_EmitsError()
    {
        var source = """
            using ZeroAlloc.StateMachine;
            namespace MyApp;

            public enum State   { X, Y }
            public enum Trigger { Go }

            // NOT a [StateMachine]:
            public partial class NotAFsm { }

            [StateMachine(InitialState = nameof(State.X))]
            [Transition<State, Trigger>(From = State.X, On = Trigger.Go, To = State.Y)]
            [CompositeState<State>(State = State.X, SubMachine = typeof(NotAFsm))]
            public partial class Parent { }
            """;

        var diagnostics = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        diagnostics.Should().Contain(d => d.Id == "ZSM0006");
    }

    [Fact]
    public async Task ZSM0007_SubMachineTriggerMismatch_EmitsError()
    {
        var source = """
            using ZeroAlloc.StateMachine;
            namespace MyApp;

            public enum SubState     { A, B }
            public enum SubTrigger   { SubGo }       // DIFFERENT trigger enum
            public enum State        { X, Y }
            public enum Trigger      { Go }

            [StateMachine(InitialState = nameof(SubState.A))]
            [Transition<SubState, SubTrigger>(From = SubState.A, On = SubTrigger.SubGo, To = SubState.B)]
            public partial class SubFsm { }

            [StateMachine(InitialState = nameof(State.X))]
            [Transition<State, Trigger>(From = State.X, On = Trigger.Go, To = State.Y)]
            [CompositeState<State>(State = State.X, SubMachine = typeof(SubFsm))]
            public partial class Parent { }
            """;

        var diagnostics = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        diagnostics.Should().Contain(d => d.Id == "ZSM0007");
    }

    [Fact]
    public async Task ZSM0008_CompositeStateValueNotInTState_EmitsError()
    {
        var source = """
            using ZeroAlloc.StateMachine;
            namespace MyApp;

            public enum SubState     { A }
            public enum State        { X, Y }
            public enum OtherState   { Z }       // not the parent's TState
            public enum Trigger      { Go }

            [StateMachine(InitialState = nameof(SubState.A))]
            [Transition<SubState, Trigger>(From = SubState.A, On = Trigger.Go, To = SubState.A)]
            public partial class SubFsm { }

            [StateMachine(InitialState = nameof(State.X))]
            [Transition<State, Trigger>(From = State.X, On = Trigger.Go, To = State.Y)]
            [CompositeState<OtherState>(State = OtherState.Z, SubMachine = typeof(SubFsm))]
            public partial class Parent { }
            """;

        var diagnostics = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        diagnostics.Should().Contain(d => d.Id == "ZSM0008");
    }

    [Fact]
    public async Task ZSM0009_DuplicateCompositeState_EmitsError()
    {
        var source = """
            using ZeroAlloc.StateMachine;
            namespace MyApp;

            public enum SubState { A }
            public enum State    { X, Y }
            public enum Trigger  { Go }

            [StateMachine(InitialState = nameof(SubState.A))]
            [Transition<SubState, Trigger>(From = SubState.A, On = Trigger.Go, To = SubState.A)]
            public partial class SubFsm1 { }

            [StateMachine(InitialState = nameof(SubState.A))]
            [Transition<SubState, Trigger>(From = SubState.A, On = Trigger.Go, To = SubState.A)]
            public partial class SubFsm2 { }

            [StateMachine(InitialState = nameof(State.X))]
            [Transition<State, Trigger>(From = State.X, On = Trigger.Go, To = State.Y)]
            [CompositeState<State>(State = State.X, SubMachine = typeof(SubFsm1))]
            [CompositeState<State>(State = State.X, SubMachine = typeof(SubFsm2))]  // dup
            public partial class Parent { }
            """;

        var diagnostics = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        diagnostics.Should().Contain(d => d.Id == "ZSM0009");
    }

    [Fact]
    public async Task ZSM0010_HistoryWithoutComposite_EmitsError()
    {
        var source = """
            using ZeroAlloc.StateMachine;
            namespace MyApp;

            public enum State   { X, Y }
            public enum Trigger { Go }

            [StateMachine(InitialState = nameof(State.X))]
            [Transition<State, Trigger>(From = State.X, On = Trigger.Go, To = State.Y)]
            [HistoryState<State>(State = State.X)]   // no matching [CompositeState]
            public partial class Parent { }
            """;

        var diagnostics = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        diagnostics.Should().Contain(d => d.Id == "ZSM0010");
    }

    [Fact]
    public async Task ZSM0011_CompositeAndTerminalOnSameState_EmitsError()
    {
        var source = """
            using ZeroAlloc.StateMachine;
            namespace MyApp;

            public enum SubState { A }
            public enum State    { X, Y }
            public enum Trigger  { Go }

            [StateMachine(InitialState = nameof(SubState.A))]
            [Transition<SubState, Trigger>(From = SubState.A, On = Trigger.Go, To = SubState.A)]
            public partial class SubFsm { }

            [StateMachine(InitialState = nameof(State.X))]
            [Transition<State, Trigger>(From = State.X, On = Trigger.Go, To = State.Y)]
            [CompositeState<State>(State = State.X, SubMachine = typeof(SubFsm))]
            [Terminal<State>(State = State.X)]   // contradictory
            public partial class Parent { }
            """;

        var diagnostics = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        diagnostics.Should().Contain(d => d.Id == "ZSM0011");
    }

    [Fact]
    public async Task ZSM0012_FiresWhen_AfterMs_OnNonConcurrentClass()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B }
public enum T { Go }
[StateMachine(InitialState = ""A"")]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, AfterMs = 1000)]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.Contains(diags, d => string.Equals(d.Id, "ZSM0012", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ZSM0013_FiresWhen_AfterMs_IsNegative()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B }
public enum T { Go }
[StateMachine(InitialState = ""A"", Concurrent = true)]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, AfterMs = -1)]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.Contains(diags, d => string.Equals(d.Id, "ZSM0013", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ZSM0014_FiresWhen_BothStateMachineAndGroup()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B } public enum T { Go }
[StateMachine(InitialState = ""A"")]
[StateMachineGroup]
[StateMachinePart<S, T>(Name = ""P"", InitialState = S.A)]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B)]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.Contains(diags, d => string.Equals(d.Id, "ZSM0014", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ZSM0015_FiresWhen_DuplicatePartNames()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B } public enum T { Go }
[StateMachineGroup]
[StateMachinePart<S, T>(Name = ""P"", InitialState = S.A)]
[StateMachinePart<S, T>(Name = ""P"", InitialState = S.A)]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, Part = ""P"")]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.Contains(diags, d => string.Equals(d.Id, "ZSM0015", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ZSM0016_FiresWhen_TransitionPart_IsUnknown()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B } public enum T { Go }
[StateMachineGroup]
[StateMachinePart<S, T>(Name = ""P"", InitialState = S.A)]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, Part = ""DoesNotExist"")]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.Contains(diags, d => string.Equals(d.Id, "ZSM0016", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ZSM0017_FiresWhen_GroupHasNoParts()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
[StateMachineGroup]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.Contains(diags, d => string.Equals(d.Id, "ZSM0017", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ZSM0018_FiresWhen_CompositeInGroup()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B } public enum T { Go }
public enum SubS { X, Y }
[StateMachine(InitialState = ""X"")]
[Transition<SubS, T>(From = SubS.X, On = T.Go, To = SubS.Y)]
public partial class Sub { }

[StateMachineGroup]
[StateMachinePart<S, T>(Name = ""P"", InitialState = S.A)]
[CompositeState<S>(State = S.A, SubMachine = typeof(Sub))]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, Part = ""P"")]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.Contains(diags, d => string.Equals(d.Id, "ZSM0018", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ZSM0019_FiresWhen_UserDispose_HasWrongSignature()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B } public enum T { Go }
[StateMachine(InitialState = ""A"", Concurrent = true)]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, AfterMs = 1000)]
public partial class M
{
    private void Dispose() { }   // wrong: private (gen wants public)
}
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.Contains(diags, d => string.Equals(d.Id, "ZSM0019", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ZSM0020_FiresWhen_Diagram_OnEmptyClass()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A } public enum T { Go }
[StateMachine(InitialState = ""A"", Diagram = true)]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.Contains(diags, d => string.Equals(d.Id, "ZSM0020", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ZSM0020_FiresWhen_Diagram_OnEmptyGroup()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
[StateMachineGroup(Diagram = true)]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.Contains(diags, d => string.Equals(d.Id, "ZSM0020", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ZSM0021_FiresWhen_UserCtor_DoesNotCall_HookConstructor()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B } public enum T { Go }
[StateMachine(InitialState = ""A"", Concurrent = true)]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, AfterMs = 1000)]
public partial class M
{
    public M(int x) { /* does NOT call HookConstructor */ }
}
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.Contains(diags, d => string.Equals(d.Id, "ZSM0021", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ZSM0021_DoesNotFire_When_UserCtor_Calls_HookConstructor()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B } public enum T { Go }
[StateMachine(InitialState = ""A"", Concurrent = true)]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, AfterMs = 1000)]
public partial class M
{
    public M(int x) { HookConstructor(); }
}
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.DoesNotContain(diags, d => string.Equals(d.Id, "ZSM0021", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ZSM0021_DoesNotFire_When_UserCtor_Calls_ThisHookConstructor()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B } public enum T { Go }
[StateMachine(InitialState = ""A"", Concurrent = true)]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, AfterMs = 1000)]
public partial class M
{
    public M(int x) { this.HookConstructor(); }
}
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.DoesNotContain(diags, d => string.Equals(d.Id, "ZSM0021", StringComparison.Ordinal));
    }
    [Fact]
    public async Task ZSM0022_FiresWhen_Guard_OnConcurrentMachine()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B } public enum T { Go }
[StateMachine(InitialState = ""A"", Concurrent = true)]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, When = true)]
[Terminal<S>(State = S.B)]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        var zsm0022 = diags.Should().ContainSingle(d => string.Equals(d.Id, "ZSM0022", StringComparison.Ordinal)).Which;
        Assert.Equal(DiagnosticSeverity.Warning, zsm0022.Severity);
        Assert.False(zsm0022.IsSuppressed);
        Assert.True(zsm0022.Location.IsInSource);
        Assert.Equal("When = true", zsm0022.Location.SourceTree!.GetText().ToString(zsm0022.Location.SourceSpan));
        Assert.Equal(
            "[Transition(From = S.A, On = T.Go, To = S.B, When = true)] on 'M': When = true is ignored on a concurrent machine, "
            + "so this transition fires unconditionally. A guard cannot be evaluated atomically with the compare-and-swap "
            + "that commits the transition (TOCTOU race). Encode the condition as a state, or check it before calling TryFire.",
            zsm0022.GetMessage(CultureInfo.InvariantCulture));
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ZSM0022_FiresOncePerGuardedTransition()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B, C } public enum T { Go, Skip, Back }
[StateMachine(InitialState = ""A"", Concurrent = true)]
[Transition<S, T>(From = S.A, On = T.Go,   To = S.B, When = true)]
[Transition<S, T>(From = S.A, On = T.Skip, To = S.C, When = true)]
[Transition<S, T>(From = S.B, On = T.Back, To = S.A)]
[Terminal<S>(State = S.C)]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        var zsm0022 = diags.Where(d => string.Equals(d.Id, "ZSM0022", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, zsm0022.Count);
        Assert.Contains(zsm0022, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("On = T.Go,", StringComparison.Ordinal));
        Assert.Contains(zsm0022, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("On = T.Skip,", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ZSM0022_FiresWhen_Guard_InStateMachinePart()
    {
        // Parts of a [StateMachineGroup] are always concurrent, so their guards are dropped too.
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B } public enum T { Go }
[StateMachineGroup]
[StateMachinePart<S, T>(Name = ""P"", InitialState = S.A)]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, Part = ""P"", When = true)]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        var zsm0022 = diags.Should().ContainSingle(d => string.Equals(d.Id, "ZSM0022", StringComparison.Ordinal)).Which;
        Assert.Equal(DiagnosticSeverity.Warning, zsm0022.Severity);
        Assert.Equal("When = true", zsm0022.Location.SourceTree!.GetText().ToString(zsm0022.Location.SourceSpan));
    }

    [Fact]
    public async Task ZSM0022_DoesNotFire_When_Guard_OnSequentialMachine()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B } public enum T { Go }
[StateMachine(InitialState = ""A"")]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, When = true)]
[Terminal<S>(State = S.B)]
public partial class M
{
    private partial bool GuardGo(S from, T on) => true;
}
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.DoesNotContain(diags, d => string.Equals(d.Id, "ZSM0022", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData(", When = false")]
    public async Task ZSM0022_DoesNotFire_When_NoGuard_OnConcurrentMachine(string when)
    {
        var source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B } public enum T { Go }
[StateMachine(InitialState = ""A"", Concurrent = true)]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B" + when + @")]
[Terminal<S>(State = S.B)]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.DoesNotContain(diags, d => string.Equals(d.Id, "ZSM0022", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ZSM0022_IsSuppressedBy_PragmaWarningDisable()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B } public enum T { Go }
[StateMachine(InitialState = ""A"", Concurrent = true)]
#pragma warning disable ZSM0022
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, When = true)]
#pragma warning restore ZSM0022
[Terminal<S>(State = S.B)]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        // The generator driver applies #pragma to generator diagnostics by marking them
        // suppressed, which the compiler then leaves out of the build output.
        var zsm0022 = diags.Should().ContainSingle(d => string.Equals(d.Id, "ZSM0022", StringComparison.Ordinal)).Which;
        Assert.True(zsm0022.IsSuppressed);
    }
}
