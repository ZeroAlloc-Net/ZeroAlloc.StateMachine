using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.StateMachine.Generator.Tests;

/// <summary>
/// Roslyn compares hint names ignoring case, so two hosts whose qualified names differ only in
/// case need the same file. The host declared first is generated; each later one gets ZSM0025
/// and is skipped, and every other host is still generated.
/// </summary>
public class CaseCollisionTests
{
    private const string Enums = @"
public enum S { A, B }
public enum T { Go, Back }
";

    private const string Machine = @"
[StateMachine(InitialState = nameof(S.A))]
[Transition<S, T>(From = S.A, On = T.Go,   To = S.B)]
[Transition<S, T>(From = S.B, On = T.Back, To = S.A)]
";

    private const string Group = @"
[StateMachineGroup]
[StateMachinePart<S, T>(Name = ""P"", InitialState = S.A)]
[Transition<S, T>(From = S.A, On = T.Go,   To = S.B, Part = ""P"")]
[Transition<S, T>(From = S.B, On = T.Back, To = S.A, Part = ""P"")]
";

    [Fact]
    public void MachinesDifferingOnlyInCase_ReportTheLaterOne_AndGenerateTheRest()
    {
        var source = $@"
using ZeroAlloc.StateMachine;
namespace App;
{Enums}
{Machine} public partial class M {{ }}
{Machine} public partial class m {{ }}
{Machine} public partial class Other {{ }}
";
        var run = GeneratorRunner.Run(source);

        var diagnostic = run.GeneratorDiagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("ZSM0025");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Be(
            "'App.m' is not generated because its file name 'App.m.g.cs' differs only in case from that of 'App.M'");
        source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length).Should().Be("m");
        run.HintNames.Should().BeEquivalentTo("App.M.g.cs", "App.Other.g.cs");
        run.GeneratorDiagnostics.Should().NotContain(static d => string.Equals(d.Id, "CS8785", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ThreeGroupsDifferingOnlyInCase_ReportTwo_AndGenerateOne()
    {
        var source = $@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
{Group} public partial class App {{ }}
{Group} public partial class APP {{ }}
{Group} public partial class app {{ }}
";
        var run = GeneratorRunner.Run(source);

        run.GeneratorDiagnostics.Select(static d => d.Id).Should().Equal("ZSM0025", "ZSM0025");
        run.GeneratorDiagnostics.Select(static d => d.GetMessage(CultureInfo.InvariantCulture)).Should().BeEquivalentTo(
            "'N.APP' is not generated because its file name 'N.APP.Group.g.cs' differs only in case from that of 'N.App'",
            "'N.app' is not generated because its file name 'N.app.Group.g.cs' differs only in case from that of 'N.App'");
        run.HintNames.Should().BeEquivalentTo("N.App.Group.g.cs");
    }

    [Fact]
    public void AcrossFiles_TheHostInTheEarlierFilePathWins_WhateverTheCompilationOrder()
    {
        var later = ("b/Later.cs", $"using ZeroAlloc.StateMachine;\nnamespace App;\n{Machine} public partial class m {{ }}\n");
        var earlier = ("a/Earlier.cs", $"using ZeroAlloc.StateMachine;\nnamespace App;\n{Enums}\n{Machine} public partial class M {{ }}\n");

        foreach (var files in new[] { new[] { later, earlier }, new[] { earlier, later } })
        {
            var run = GeneratorRunner.Run(files);

            var diagnostic = run.GeneratorDiagnostics.Should().ContainSingle().Subject;
            diagnostic.Id.Should().Be("ZSM0025");
            diagnostic.Location.SourceTree!.FilePath.Should().Be("b/Later.cs");
            run.Sources.Should().ContainSingle().Which.SourceText.ToString().Should().Contain("partial class M");
        }
    }

    [Fact]
    public void AMachineAndAGroupDifferingOnlyInCase_UseDifferentFiles_AndAreBothGenerated()
    {
        var run = GeneratorRunner.Run($@"
using ZeroAlloc.StateMachine;
namespace App;
{Enums}
{Machine} public partial class M {{ }}
{Group} public partial class m {{ }}
");

        run.GeneratorDiagnostics.Should().BeEmpty();
        run.HintNames.Should().BeEquivalentTo("App.M.g.cs", "App.m.Group.g.cs");
    }
}
