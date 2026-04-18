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
    public async Task SingleUseTrigger_ZSM0003_Reported()
    {
        var source = """
            using ZeroAlloc.StateMachine;
            namespace T;
            public enum S { A, B, C }
            public enum R { Go, Back }

            [StateMachine(InitialState = nameof(S.A))]
            [Transition<S, R>(From = S.A, On = R.Go,   To = S.B)]
            [Transition<S, R>(From = S.B, On = R.Go,   To = S.C)]
            [Transition<S, R>(From = S.C, On = R.Back, To = S.A)]
            [Terminal<S>(State = S.C)]
            public partial class TestMachine { }
            """;

        var diagnostics = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        diagnostics.Should().Contain(d => d.Id == "ZSM0003");
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
}
