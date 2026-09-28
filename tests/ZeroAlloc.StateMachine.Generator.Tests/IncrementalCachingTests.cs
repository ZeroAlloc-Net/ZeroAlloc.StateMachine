using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace ZeroAlloc.StateMachine.Generator.Tests;

/// <summary>
/// The generator must serve cached results when nothing it reads has changed. Every value
/// between stages has to be value-equatable for that: one reference-compared collection or
/// <see cref="Diagnostic"/> in a model makes every run look new, and the generator then
/// re-emits every machine and re-reports every diagnostic on every keystroke in the IDE.
/// </summary>
public class IncrementalCachingTests
{
    // The generator's WithTrackingName values.
    private const string StateMachinesStep = "StateMachines";
    private const string StateMachineGroupsStep = "StateMachineGroups";

    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    /// <summary>
    /// A machine with a composite state and a diagram, so the sub-machine is resolved; a group;
    /// and diagnostics from both pipelines, ZSM0002 from the machine and ZSM0016 from the group.
    /// </summary>
    private const string MachinesSource = """
        using ZeroAlloc.StateMachine;
        namespace MyApp;

        public enum SubState { A, B }
        public enum State    { X, Y, Z }
        public enum Trigger  { Go, Stop }

        [StateMachine(InitialState = nameof(SubState.A))]
        [Transition<SubState, Trigger>(From = SubState.A, On = Trigger.Go, To = SubState.B)]
        [Terminal<SubState>(State = SubState.B)]
        public partial class SubFsm { }

        [StateMachine(InitialState = nameof(State.X), Diagram = true)]
        [Transition<State, Trigger>(From = State.X, On = Trigger.Go,   To = State.Y)]
        [Transition<State, Trigger>(From = State.Y, On = Trigger.Stop, To = State.Z)]
        [CompositeState<State>(State = State.Y, SubMachine = typeof(SubFsm))]
        [HistoryState<State>(State = State.Y)]
        public partial class Parent { }

        [StateMachineGroup(Diagram = true)]
        [StateMachinePart<State, Trigger>(Name = "P", InitialState = State.X)]
        [Transition<State, Trigger>(From = State.X, On = Trigger.Go, To = State.Y, Part = "P")]
        [Transition<State, Trigger>(From = State.Y, On = Trigger.Go, To = State.X, Part = "Q")]
        public partial class Group { }
        """;

    private const string UnrelatedSource = """
        namespace MyApp;

        public static class Unrelated
        {
            public static int Value => 1;
        }
        """;

    [Fact]
    public void EditedUnrelatedFile_ServesEveryStepAndOutputFromCache()
    {
        var compilation = CreateCompilation();
        var unrelated = Tree(compilation, "Unrelated.cs");
        var text = unrelated.GetText();
        var edited = unrelated.WithChangedText(text.Replace(
            text.ToString().IndexOf("1;", StringComparison.Ordinal), 1, "2"));

        var driver = RunTwice(compilation, compilation.ReplaceSyntaxTree(unrelated, edited));

        AssertAllCached(driver);
    }

    [Fact]
    public void EditedMachine_RegeneratesIt()
    {
        var compilation = CreateCompilation();
        var machines = Tree(compilation, "Machines.cs");
        var text = machines.GetText().ToString()
            .Replace("On = Trigger.Stop", "On = Trigger.Go", StringComparison.Ordinal);

        var driver = RunTwice(compilation, compilation.ReplaceSyntaxTree(machines, machines.WithChangedText(SourceText.From(text))));

        var reasons = driver.GetRunResult().Results[0].TrackedSteps[StateMachinesStep]
            .SelectMany(static s => s.Outputs)
            .Select(static o => o.Reason);
        Assert.Contains(IncrementalStepRunReason.Modified, reasons);
    }

    /// <summary>
    /// Diagnostics are cached as data and rebuilt when reported. They must come back as source
    /// locations in the compilation's own tree: an external-file location, as
    /// <c>Location.Create(filePath, span, lineSpan)</c> makes, is ignored by
    /// <c>#pragma warning disable</c>.
    /// </summary>
    [Fact]
    public void CachedDiagnostics_AreReportedAtSourceLocations()
    {
        var compilation = CreateCompilation();
        var driver = RunTwice(compilation, compilation.Clone());
        var diagnostics = driver.GetRunResult().Diagnostics;

        Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZSM0002", StringComparison.Ordinal));
        Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZSM0016", StringComparison.Ordinal));
        Assert.All(diagnostics, d =>
        {
            Assert.True(d.Location.IsInSource, d.ToString());
            Assert.Contains(d.Location.SourceTree, compilation.SyntaxTrees);
        });
    }

    /// <summary>Guards the test itself: with no step names the assertions would pass vacuously.</summary>
    [Fact]
    public void TrackedStepsAreNamed()
    {
        var result = CreateDriver().RunGenerators(CreateCompilation()).GetRunResult().Results[0];

        Assert.True(result.TrackedSteps.ContainsKey(StateMachinesStep));
        Assert.True(result.TrackedSteps.ContainsKey(StateMachineGroupsStep));
        Assert.NotEmpty(result.TrackedOutputSteps);
    }

    private static void AssertAllCached(GeneratorDriver driver)
    {
        var result = driver.GetRunResult().Results[0];
        var steps = new[] { StateMachinesStep, StateMachineGroupsStep }
            .SelectMany(name => result.TrackedSteps[name])
            .Concat(result.TrackedOutputSteps.SelectMany(static kv => kv.Value));

        foreach (var step in steps)
        {
            foreach (var (_, reason) in step.Outputs)
            {
                Assert.True(
                    reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                    $"Step '{step.Name}' was {reason}.");
            }
        }
    }

    private static GeneratorDriver CreateDriver() =>
        CSharpGeneratorDriver.Create(
            [new StateMachineGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

    private static GeneratorDriver RunTwice(Compilation first, Compilation second) =>
        CreateDriver().RunGenerators(first).RunGenerators(second);

    private static SyntaxTree Tree(Compilation compilation, string path) =>
        compilation.SyntaxTrees.First(t => string.Equals(t.FilePath, path, StringComparison.Ordinal));

    private static CSharpCompilation CreateCompilation()
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(static a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .Append(MetadataReference.CreateFromFile(typeof(StateMachineAttribute).Assembly.Location));

        return CSharpCompilation.Create(
            "Tests",
            [
                CSharpSyntaxTree.ParseText(MachinesSource, ParseOptions, path: "Machines.cs"),
                CSharpSyntaxTree.ParseText(UnrelatedSource, ParseOptions, path: "Unrelated.cs"),
            ],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
