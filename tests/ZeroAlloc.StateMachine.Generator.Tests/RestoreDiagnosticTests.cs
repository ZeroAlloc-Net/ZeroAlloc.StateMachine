using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.StateMachine.Generator.Tests;

/// <summary>
/// ZSM0027 and ZSM0028: the generated static Restore is left out, with an informational
/// diagnostic, where it would not compile. The machine itself is still generated.
/// </summary>
public class RestoreDiagnosticTests
{
    private const string Header = @"
using ZeroAlloc.StateMachine;
namespace N;
public enum S { A, B }
public enum T { Go }
";

    private const string Attrs = @"
[StateMachine(InitialState = nameof(S.A))]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B)]
[Terminal<S>(State = S.B)]
";

    [Fact]
    public void PlainMachine_EmitsRestore_AndNoDiagnostic()
    {
        var run = GeneratorRunner.Run(Header + Attrs + "public partial class M { }");

        run.GeneratorDiagnostics.Should().BeEmpty();
        run.CompilationErrors.Should().BeEmpty();
        run.Source("N.M.g.cs").Should().Contain("public static M Restore(global::N.S state)");
    }

    [Fact]
    public void ClassWithOnlyParameterizedConstructor_ReportsZsm0027_AndEmitsNoRestore()
    {
        var run = GeneratorRunner.Run(Header + Attrs + "public partial class M { public M(int x) { } }");

        var d = run.GeneratorDiagnostics.Should().ContainSingle().Subject;
        d.Id.Should().Be("ZSM0027");
        d.Severity.Should().Be(DiagnosticSeverity.Info);
        d.GetMessage(CultureInfo.InvariantCulture).Should().Be(
            "'M' gets no generated Restore method because it declares constructors but no parameterless one");
        run.CompilationErrors.Should().BeEmpty();
        run.Source("N.M.g.cs").Should().NotContain(" Restore(");
    }

    [Fact]
    public void AbstractClass_ReportsZsm0027()
    {
        var run = GeneratorRunner.Run(Header + Attrs + "public abstract partial class M { }");

        run.GeneratorDiagnostics.Should().ContainSingle().Which.Id.Should().Be("ZSM0027");
        run.CompilationErrors.Should().BeEmpty();
    }

    [Fact]
    public void StructWithPrimaryConstructor_ReportsZsm0027()
    {
        var run = GeneratorRunner.Run(Header + Attrs + "public partial struct M(int x) { }");

        run.GeneratorDiagnostics.Should().ContainSingle().Which.Id.Should().Be("ZSM0027");
        run.Source("N.M.g.cs").Should().NotContain(" Restore(");
    }

    [Fact]
    public void ClassWithParameterlessAndOtherConstructors_EmitsRestore()
    {
        var run = GeneratorRunner.Run(Header + Attrs + "public partial class M { public M() { } public M(int x) { } }");

        run.GeneratorDiagnostics.Should().BeEmpty();
        run.CompilationErrors.Should().BeEmpty();
        run.Source("N.M.g.cs").Should().Contain(" Restore(");
    }

    [Fact]
    public void UserDeclaredRestoreOfTheStateType_ReportsZsm0028_AtTheMember_AndEmitsNoRestore()
    {
        var source = Header + Attrs + "public partial class M { public static M Restore(S state) => new M(); }";
        var run = GeneratorRunner.Run(source);

        var d = run.GeneratorDiagnostics.Should().ContainSingle().Subject;
        d.Id.Should().Be("ZSM0028");
        d.Severity.Should().Be(DiagnosticSeverity.Info);
        source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length).Should().Be("Restore");
        run.CompilationErrors.Should().BeEmpty();
        run.Source("N.M.g.cs").Should().NotContain("public static M Restore(");
    }

    [Fact]
    public void UserDeclaredRestoreProperty_ReportsZsm0028()
    {
        var run = GeneratorRunner.Run(Header + Attrs + "public partial class M { public int Restore { get; } }");

        run.GeneratorDiagnostics.Should().ContainSingle().Which.Id.Should().Be("ZSM0028");
        run.CompilationErrors.Should().BeEmpty();
    }

    [Fact]
    public void UserDeclaredRestoreWithAnotherSignature_IsNotACollision()
    {
        var run = GeneratorRunner.Run(Header + Attrs + "public partial class M { public static M Restore(string json) => new M(); }");

        run.GeneratorDiagnostics.Should().BeEmpty();
        run.CompilationErrors.Should().BeEmpty();
        run.Source("N.M.g.cs").Should().Contain("public static M Restore(global::N.S state)");
    }

    [Fact]
    public void RestorableStates_ExcludeAStateNothingLeadsTo()
    {
        var run = GeneratorRunner.Run(@"
using ZeroAlloc.StateMachine;
namespace N;
public enum S { A, B, Orphan }
public enum T { Go }
[StateMachine(InitialState = nameof(S.A))]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B)]
[Terminal<S>(State = S.B)]
public partial class M { }");

        var source = run.Source("N.M.g.cs");
        source.Should().Contain("case global::N.S.A:").And.Contain("case global::N.S.B:");
        source.Should().NotContain("case global::N.S.Orphan:");
    }

    private const string TimedAttrs = @"
