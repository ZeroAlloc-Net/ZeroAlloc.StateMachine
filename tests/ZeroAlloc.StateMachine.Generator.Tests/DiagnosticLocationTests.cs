using Microsoft.CodeAnalysis;

namespace ZeroAlloc.StateMachine.Generator.Tests;

/// <summary>
/// Every ZSM diagnostic points at the attribute, argument or member it is about, as a source
/// location in the compilation's own tree. Reported at the class identifier, the IDE could not
/// show which transition or part was wrong, and a <c>#pragma warning disable</c> around the
/// offending attribute did not cover it, so a rule could only be silenced for the whole project.
/// </summary>
public class DiagnosticLocationTests
{
    [Fact]
    public async Task ZSM0001_PointsAtTheFromOfTheUnreachableState()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
            public enum S { Start, End, Orphan }
            public enum R { Go, Detour }
            [StateMachine(InitialState = nameof(S.Start))]
            [Transition<S, R>(From = S.Start,  On = R.Go,     To = S.End)]
            [Transition<S, R>(From = S.Orphan, On = R.Detour, To = S.End)]
            [Terminal<S>(State = S.End)]
            public partial class M { }
            """;

        await AssertLocatedAt(source, "ZSM0001", "From = S.Orphan");
    }

    [Fact]
    public async Task ZSM0002_PointsAtTheToOfTheSinkState()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
            public enum S { Start, Middle, End }
            public enum R { Go, Next }
            [StateMachine(InitialState = nameof(S.Start))]
            [Transition<S, R>(From = S.Start,  On = R.Go,   To = S.Middle)]
            [Transition<S, R>(From = S.Middle, On = R.Next, To = S.End)]
            public partial class M { }
            """;

