using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ZeroAlloc.StateMachine.Generator.Tests;

internal static class TestHelper
{
    public static Task Verify<TGenerator>(string source)
        where TGenerator : IIncrementalGenerator, new()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .Append(MetadataReference.CreateFromFile(
                typeof(StateMachineAttribute).Assembly.Location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            assemblyName: "Tests",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new TGenerator();
        var driver = CSharpGeneratorDriver
            .Create(generator)
            .RunGenerators(compilation);

        return VerifyXunit.Verifier.Verify(driver).UseDirectory("Snapshots");
    }

    public static Task<IReadOnlyList<Diagnostic>> GetDiagnostics<TGenerator>(string source)
        where TGenerator : IIncrementalGenerator, new()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .Append(MetadataReference.CreateFromFile(
                typeof(StateMachineAttribute).Assembly.Location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            assemblyName: "Tests",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new TGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        var result = driver.GetRunResult();

        var diags = result.Diagnostics
            .Concat(result.GeneratedTrees.SelectMany(t => t.GetDiagnostics()))
            .ToList();

        return Task.FromResult<IReadOnlyList<Diagnostic>>(diags);
    }
}
