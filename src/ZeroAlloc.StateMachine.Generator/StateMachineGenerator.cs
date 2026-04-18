namespace ZeroAlloc.StateMachine.Generator;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Threading;

[Generator]
public sealed class StateMachineGenerator : IIncrementalGenerator
{
    private const string StateMachineAttributeFqn        = "ZeroAlloc.StateMachine.StateMachineAttribute";
    private const string TransitionAttributeMetadataName = "TransitionAttribute`2";
    private const string TerminalAttributeMetadataName   = "TerminalAttribute`1";

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
