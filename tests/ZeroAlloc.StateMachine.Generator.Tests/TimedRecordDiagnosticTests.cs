using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.StateMachine.Generator.Tests;

/// <summary>
/// ZSM0026: a record's compiler-generated copy constructor copies the timer fields, so a copy
/// made with <c>with</c> shares the original's timers (#162). Reported once per record, at the
/// first <c>AfterMs</c> argument. The machine is still generated.
/// </summary>
public class TimedRecordDiagnosticTests
{
    private const string Enums = @"
public enum S { A, B }
public enum T { Go, Back }
";

    [Fact]
    public void TimedRecordMachine_ReportsZsm0026_OnceAtTheFirstAfterMs_AndIsStillGenerated()
    {
        var source = $@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
[StateMachine(InitialState = nameof(S.A), Concurrent = true)]
[Transition<S, T>(From = S.A, On = T.Go,   To = S.B, AfterMs = 1000)]
[Transition<S, T>(From = S.B, On = T.Back, To = S.A, AfterMs = 2000)]
public partial record M;
";
        var run = GeneratorRunner.Run(source);

        var diagnostic = run.GeneratorDiagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("ZSM0026");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Be(
            "'M' is a record with timed transitions: a copy made with 'with' shares the original's timers, so disposing the copy stops the original's timers");
        source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length)
            .Should().Be("AfterMs = 1000");
        run.HintNames.Should().Equal("N.M.g.cs");
        run.CompilationErrors.Should().BeEmpty();
    }

    [Fact]
    public void TimedRecordGroup_ReportsZsm0026()
    {
        var source = $@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
[StateMachineGroup]
[StateMachinePart<S, T>(Name = ""P"", InitialState = S.A)]
[Transition<S, T>(From = S.A, On = T.Go,   To = S.B, Part = ""P"")]
[Transition<S, T>(From = S.B, On = T.Back, To = S.A, Part = ""P"", AfterMs = 500)]
public partial record G;
";
        var run = GeneratorRunner.Run(source);

        var diagnostic = run.GeneratorDiagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("ZSM0026");
        source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length)
            .Should().Be("AfterMs = 500");
        run.HintNames.Should().Equal("N.G.Group.g.cs");
    }

    [Theory]
    [InlineData("[StateMachine(InitialState = nameof(S.A))] [Transition<S, T>(From = S.A, On = T.Go, To = S.B)] [Transition<S, T>(From = S.B, On = T.Back, To = S.A)] public partial record M;")]
    [InlineData("[StateMachine(InitialState = nameof(S.A), Concurrent = true)] [Transition<S, T>(From = S.A, On = T.Go, To = S.B, AfterMs = 5)] [Transition<S, T>(From = S.B, On = T.Back, To = S.A)] public partial class M;")]
    [InlineData("public partial record Outer { [StateMachine(InitialState = nameof(S.A), Concurrent = true)] [Transition<S, T>(From = S.A, On = T.Go, To = S.B, AfterMs = 5)] [Transition<S, T>(From = S.B, On = T.Back, To = S.A)] public partial class M; }")]
    public void RecordsWithoutTimersAndTimedClasses_ReportNothing(string declaration)
    {
        var run = GeneratorRunner.Run($"using ZeroAlloc.StateMachine;\nnamespace N;\n{Enums}\n{declaration}");

        run.GeneratorDiagnostics.Should().BeEmpty();
        run.CompilationErrors.Should().BeEmpty();
    }

    // A record struct cannot be timed: AfterMs needs Concurrent = true, which a struct cannot
    // have. Its errors are the existing ones, not ZSM0026.
    [Theory]
    [InlineData("Concurrent = true, ", "ZSM0004")]
    [InlineData("", "ZSM0012")]
    public void TimedRecordStruct_ReportsOnlyTheStructErrors(string concurrent, string id)
    {
        var run = GeneratorRunner.Run($@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
[StateMachine({concurrent}InitialState = nameof(S.A))]
[Transition<S, T>(From = S.A, On = T.Go,   To = S.B, AfterMs = 5)]
[Transition<S, T>(From = S.B, On = T.Back, To = S.A)]
public partial record struct M;
");

        run.GeneratorDiagnostics.Select(static d => d.Id).Should().Equal(id);
    }
}
