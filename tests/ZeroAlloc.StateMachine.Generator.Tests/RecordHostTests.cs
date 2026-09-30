namespace ZeroAlloc.StateMachine.Generator.Tests;

/// <summary>
/// A <c>record</c> or <c>record struct</c> can be a [StateMachine] host. The generated members
/// are written into a <c>partial record</c> or <c>partial record struct</c> declaration (#161).
/// </summary>
public class RecordHostTests
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

    private const string TimedMachine = @"
[StateMachine(InitialState = nameof(S.A), Concurrent = true)]
[Transition<S, T>(From = S.A, On = T.Go,   To = S.B, AfterMs = 1000)]
[Transition<S, T>(From = S.B, On = T.Back, To = S.A)]
";

    [Theory]
    [InlineData("public partial record M", "partial record M")]
    [InlineData("public sealed partial record class M", "partial record M")]
    [InlineData("public partial record struct M", "partial record struct M")]
    public void RecordMachine_IsGeneratedIntoTheRecord(string declaration, string header)
    {
        var run = GeneratorRunner.Run($@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
{Machine} {declaration} {{ }}
public static class Use
{{
    public static bool U() {{ var m = new M(); return m.TryFire(T.Go) && m.Current == S.B; }}
}}
");

        run.Errors.Should().BeEmpty();
        run.Source("N.M.g.cs").Should().MatchRegex(@"(?m)^" + header + @"\r?$");
    }

    [Fact]
    public void RecordStructMachine_GetsTheParameterlessConstructorThatSetsItsInitialState()
    {
        var run = GeneratorRunner.Run($@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
[StateMachine(InitialState = nameof(S.B))]
[Transition<S, T>(From = S.A, On = T.Go,   To = S.B)]
[Transition<S, T>(From = S.B, On = T.Back, To = S.A)]
public partial record struct M {{ }}
");

        run.Errors.Should().BeEmpty();
        run.Source("N.M.g.cs").Should().Contain("public M()");
    }

    // A primary constructor runs the field initializers itself, and a generated parameterless
    // constructor next to it would have to chain to it (CS8862), so none is generated.
    [Theory]
    [InlineData("public partial record M(int X)")]
    [InlineData("public partial record struct M(int X)")]
    [InlineData("public partial struct M(int X)")]
    public void MachineWithAPrimaryConstructor_Compiles(string declaration)
    {
        var run = GeneratorRunner.Run($@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
{Machine} {declaration} {{ }}
public static class Use
{{
    public static bool U() {{ var m = new M(1); return m.TryFire(T.Go) && m.Current == S.B; }}
}}
");

        run.Errors.Should().BeEmpty();
        run.Source("N.M.g.cs").Should().NotContain("public M()");
    }

    // A timed machine needs a constructor that calls HookConstructor (ZSM0021). With a primary
    // constructor, that is one that chains to it.
    [Fact]
    public void TimedPositionalRecord_WithAConstructorCallingHookConstructor_Compiles()
    {
        var run = GeneratorRunner.Run($@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
{TimedMachine}
public partial record M(int X)
{{
    public M() : this(0) {{ HookConstructor(); }}
}}
");

        run.Errors.Should().BeEmpty();
        run.GeneratorDiagnostics.Should().BeEmpty();
    }

    [Fact]
    public void TimedRecordMachine_AndGenericRecordMachine_Compile()
    {
        var run = GeneratorRunner.Run($@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
{TimedMachine} public partial record Timed {{ }}
{Machine} public partial record Generic<TX> {{ }}
{Machine} public partial record struct GenericStruct<TX> where TX : struct {{ }}
public static class Use
{{
    public static bool U()
    {{
        using var t = new Timed();
        var s = new GenericStruct<int>();
        return t.TryFire(T.Go) && new Generic<string>().TryFire(T.Go) && s.TryFire(T.Go);
    }}
}}
");

        run.Errors.Should().BeEmpty();
        run.Source("N.Timed.g.cs").Should().Contain("partial record Timed : System.IDisposable")
            .And.Contain("((Timed)s!)");
    }

    [Fact]
    public void RecordMachinesNestedInRecords_AreGeneratedIntoTheHost()
    {
        var run = GeneratorRunner.Run($@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
public partial record Orders(int Id)
{{
    {Machine} public partial record struct Inner {{ }}
}}
public partial record struct Batches<TKey>
{{
    {Machine} public partial record Inner {{ }}
}}
public static class Use
{{
    public static bool U()
    {{
        var a = new Orders.Inner();
        return a.TryFire(T.Go) && new Batches<int>.Inner().TryFire(T.Go);
    }}
}}
");

        run.Errors.Should().BeEmpty();
        run.Source("N.Orders+Inner.g.cs").Should().Contain("partial record Orders").And.Contain("partial record struct Inner");
        run.Source("N.Batches`1+Inner.g.cs").Should().Contain("partial record struct Batches<TKey>").And.Contain("partial record Inner");
    }

    [Fact]
    public void RecordSubMachine_OfACompositeState_IsGenerated()
    {
        var run = GeneratorRunner.Run(@"
using ZeroAlloc.StateMachine;
namespace N;
public enum Sub { X, Y }
public enum Top { Idle, Busy, Done }
public enum Trig { Go, Step, Stop }

[StateMachine(InitialState = nameof(Sub.X))]
[Transition<Sub, Trig>(From = Sub.X, On = Trig.Step, To = Sub.Y)]
[Terminal<Sub>(State = Sub.Y)]
public partial record struct Inner { }

[StateMachine(InitialState = nameof(Top.Idle), Diagram = true)]
[Transition<Top, Trig>(From = Top.Idle, On = Trig.Go,   To = Top.Busy)]
[Transition<Top, Trig>(From = Top.Busy, On = Trig.Stop, To = Top.Done)]
[CompositeState<Top>(State = Top.Busy, SubMachine = typeof(Inner))]
[Terminal<Top>(State = Top.Done)]
public partial record Parent { }

public static class Use { public static bool U() => new Parent().TryFire(Trig.Go); }
");

        run.Errors.Should().BeEmpty();
        run.HintNames.Should().BeEquivalentTo("N.Inner.g.cs", "N.Parent.g.cs");
    }

    // [StateMachineGroup] targets classes only, and a record is a class.
    [Fact]
    public void GroupOnARecord_IsGeneratedIntoTheRecord()
    {
        var run = GeneratorRunner.Run($@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
[StateMachineGroup]
[StateMachinePart<S, T>(Name = ""P"", InitialState = S.A)]
[Transition<S, T>(From = S.A, On = T.Go,   To = S.B, Part = ""P"", AfterMs = 1000)]
[Transition<S, T>(From = S.B, On = T.Back, To = S.A, Part = ""P"")]
public partial record G;
public static class Use {{ public static bool U() {{ using var g = new G(); return g.TryFireP(T.Go); }} }}
");

        run.Errors.Should().BeEmpty();
        run.Source("N.G.Group.g.cs").Should().Contain("partial record G : System.IDisposable");
    }

    [Fact]
    public void RecordMachineInANonPartialRecord_ReportsZsm0023()
    {
        var run = GeneratorRunner.Run($@"
using ZeroAlloc.StateMachine;
namespace N;
{Enums}
public record Outer {{ {Machine} public partial record M {{ }} }}
");

        run.GeneratorDiagnostics.Should().ContainSingle().Which.Id.Should().Be("ZSM0023");
        run.HintNames.Should().BeEmpty();
    }
}
