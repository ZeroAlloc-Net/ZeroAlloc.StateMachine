namespace ZeroAlloc.StateMachine.Generator;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Threading;

[Generator]
public sealed class StateMachineGenerator : IIncrementalGenerator
{
    private const string StateMachineAttributeFqn        = "ZeroAlloc.StateMachine.StateMachineAttribute";

    // Constants used in Parse() — implemented in Task 5
#pragma warning disable IDE0051 // Remove unused private members
    private const string TransitionAttributeMetadataName = "TransitionAttribute`2";
    private const string TerminalAttributeMetadataName   = "TerminalAttribute`1";
#pragma warning restore IDE0051

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                StateMachineAttributeFqn,
                predicate: static (node, _) =>
                    node is ClassDeclarationSyntax or StructDeclarationSyntax,
                transform: static (ctx, ct) => Parse(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        context.RegisterSourceOutput(models, static (ctx, model) =>
        {
            foreach (var diag in model.Diagnostics)
                ctx.ReportDiagnostic(diag);

            // Do not emit source if any diagnostic is a hard error — the model is invalid
            if (model.Diagnostics.Any(static d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                return;

            var source = StateMachineWriter.Write(model);
            var hintName = model.Namespace is null
                ? $"{model.ClassName}.g.cs"
                : $"{model.Namespace}_{model.ClassName}.g.cs";
            ctx.AddSource(hintName, source);
        });
    }

    private static StateMachineModel? Parse(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        // Implemented in Task 5
        return null;
    }
}
