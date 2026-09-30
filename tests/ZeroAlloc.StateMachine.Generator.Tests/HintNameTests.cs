using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.StateMachine.Generator.Tests;

/// <summary>
/// Every generated file is named after its host's namespace, containing types and generic
/// arity, so two hosts never share a hint name. A shared hint name makes <c>AddSource</c>
/// throw, and the generator then emits nothing for the whole project, reported as CS8785.
/// </summary>
public class HintNameTests
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
    public void Machines_WhoseNamespaceAndNameJoinTheSame_AreBothGenerated_AndCompile()
    {
        var source = $@"
using ZeroAlloc.StateMachine;
namespace Shared {{ {Enums} }}
namespace A_B {{ using Shared; {Machine} public partial class C {{ }} }}
namespace A {{ using Shared; {Machine} public partial class B_C {{ }} }}
";
        var run = Run(source);

        run.HintNames.Should().BeEquivalentTo("A_B.C.g.cs", "A.B_C.g.cs");
        run.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Groups_WhoseNamespaceAndNameJoinTheSame_AreBothGenerated_AndCompile()
    {
        var source = $@"
using ZeroAlloc.StateMachine;
namespace Shared {{ {Enums} }}
namespace A_B {{ using Shared; {Group} public partial class C {{ }} }}
namespace A {{ using Shared; {Group} public partial class B_C {{ }} }}
";
        var run = Run(source);

        run.HintNames.Should().BeEquivalentTo("A_B.C.Group.g.cs", "A.B_C.Group.g.cs");
        run.Errors.Should().BeEmpty();
    }

    [Fact]
    public void NestedMachines_WithTheSameName_GetDistinctHintNames()
    {
        var source = $@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
public partial class Outer1 {{ {Machine} public partial class M {{ }} }}
public partial class Outer2 {{ {Machine} public partial class M {{ }} }}
";
        Run(source).HintNames.Should().BeEquivalentTo("N.Outer1+M.g.cs", "N.Outer2+M.g.cs");
    }

    [Fact]
    public void NestedGroups_WithTheSameName_GetDistinctHintNames()
    {
        var source = $@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
public partial class Outer1 {{ {Group} public partial class G {{ }} }}
public partial class Outer2 {{ {Group} public partial class G {{ }} }}
";
        Run(source).HintNames.Should().BeEquivalentTo("N.Outer1+G.Group.g.cs", "N.Outer2+G.Group.g.cs");
    }

    [Fact]
    public void GenericMachines_CarryTheirArity()
    {
        var source = $@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
{Machine} public partial class M {{ }}
{Machine} public partial class M<TX> {{ }}
public partial class Outer<TY> {{ {Machine} public partial class M {{ }} }}
";
        Run(source).HintNames.Should().BeEquivalentTo("N.M.g.cs", "N.M`1.g.cs", "N.Outer`1+M.g.cs");
    }

    [Fact]
    public void GenericGroups_CarryTheirArity()
    {
        var source = $@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
{Group} public partial class G {{ }}
{Group} public partial class G<TX> {{ }}
";
        Run(source).HintNames.Should().BeEquivalentTo("N.G.Group.g.cs", "N.G`1.Group.g.cs");
    }

    [Fact]
    public void HostsInTheGlobalNamespace_HaveNoNamespacePart()
    {
        var source = $@"
using ZeroAlloc.StateMachine;
{Enums}
{Machine} public partial class M {{ }}
{Group} public partial class G {{ }}
";
        var run = Run(source);

        run.HintNames.Should().BeEquivalentTo("M.g.cs", "G.Group.g.cs");
        run.Errors.Should().BeEmpty();
    }

    [Fact]
    public void VerbatimAndNonAsciiNames_AreWrittenWithoutEscapes()
    {
        var source = $@"
using ZeroAlloc.StateMachine;
namespace @event.Café;
{Enums}
{Machine} public partial class @Machine {{ }}
";
        var run = Run(source);

        run.HintNames.Should().BeEquivalentTo("event.Café.Machine.g.cs");
        run.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Foo", "Foo")]
    [InlineData("App.Outer`1+M", "App.Outer`1+M")]
    [InlineData("Café_1", "Café_1")]
    [InlineData("a<b>", "a-u003Cb-u003E")]
    [InlineData("a/b c", "a-u002Fb-u0020c")]
    [InlineData("\U0001D400x", "\U0001D400x")]
    public void Sanitize_KeepsIdentifierCharacters_AndEscapesTheRest(string input, string expected)
        => HintNames.Sanitize(input).Should().Be(expected);

    // Lone surrogates do not survive xunit's serialisation of [InlineData], so they get a [Fact].
    [Fact]
    public void Sanitize_EscapesLoneSurrogates()
    {
        HintNames.Sanitize(new string(new[] { '\uD835', 'x' })).Should().Be("-uD835x");
        HintNames.Sanitize(new string(new[] { 'x', '\uDC00' })).Should().Be("x-uDC00");
    }

    private static (string[] HintNames, Diagnostic[] Errors) Run(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .Append(MetadataReference.CreateFromFile(typeof(StateMachineAttribute).Assembly.Location))
            .ToList();
        var compilation = CSharpCompilation.Create(
            "Tests",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var run = CSharpGeneratorDriver.Create(new StateMachineGenerator())
            .WithUpdatedParseOptions(parseOptions)
            .RunGenerators(compilation)
            .GetRunResult();

        run.Results.Should().HaveCount(1);
        var result = run.Results[0];
        result.Exception.Should().BeNull();
        var errors = run.Diagnostics
            .Concat(compilation.AddSyntaxTrees(run.GeneratedTrees).GetDiagnostics())
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        return (result.GeneratedSources.Select(s => s.HintName).ToArray(), errors);
    }
}