[StateMachine(InitialState = nameof(S.A), Concurrent = true)]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, AfterMs = 100)]
[Terminal<S>(State = S.B)]
";

    [Fact]
    public void TimedMachine_RestoresThroughAPrivateConstructorThatArmsNoTimer()
    {
        var run = GeneratorRunner.Run(Header + TimedAttrs + "public partial class M { }");

        run.GeneratorDiagnostics.Should().BeEmpty();
        run.CompilationErrors.Should().BeEmpty();
        var source = run.Source("N.M.g.cs");
        source.Should().Contain("private M(__RestoreMarker _)");
        source.Should().Contain("new M(default(__RestoreMarker))");
    }

    [Fact]
    public void TimedMachineWithAPrimaryConstructor_ReportsZsm0027()
    {
        var run = GeneratorRunner.Run(Header + TimedAttrs + "public partial class M(int x) { public M() : this(0) { HookConstructor(); } }");

        run.GeneratorDiagnostics.Should().ContainSingle().Which.Id.Should().Be("ZSM0027");
        run.CompilationErrors.Should().BeEmpty();
        run.Source("N.M.g.cs").Should().NotContain(" Restore(");
    }

    [Fact]
    public void TimedMachineWithABaseClassNeedingArguments_ReportsZsm0027()
    {
        var run = GeneratorRunner.Run(Header +
            "public class B { public B(int x) { } }" + TimedAttrs + "public partial class M : B { public M() : base(0) { HookConstructor(); } }");

        run.GeneratorDiagnostics.Should().ContainSingle().Which.Id.Should().Be("ZSM0027");
        run.CompilationErrors.Should().BeEmpty();
    }

    [Fact]
    public void NonTimedMachine_UsesNoMarkerConstructor()
    {
        var run = GeneratorRunner.Run(Header + Attrs + "public partial class M { }");

        run.Source("N.M.g.cs").Should().NotContain("__RestoreMarker");
    }

    private const string GroupSource = @"
using ZeroAlloc.StateMachine;
namespace N;
public enum S { A, B }
public enum T { Go }
public enum U { X, Y, Orphan }
[StateMachineGroup]
[StateMachinePart<S, T>(Name = ""P"", InitialState = S.A)]
[StateMachinePart<U, T>(Name = ""Q"", InitialState = U.X)]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, Part = ""P"")]
[Transition<U, T>(From = U.X, On = T.Go, To = U.Y, Part = ""Q"")]
";

    [Fact]
    public void Group_EmitsGroupStateCurrentStateAndRestore_ExcludingUnreachablePartStates()
    {
        var run = GeneratorRunner.Run(GroupSource + "public partial class G { }");

        run.GeneratorDiagnostics.Should().BeEmpty();
        run.CompilationErrors.Should().BeEmpty();
        var source = run.Source("N.G.Group.g.cs");
        source.Should().Contain("public readonly record struct GroupState(global::N.S P, global::N.U Q);");
        source.Should().Contain("public GroupState CurrentState");
        source.Should().Contain("public static G Restore(GroupState state)");
        source.Should().NotContain("case global::N.U.Orphan:");
        source.Should().NotContain("__RestoreMarker");
    }

    [Fact]
    public void TimedGroup_RestoresThroughAMarkerConstructor()
    {
        var run = GeneratorRunner.Run(GroupSource.Replace("To = S.B, Part", "To = S.B, AfterMs = 50, Part") + "public partial class G { }");

        run.GeneratorDiagnostics.Should().BeEmpty();
        run.CompilationErrors.Should().BeEmpty();
        run.Source("N.G.Group.g.cs").Should().Contain("new G(default(__RestoreMarker))");
    }

    [Fact]
    public void Group_WithOnlyAParameterizedConstructor_ReportsZsm0027()
    {
        var run = GeneratorRunner.Run(GroupSource + "public partial class G { public G(int x) { } }");

        run.GeneratorDiagnostics.Should().ContainSingle().Which.Id.Should().Be("ZSM0027");
        run.CompilationErrors.Should().BeEmpty();
        run.Source("N.G.Group.g.cs").Should().NotContain(" Restore(");
    }

    [Theory]
    [InlineData("public int CurrentState { get; }")]
    [InlineData("public class GroupState { }")]
    [InlineData("public static G Restore(int x) => new G();")]
    public void Group_WithAConflictingMember_ReportsZsm0028(string member)
    {
        var run = GeneratorRunner.Run(GroupSource + "public partial class G { " + member + " }");

        run.GeneratorDiagnostics.Should().ContainSingle().Which.Id.Should().Be("ZSM0028");
        run.CompilationErrors.Should().BeEmpty();
        run.Source("N.G.Group.g.cs").Should().NotContain("record struct GroupState");
    }

    [Fact]
    public void Group_Snapshot()
    {
        TestHelper.Verify<StateMachineGenerator>(GroupSource.Replace("To = U.Y, Part", "To = U.Y, AfterMs = 50, Part") + "public partial class G { }");
    }
}