        await AssertLocatedAt(source, "ZSM0002", "To = S.End");
    }

    [Fact]
    public async Task ZSM0003_PointsAtTheOnOfTheSingleUseTrigger()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
            public enum S { A, B, C }
            public enum R { Submit, Sbumit }
            [StateMachine(InitialState = nameof(S.A))]
            [Transition<S, R>(From = S.A, On = R.Submit, To = S.B)]
            [Transition<S, R>(From = S.B, On = R.Submit, To = S.C)]
            [Transition<S, R>(From = S.C, On = R.Sbumit, To = S.A)]
            public partial class M { }
            """;

        await AssertLocatedAt(source, "ZSM0003", "On = R.Sbumit");
    }

    [Fact]
    public async Task ZSM0004_PointsAtTheConcurrentArgument()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
            public enum S { A, B }
            public enum R { Go }
            [StateMachine(InitialState = nameof(S.A), Concurrent = true)]
            [Transition<S, R>(From = S.A, On = R.Go, To = S.B)]
            [Terminal<S>(State = S.B)]
            public partial struct M { }
            """;

        await AssertLocatedAt(source, "ZSM0004", "Concurrent = true");
    }

    [Fact]
    public async Task ZSM0005_PointsAtTheCompositeState()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
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

        await AssertLocatedAt(source, "ZSM0005", "CompositeState<State>(State = State.X, SubMachine = typeof(SubFsm))");
    }

    [Fact]
    public async Task ZSM0006_PointsAtTheSubMachineArgument()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
            public enum State   { X, Y }
            public enum Trigger { Go }
            public partial class NotAFsm { }
            [StateMachine(InitialState = nameof(State.X))]
            [Transition<State, Trigger>(From = State.X, On = Trigger.Go, To = State.Y)]
            [CompositeState<State>(State = State.X, SubMachine = typeof(NotAFsm))]
            public partial class Parent { }
            """;

        await AssertLocatedAt(source, "ZSM0006", "SubMachine = typeof(NotAFsm)");
    }

    [Fact]
    public async Task ZSM0007_PointsAtTheSubMachineArgument()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
            public enum SubState   { A, B }
            public enum SubTrigger { SubGo }
            public enum State      { X, Y }
            public enum Trigger    { Go }
            [StateMachine(InitialState = nameof(SubState.A))]
            [Transition<SubState, SubTrigger>(From = SubState.A, On = SubTrigger.SubGo, To = SubState.B)]
            public partial class SubFsm { }
            [StateMachine(InitialState = nameof(State.X))]
            [Transition<State, Trigger>(From = State.X, On = Trigger.Go, To = State.Y)]
            [CompositeState<State>(State = State.X, SubMachine = typeof(SubFsm))]
            public partial class Parent { }
            """;

        await AssertLocatedAt(source, "ZSM0007", "SubMachine = typeof(SubFsm)");
    }

    [Fact]
    public async Task ZSM0008_PointsAtTheStateArgument()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
            public enum SubState   { A }
            public enum State      { X, Y }
            public enum OtherState { Z }
            public enum Trigger    { Go }
            [StateMachine(InitialState = nameof(SubState.A))]
            [Transition<SubState, Trigger>(From = SubState.A, On = Trigger.Go, To = SubState.A)]
            public partial class SubFsm { }
            [StateMachine(InitialState = nameof(State.X))]
            [Transition<State, Trigger>(From = State.X, On = Trigger.Go, To = State.Y)]
            [CompositeState<OtherState>(State = OtherState.Z, SubMachine = typeof(SubFsm))]
            public partial class Parent { }
            """;

        await AssertLocatedAt(source, "ZSM0008", "State = OtherState.Z");
    }

    [Fact]
    public async Task ZSM0009_PointsAtTheDuplicateCompositeState()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
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
            [CompositeState<State>(State = State.X, SubMachine = typeof(SubFsm2))]
            public partial class Parent { }
            """;

        await AssertLocatedAt(source, "ZSM0009", "CompositeState<State>(State = State.X, SubMachine = typeof(SubFsm2))");
    }

    [Fact]
    public async Task ZSM0010_PointsAtTheHistoryState()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
            public enum State   { X, Y }
            public enum Trigger { Go }
            [StateMachine(InitialState = nameof(State.X))]
            [Transition<State, Trigger>(From = State.X, On = Trigger.Go, To = State.Y)]
            [HistoryState<State>(State = State.X)]
            public partial class Parent { }
            """;

        await AssertLocatedAt(source, "ZSM0010", "HistoryState<State>(State = State.X)");
    }

    [Fact]
    public async Task ZSM0011_PointsAtTheTerminal()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
            public enum SubState { A }
            public enum State    { X, Y }
            public enum Trigger  { Go }
            [StateMachine(InitialState = nameof(SubState.A))]
            [Transition<SubState, Trigger>(From = SubState.A, On = Trigger.Go, To = SubState.A)]
            public partial class SubFsm { }
            [StateMachine(InitialState = nameof(State.X))]
            [Transition<State, Trigger>(From = State.X, On = Trigger.Go, To = State.Y)]
            [CompositeState<State>(State = State.X, SubMachine = typeof(SubFsm))]
            [Terminal<State>(State = State.X)]
            public partial class Parent { }
            """;

        await AssertLocatedAt(source, "ZSM0011", "Terminal<State>(State = State.X)");
    }

    [Fact]
    public async Task ZSM0012_PointsAtTheAfterMsArgument()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
            public enum S { A, B }
            public enum T { Go }
            [StateMachine(InitialState = "A")]
            [Transition<S, T>(From = S.A, On = T.Go, To = S.B, AfterMs = 1000)]
            [Terminal<S>(State = S.B)]
            public partial class M { }
            """;

        await AssertLocatedAt(source, "ZSM0012", "AfterMs = 1000");
    }

    [Fact]
    public async Task ZSM0013_PointsAtTheAfterMsArgument()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
            public enum S { A, B }
            public enum T { Go }
            [StateMachine(InitialState = "A", Concurrent = true)]
            [Transition<S, T>(From = S.A, On = T.Go, To = S.B, AfterMs = -1)]
            [Terminal<S>(State = S.B)]
            public partial class M { }
            """;

        await AssertLocatedAt(source, "ZSM0013", "AfterMs = -1");
    }

    [Fact]
    public async Task ZSM0014_PointsAtTheStateMachineAttribute()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
            public enum S { A, B }
            public enum T { Go }
            [StateMachine(InitialState = "A")]
            [StateMachineGroup]
            [StateMachinePart<S, T>(Name = "P", InitialState = S.A)]
            [Transition<S, T>(From = S.A, On = T.Go, To = S.B, Part = "P")]
            public partial class M { }
            """;

        await AssertLocatedAt(source, "ZSM0014", "StateMachine(InitialState = \"A\")");
    }

    [Fact]
    public async Task ZSM0015_PointsAtTheNameOfTheDuplicatePart()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
            public enum S { A, B }
            public enum T { Go }
            [StateMachineGroup]
            [StateMachinePart<S, T>(Name = "P", InitialState = S.A)]
            [StateMachinePart<S, T>(InitialState = S.B, Name = "P")]
            [Transition<S, T>(From = S.A, On = T.Go, To = S.B, Part = "P")]
            public partial class M { }
            """;

        var diagnostic = await AssertLocatedAt(source, "ZSM0015", "Name = \"P\"");
        Assert.Contains("InitialState = S.B", LineOf(diagnostic), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ZSM0016_PointsAtThePartArgument()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
            public enum S { A, B }
            public enum T { Go }
            [StateMachineGroup]
            [StateMachinePart<S, T>(Name = "P", InitialState = S.A)]
            [Transition<S, T>(From = S.A, On = T.Go, To = S.B, Part = "DoesNotExist")]
            public partial class M { }
            """;

        await AssertLocatedAt(source, "ZSM0016", "Part = \"DoesNotExist\"");
    }

    [Fact]
    public async Task ZSM0016_PointsAtTheTransition_WhenPartIsMissing()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
            public enum S { A, B }
            public enum T { Go }
            [StateMachineGroup]
            [StateMachinePart<S, T>(Name = "P", InitialState = S.A)]
            [Transition<S, T>(From = S.A, On = T.Go, To = S.B)]
            public partial class M { }
            """;

        await AssertLocatedAt(source, "ZSM0016", "Transition<S, T>(From = S.A, On = T.Go, To = S.B)");
    }

    [Fact]
    public async Task ZSM0017_PointsAtTheGroupAttribute()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
            [StateMachineGroup]
            public partial class M { }
            """;

        await AssertLocatedAt(source, "ZSM0017", "StateMachineGroup");
    }

    [Fact]
    public async Task ZSM0018_PointsAtTheCompositeState()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
            public enum S { A, B }
            public enum T { Go }
            public enum SubS { X, Y }
            [StateMachine(InitialState = "X")]
            [Transition<SubS, T>(From = SubS.X, On = T.Go, To = SubS.Y)]
            public partial class Sub { }
            [StateMachineGroup]
            [StateMachinePart<S, T>(Name = "P", InitialState = S.A)]
            [CompositeState<S>(State = S.A, SubMachine = typeof(Sub))]
            [Transition<S, T>(From = S.A, On = T.Go, To = S.B, Part = "P")]
            public partial class M { }
            """;

        await AssertLocatedAt(source, "ZSM0018", "CompositeState<S>(State = S.A, SubMachine = typeof(Sub))");
    }

    [Fact]
    public async Task ZSM0019_PointsAtTheDisposeMethod()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
            public enum S { A, B }
            public enum T { Go }
            [StateMachine(InitialState = "A", Concurrent = true)]
            [Transition<S, T>(From = S.A, On = T.Go, To = S.B, AfterMs = 1000)]
            [Terminal<S>(State = S.B)]
            public partial class M
            {
                private void Dispose() { }
            }
            """;

        var diagnostic = await AssertLocatedAt(source, "ZSM0019", "Dispose");
        Assert.Contains("private void Dispose()", LineOf(diagnostic), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ZSM0020_PointsAtTheDiagramArgument_OnAStateMachine()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
            public enum S { A }
            public enum T { Go }
            [StateMachine(InitialState = "A", Diagram = true)]
            public partial class M { }
            """;

        await AssertLocatedAt(source, "ZSM0020", "Diagram = true");
    }

    [Fact]
    public async Task ZSM0020_PointsAtTheDiagramArgument_OnAGroup()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
            [StateMachineGroup(Diagram = true)]
            public partial class M { }
            """;

        await AssertLocatedAt(source, "ZSM0020", "Diagram = true");
    }

    [Fact]
    public async Task ZSM0021_PointsAtTheConstructor()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
            public enum S { A, B }
            public enum T { Go }
            [StateMachine(InitialState = "A", Concurrent = true)]
            [Transition<S, T>(From = S.A, On = T.Go, To = S.B, AfterMs = 1000)]
            [Terminal<S>(State = S.B)]
            public partial class M
            {
                public M(int x) { }
            }
            """;

        var diagnostic = await AssertLocatedAt(source, "ZSM0021", "M");
        Assert.Contains("public M(int x)", LineOf(diagnostic), StringComparison.Ordinal);
    }

    /// <summary>
    /// A pragma around one transition silences the finding on that transition and on no other:
    /// the diagnostic's location has to fall inside the disabled region.
    /// </summary>
    [Fact]
    public async Task PragmaAroundOneTransition_SuppressesOnlyThatTransitionsDiagnostic()
    {
        const string source = """
            using ZeroAlloc.StateMachine;
            public enum S { Start, Quiet, Loud }
            public enum R { Hush, Shout }
            [StateMachine(InitialState = nameof(S.Start))]
            #pragma warning disable ZSM0002
            [Transition<S, R>(From = S.Start, On = R.Hush,  To = S.Quiet)]
            #pragma warning restore ZSM0002
            [Transition<S, R>(From = S.Start, On = R.Shout, To = S.Loud)]
            public partial class M { }
            """;

        var diagnostics = (await TestHelper.GetDiagnostics<StateMachineGenerator>(source))
            .Where(static d => string.Equals(d.Id, "ZSM0002", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, diagnostics.Count);
        Assert.True(diagnostics.First(d => d.GetMessage().Contains("'Quiet'", StringComparison.Ordinal)).IsSuppressed);
        Assert.False(diagnostics.First(d => d.GetMessage().Contains("'Loud'", StringComparison.Ordinal)).IsSuppressed);
    }

    private static async Task<Diagnostic> AssertLocatedAt(string source, string id, string expectedText)
    {
        var diagnostics = await TestHelper.GetDiagnostics<StateMachineGenerator>(source).ConfigureAwait(false);
        var diagnostic = diagnostics.Should()
            .ContainSingle(d => string.Equals(d.Id, id, StringComparison.Ordinal)).Which;

        Assert.True(diagnostic.Location.IsInSource, diagnostic.ToString());
        Assert.Equal(expectedText, diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
        return diagnostic;
    }

    private static string LineOf(Diagnostic diagnostic)
    {
        var text = diagnostic.Location.SourceTree!.GetText();
        return text.Lines.GetLineFromPosition(diagnostic.Location.SourceSpan.Start).ToString();
    }
}
