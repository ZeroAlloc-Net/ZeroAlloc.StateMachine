using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.StateMachine.Generator.Tests;

/// <summary>
/// A machine or group is generated into the host itself: every containing type is reopened as
/// <c>partial</c>, then the host with its type parameters and its name escaped where it is a
/// keyword. Each case compiles code that uses the generated members on the real host.
/// </summary>
public class NestedHostTests
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

    // Timed edges make the generated code refer to the host by name, in the timer callbacks,
    // and declare its constructor.
    private const string TimedMachine = @"
[StateMachine(InitialState = nameof(S.A), Concurrent = true)]
[Transition<S, T>(From = S.A, On = T.Go,   To = S.B, AfterMs = 1000)]
[Transition<S, T>(From = S.B, On = T.Back, To = S.A)]
";

    private const string Group = @"
[StateMachineGroup]
[StateMachinePart<S, T>(Name = ""P"", InitialState = S.A)]
[Transition<S, T>(From = S.A, On = T.Go,   To = S.B, Part = ""P"", AfterMs = 1000)]
[Transition<S, T>(From = S.B, On = T.Back, To = S.A, Part = ""P"")]
";

    [Fact]
    public void SameNamedNestedMachines_AreGeneratedIntoEachHost()
    {
        var run = GeneratorRunner.Run($@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
public partial class Outer1 {{ {Machine} public partial class M {{ }} }}
public partial class Outer2 {{ {Machine} public partial class M {{ }} }}
public static class Use
{{
    public static bool U(Outer1.M a, Outer2.M b) => a.TryFire(T.Go) && b.TryFire(T.Go) && a.Current == S.B;
}}
");

        run.Errors.Should().BeEmpty();
        run.Source("N.Outer1+M.g.cs").Should().Contain("partial class Outer1").And.Contain("partial class M");
    }

    [Fact]
    public void SameNamedNestedGroups_AreGeneratedIntoEachHost()
    {
        var run = GeneratorRunner.Run($@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
public partial class Outer1 {{ {Group} public partial class G {{ }} }}
public partial class Outer2 {{ {Group} public partial class G {{ }} }}
public static class Use
{{
    public static bool U() {{ using var a = new Outer1.G(); using var b = new Outer2.G(); return a.TryFireP(T.Go) && b.PCurrent == S.A; }}
}}
");

        run.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("class", "M", "M<int>")]
    [InlineData("class", "M<TX>", "M<int>")]
    [InlineData("class", "M<TX, TY> where TX : class where TY : struct", "M<string, int>")]
    [InlineData("struct", "M<TX> where TX : notnull", "M<int>")]
    public void GenericMachines_AreGeneratedIntoTheGenericHost(string kind, string declaration, string use)
    {
        // The non-generic "M" case pairs with a generic M<TX>, so the two must not merge.
        var extra = string.Equals(declaration, "M", StringComparison.Ordinal) ? $"{Machine} public partial class M<TX> {{ }}" : string.Empty;
        var run = GeneratorRunner.Run($@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
{Machine} public partial {kind} {declaration} {{ }}
{extra}
public static class Use
{{
    public static bool U() {{ var m = new {use}(); return m.TryFire(T.Go) && m.Current == S.B; }}
}}
");

        run.Errors.Should().BeEmpty();
    }

    [Fact]
    public void GenericTimedMachineAndGroup_ReferToThemselvesWithTheirTypeParameters()
    {
        var run = GeneratorRunner.Run($@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
public partial class Outer<TO>
{{
    {TimedMachine} public partial class M<TX> {{ }}
    {Group} public partial class G<TX, TY> {{ }}
}}
public static class Use
{{
    public static bool U()
    {{
        using var m = new Outer<int>.M<string>();
        using var g = new Outer<int>.G<string, long>();
        return m.TryFire(T.Go) && g.TryFireP(T.Go);
    }}
}}
");

        run.Errors.Should().BeEmpty();
        var source = run.Source("N.Outer`1+M`1.g.cs");
        source.Should().Contain("partial class Outer<TO>");
        source.Should().Contain("partial class M<TX> : System.IDisposable");
        source.Should().Contain("((M<TX>)s!)");
        source.Should().Contain("public M()");
    }

    [Theory]
    [InlineData("public partial struct Outer<TC> where TC : struct", "Outer<int>")]
    [InlineData("public partial interface Outer", "Outer")]
    [InlineData("public partial record struct Outer(int X)", "Outer")]
    [InlineData("public partial record Outer(int X)", "Outer")]
    [InlineData("public readonly ref partial struct Outer", "Outer")]
    [InlineData("public static partial class Outer", "Outer")]
    public void MachinesInContainersOfEveryKind_AreGeneratedIntoTheHost(string container, string use)
    {
        var run = GeneratorRunner.Run($@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
{container}
{{
    {Machine} public partial class M {{ }}
    {Group} public partial class G {{ }}
}}
public static class Use
{{
    public static bool U() {{ using var g = new {use}.G(); return new {use}.M().TryFire(T.Go) && g.TryFireP(T.Go); }}
}}
");

        run.Errors.Should().BeEmpty();
        run.HintNames.Should().HaveCount(2);
    }

    [Fact]
    public void KeywordNames_AreWrittenAsVerbatimIdentifiers()
    {
        var run = GeneratorRunner.Run($@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
{TimedMachine} public partial class @class {{ }}
public partial class @event<@int>
{{
    {Machine} public partial struct @static<@void> {{ }}
    {Group} public partial class @object {{ }}
}}
public static class Use
{{
    public static bool U()
    {{
        using var a = new @class();
        var b = new @event<int>.@static<long>();
        using var c = new @event<int>.@object();
        return a.TryFire(T.Go) && b.TryFire(T.Go) && c.TryFireP(T.Go);
    }}
}}
");

        run.Errors.Should().BeEmpty();
        run.Source("N.class.g.cs").Should().Contain("partial class @class : System.IDisposable")
            .And.Contain("((@class)s!)").And.Contain("public @class()");
        run.Source("N.event`1+static`1.g.cs").Should().Contain("partial class @event<@int>")
            .And.Contain("partial struct @static<@void>").And.Contain("public @static()");
    }

    [Fact]
    public void NestedHosts_WithTheirOwnAccessibility_AreGeneratedIntoTheHost()
    {
        var run = GeneratorRunner.Run($@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
public partial class Outer
{{
    {Machine} private partial class Private {{ }}
    {Machine} protected partial class Protected {{ }}
    {Machine} protected internal partial class ProtectedInternal {{ }}
    {Machine} private protected partial class PrivateProtected {{ }}
    {Group} internal partial class Internal {{ }}
    public static bool U() => new Private().TryFire(T.Go);
}}
");

        run.Errors.Should().BeEmpty();
        run.HintNames.Should().HaveCount(5);
    }

    [Fact]
    public void ContainingTypeDeclaredInSeveralParts_IsReopenedOnce()
    {
        var run = GeneratorRunner.Run($@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
public partial class Outer {{ }}
public partial class Outer {{ {Machine} public partial class M {{ }} }}
public class Use {{ public static bool U() => new Outer.M().TryFire(T.Go); }}
");

        run.Errors.Should().BeEmpty();
    }

    [Fact]
    public void NestedSubMachine_OfACompositeState_IsGeneratedIntoItsHost()
    {
        var run = GeneratorRunner.Run(@"
using ZeroAlloc.StateMachine;
namespace N;
public enum Sub { X, Y }
public enum Top { Idle, Busy, Done }
public enum Trig { Go, Step, Stop }
public partial class Machines
{
    [StateMachine(InitialState = nameof(Sub.X))]
    [Transition<Sub, Trig>(From = Sub.X, On = Trig.Step, To = Sub.Y)]
    [Terminal<Sub>(State = Sub.Y)]
    public partial class Inner { }

    [StateMachine(InitialState = nameof(Top.Idle), Diagram = true)]
    [Transition<Top, Trig>(From = Top.Idle, On = Trig.Go,   To = Top.Busy)]
    [Transition<Top, Trig>(From = Top.Busy, On = Trig.Stop, To = Top.Done)]
    [CompositeState<Top>(State = Top.Busy, SubMachine = typeof(Inner))]
    [Terminal<Top>(State = Top.Done)]
    public partial class Parent { }
}
public static class Use { public static bool U() => new Machines.Parent().TryFire(Trig.Go); }
");

        run.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TopLevelHostsInTheGlobalNamespace_AreStillGenerated()
    {
        var run = GeneratorRunner.Run($@"
using ZeroAlloc.StateMachine;
{Enums}
public partial class Outer {{ {Machine} public partial class M {{ }} }}
public static class Use {{ public static bool U() => new Outer.M().TryFire(T.Go); }}
");

        run.Errors.Should().BeEmpty();
        run.Source("Outer+M.g.cs").Should().NotContain("namespace");
    }

    [Fact]
    public void RefStructMachine_KeepsItsPlainPartialStructHeader()
    {
        var run = GeneratorRunner.Run($@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
{Machine} public ref partial struct M {{ }}
public partial class Outer {{ {Machine} public ref partial struct R {{ }} }}
public ref partial struct Container {{ {Machine} public partial class C {{ }} }}
");

        run.Errors.Should().BeEmpty();
        run.Source("N.M.g.cs").Should().MatchRegex(@"(?m)^partial struct M\r?$");
        run.Source("N.Outer+R.g.cs").Should().Contain("partial struct R");
        run.Source("N.Container+C.g.cs").Should().Contain("ref partial struct Container");
    }

    // ── ZSM0023: a containing type is not partial ─────────────────────────────

    [Fact]
    public void MachineInANonPartialContainingType_ReportsZsm0023_AndGeneratesNothing()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
namespace N;
public enum S { A, B }
public enum T { Go, Back }
public class Outer
{
    [StateMachine(InitialState = nameof(S.A))]
    [Transition<S, T>(From = S.A, On = T.Go,   To = S.B)]
    [Transition<S, T>(From = S.B, On = T.Back, To = S.A)]
    public partial class M { }
}
";
        var run = GeneratorRunner.Run(source);

        var diagnostic = run.GeneratorDiagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("ZSM0023");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning);
        diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be("'N.Outer.M' is not generated because its containing type 'N.Outer' is not partial");
        LocatedText(diagnostic, source).Should().Be("M");
        diagnostic.Location.GetLineSpan().StartLinePosition.Line.Should().Be(10);
        run.HintNames.Should().BeEmpty();
        run.CompilationErrors.Should().BeEmpty();
    }

    [Fact]
    public void GroupUnderANonPartialOuterType_ReportsZsm0023_NamingTheOutermost()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
namespace N;
public enum S { A, B }
public enum T { Go, Back }
public class Outer
{
    public partial class Middle
    {
        [StateMachineGroup]
        [StateMachinePart<S, T>(Name = ""P"", InitialState = S.A)]
        [Transition<S, T>(From = S.A, On = T.Go,   To = S.B, Part = ""P"")]
        [Transition<S, T>(From = S.B, On = T.Back, To = S.A, Part = ""P"")]
        public partial class G { }
    }
}
public partial class Fine
{
    [StateMachineGroup]
    [StateMachinePart<S, T>(Name = ""P"", InitialState = S.A)]
    [Transition<S, T>(From = S.A, On = T.Go,   To = S.B, Part = ""P"")]
    [Transition<S, T>(From = S.B, On = T.Back, To = S.A, Part = ""P"")]
    public partial class G { }
}
";
        var run = GeneratorRunner.Run(source);

        var diagnostic = run.GeneratorDiagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("ZSM0023");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning);
        diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be("'N.Outer.Middle.G' is not generated because its containing type 'N.Outer' is not partial");
        LocatedText(diagnostic, source).Should().Be("G");
        diagnostic.Location.GetLineSpan().StartLinePosition.Line.Should().Be(13);
        run.HintNames.Should().BeEquivalentTo("N.Fine+G.Group.g.cs");
        run.CompilationErrors.Should().BeEmpty();
    }

    // ── ZSM0024: a file-local host ───────────────────────────────────────────

    [Fact]
    public void FileLocalMachine_ReportsZsm0024_AndGeneratesNothing()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
namespace N;
public enum S { A, B }
public enum T { Go, Back }
[StateMachine(InitialState = nameof(S.A))]
[Transition<S, T>(From = S.A, On = T.Go,   To = S.B)]
[Transition<S, T>(From = S.B, On = T.Back, To = S.A)]
file partial class M { }
";
        var run = GeneratorRunner.Run(source);

        var diagnostic = run.GeneratorDiagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("ZSM0024");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be("'N.M' is not generated because it is file-local, and a generated file cannot extend a file-local type");
        LocatedText(diagnostic, source).Should().Be("M");
        run.HintNames.Should().BeEmpty();
        run.CompilationErrors.Should().BeEmpty();
    }

    [Fact]
    public void GroupNestedInAFileLocalType_ReportsZsm0024_AndGeneratesNothing()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
namespace N;
public enum S { A, B }
public enum T { Go, Back }
file partial class Outer
{
    [StateMachineGroup]
    [StateMachinePart<S, T>(Name = ""P"", InitialState = S.A)]
    [Transition<S, T>(From = S.A, On = T.Go,   To = S.B, Part = ""P"")]
    [Transition<S, T>(From = S.B, On = T.Back, To = S.A, Part = ""P"")]
    public partial class G { }
}
";
        var run = GeneratorRunner.Run(source);

        var diagnostic = run.GeneratorDiagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("ZSM0024");
        diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be("'N.Outer.G' is not generated because its containing type 'N.Outer' is file-local, and a generated file cannot extend a file-local type");
        LocatedText(diagnostic, source).Should().Be("G");
        run.HintNames.Should().BeEmpty();
        run.CompilationErrors.Should().BeEmpty();
    }

    private static string LocatedText(Diagnostic diagnostic, string source) =>
        source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);
}
