using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.StateMachine.Generator.Tests;

/// <summary>
/// Runs the generator over one or more files and compiles its output with them.
/// </summary>
internal static class GeneratorRunner
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    public sealed record Result(
        IReadOnlyList<GeneratedSourceResult> Sources,
        IReadOnlyList<Diagnostic> GeneratorDiagnostics,
        IReadOnlyList<Diagnostic> CompilationErrors)
    {
        public string[] HintNames => Sources.Select(static s => s.HintName).ToArray();

        public string Source(string hintName) =>
            Sources.Single(s => string.Equals(s.HintName, hintName, StringComparison.Ordinal)).SourceText.ToString();

        /// <summary>Generator diagnostics and compilation errors, generator ones first.</summary>
        public IEnumerable<Diagnostic> Errors =>
            GeneratorDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Concat(CompilationErrors);
    }

    public static Result Run(string source) => Run(("Source.cs", source));

    /// <summary>Each file is parsed with its path, in the order given.</summary>
    public static Result Run(params (string Path, string Text)[] files)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(static a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .Append(MetadataReference.CreateFromFile(typeof(StateMachineAttribute).Assembly.Location))
            .ToList();
        var compilation = CSharpCompilation.Create(
            "Tests",
            files.Select(static f => CSharpSyntaxTree.ParseText(f.Text, ParseOptions, path: f.Path)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var run = CSharpGeneratorDriver.Create(new StateMachineGenerator())
            .WithUpdatedParseOptions(ParseOptions)
            .RunGenerators(compilation)
            .GetRunResult();

        run.Results.Should().HaveCount(1);
        var result = run.Results[0];
        result.Exception.Should().BeNull();

        var errors = compilation.AddSyntaxTrees(run.GeneratedTrees).GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        return new Result(result.GeneratedSources, run.Diagnostics, errors);
    }
}
