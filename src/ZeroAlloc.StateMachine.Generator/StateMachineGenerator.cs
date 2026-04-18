namespace ZeroAlloc.StateMachine.Generator;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

            // Do not emit source if any diagnostic is a hard error — the model is invalid
            if (model.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error))
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
        if (ctx.TargetSymbol is not INamedTypeSymbol type) return null;

        // [StateMachine] — the primary matched attribute
        var smAttr = ctx.Attributes[0];
        var initialState = smAttr.NamedArguments
            .FirstOrDefault(kv => string.Equals(kv.Key, "InitialState", StringComparison.Ordinal)).Value.Value as string ?? string.Empty;
        var concurrent = smAttr.NamedArguments
            .FirstOrDefault(kv => string.Equals(kv.Key, "Concurrent", StringComparison.Ordinal)).Value.Value is true;

        var (transitions, terminalStates, stateTypeFqn, stateTypeShort, triggerTypeFqn, triggerTypeShort)
            = CollectAttributes(type);

        if (stateTypeFqn is null || triggerTypeFqn is null) return null;
        if (string.IsNullOrEmpty(initialState)) return null;

        var ns       = type.ContainingNamespace.IsGlobalNamespace
                     ? null
                     : type.ContainingNamespace.ToDisplayString();
        var isStruct = type.TypeKind == TypeKind.Struct;
        var diagnostics = new List<Diagnostic>();

        AnalyzeDiagnostics(initialState, transitions, terminalStates, stateTypeShort!, type, diagnostics);

        return new StateMachineModel(
            ns, type.Name, isStruct,
            initialState, concurrent,
            stateTypeFqn, stateTypeShort!,
            triggerTypeFqn, triggerTypeShort!,
            transitions, terminalStates, diagnostics);
    }

    private static (
        List<TransitionModel> Transitions,
        List<string> TerminalStates,
        string? StateTypeFqn,
        string? StateTypeShort,
        string? TriggerTypeFqn,
        string? TriggerTypeShort)
        CollectAttributes(INamedTypeSymbol type)
    {
        var transitions    = new List<TransitionModel>();
        var terminalStates = new List<string>();
        string? stateTypeFqn    = null;
        string? stateTypeShort  = null;
        string? triggerTypeFqn  = null;
        string? triggerTypeShort = null;

        // Walk ALL attributes on the type to find [Transition<,>] and [Terminal<>]
        foreach (var attr in type.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null) continue;

            var metadataName = attrClass.MetadataName;

            if (string.Equals(metadataName, TransitionAttributeMetadataName, StringComparison.Ordinal) &&
                attrClass.TypeArguments.Length == 2)
            {
                var stateType   = attrClass.TypeArguments[0];
                var triggerType = attrClass.TypeArguments[1];

                stateTypeFqn    ??= stateType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                stateTypeShort  ??= stateType.Name;
                triggerTypeFqn  ??= triggerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                triggerTypeShort ??= triggerType.Name;

                var from     = GetEnumMemberName(attr, "From",  attrClass.TypeArguments[0]);
                var on       = GetEnumMemberName(attr, "On",    attrClass.TypeArguments[1]);
                var to       = GetEnumMemberName(attr, "To",    attrClass.TypeArguments[0]);
                var hasGuard = attr.NamedArguments
                    .FirstOrDefault(kv => string.Equals(kv.Key, "When", StringComparison.Ordinal)).Value.Value is true;

                if (from is not null && on is not null && to is not null)
                    transitions.Add(new TransitionModel(from, on, to, hasGuard));
            }
            else if (string.Equals(metadataName, TerminalAttributeMetadataName, StringComparison.Ordinal) &&
                     attrClass.TypeArguments.Length == 1)
            {
                var stateName = GetEnumMemberName(attr, "State", attrClass.TypeArguments[0]);
                if (stateName is not null) terminalStates.Add(stateName);
            }
        }

        return (transitions, terminalStates, stateTypeFqn, stateTypeShort, triggerTypeFqn, triggerTypeShort);
    }

    private static string? GetEnumMemberName(AttributeData attr, string namedArgKey, ITypeSymbol enumType)
    {
        var value = attr.NamedArguments.FirstOrDefault(kv => string.Equals(kv.Key, namedArgKey, StringComparison.Ordinal)).Value;
        if (value.IsNull || value.Value is null) return null;

        if (enumType is INamedTypeSymbol namedEnum)
        {
            var intVal = System.Convert.ToInt64(value.Value, CultureInfo.InvariantCulture);
            foreach (var member in namedEnum.GetMembers().OfType<IFieldSymbol>())
            {
                if (member.ConstantValue is not null &&
                    System.Convert.ToInt64(member.ConstantValue, CultureInfo.InvariantCulture) == intVal)
                    return member.Name;
            }
        }

        return value.Value?.ToString();
    }

    private static void AnalyzeDiagnostics(
        string initialState,
        IReadOnlyList<TransitionModel> transitions,
        IReadOnlyList<string> terminalStates,
        string stateTypeShort,
        INamedTypeSymbol type,
        List<Diagnostic> diagnostics)
    {
        // Implemented in Task 9
    }
}
