namespace ZeroAlloc.StateMachine.Generator;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
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
        ct.ThrowIfCancellationRequested();

        // [StateMachine] — the primary matched attribute
        var smAttr = ctx.Attributes[0];
        var initialState = smAttr.NamedArguments
            .FirstOrDefault(kv => string.Equals(kv.Key, "InitialState", StringComparison.Ordinal)).Value.Value as string ?? string.Empty;
        var concurrent = smAttr.NamedArguments
            .FirstOrDefault(kv => string.Equals(kv.Key, "Concurrent", StringComparison.Ordinal)).Value.Value is true;

        var (transitions, terminalStates, stateTypeFqn, stateTypeShort, triggerTypeFqn, triggerTypeShort)
            = CollectAttributes(type);

        if (transitions.IsEmpty) return null; // No transitions found — not a valid state machine
        if (stateTypeFqn is null || triggerTypeFqn is null) return null;
        if (string.IsNullOrEmpty(initialState)) return null;

        var ns       = type.ContainingNamespace.IsGlobalNamespace
                     ? null
                     : type.ContainingNamespace.ToDisplayString();
        var isStruct = type.TypeKind == TypeKind.Struct;
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        ct.ThrowIfCancellationRequested();
        AnalyzeDiagnostics(initialState, transitions, terminalStates, stateTypeShort!, type, isStruct, concurrent, diagnostics);

        return new StateMachineModel(
            ns, type.Name, isStruct,
            initialState, concurrent,
            stateTypeFqn, stateTypeShort!,
            triggerTypeFqn, triggerTypeShort!,
            transitions, terminalStates, diagnostics.ToImmutable());
    }

    private static (
        ImmutableArray<TransitionModel> Transitions,
        ImmutableArray<string> TerminalStates,
        string? StateTypeFqn,
        string? StateTypeShort,
        string? TriggerTypeFqn,
        string? TriggerTypeShort)
        CollectAttributes(INamedTypeSymbol type)
    {
        var transitions    = ImmutableArray.CreateBuilder<TransitionModel>();
        var terminalStates = ImmutableArray.CreateBuilder<string>();
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

        return (transitions.ToImmutable(), terminalStates.ToImmutable(), stateTypeFqn, stateTypeShort, triggerTypeFqn, triggerTypeShort);
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

        return null; // Unresolvable enum value — caller will skip this transition
    }

    private static void AnalyzeDiagnostics(
        string initialState,
        ImmutableArray<TransitionModel> transitions,
        ImmutableArray<string> terminalStates,
        string stateTypeShort,
        INamedTypeSymbol type,
        bool isStruct,
        bool concurrent,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var location = type.Locations.Length > 0 ? type.Locations[0] : Location.None;

        if (isStruct && concurrent)
        {
            diagnostics.Add(Diagnostic.Create(
                StateMachineDiagnostics.StructConcurrentNotSupported, location,
                type.Name));
            return;
        }

        var allFromStates = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        var allToStates   = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        var allTriggers   = new string[transitions.Length];

        for (var i = 0; i < transitions.Length; i++)
        {
            allFromStates.Add(transitions[i].From);
            allToStates.Add(transitions[i].To);
            allTriggers[i] = transitions[i].On;
        }

        AnalyzeReachability(initialState, terminalStates, stateTypeShort, type, location, allFromStates, allToStates, diagnostics);
        AnalyzeTriggerUsage(type, location, allTriggers, diagnostics);
    }

    private static void AnalyzeReachability(
        string initialState,
        ImmutableArray<string> terminalStates,
        string stateTypeShort,
        INamedTypeSymbol type,
        Location location,
        System.Collections.Generic.HashSet<string> allFromStates,
        System.Collections.Generic.HashSet<string> allToStates,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        // ZSM0001: states that appear in From but never in To and are not InitialState → unreachable
        foreach (var fromState in allFromStates)
        {
            if (!allToStates.Contains(fromState) &&
                !string.Equals(fromState, initialState, StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic.Create(
                    StateMachineDiagnostics.UnreachableState, location,
                    fromState, type.Name));
            }
        }

        // ZSM0002: states that appear in To but never in From and are not declared [Terminal]
        var terminalSet = new System.Collections.Generic.HashSet<string>(terminalStates, StringComparer.Ordinal);
        foreach (var toState in allToStates)
        {
            if (!allFromStates.Contains(toState) && !terminalSet.Contains(toState))
            {
                diagnostics.Add(Diagnostic.Create(
                    StateMachineDiagnostics.SinkState, location,
                    toState, type.Name, stateTypeShort));
            }
        }
    }

    private static void AnalyzeTriggerUsage(
        INamedTypeSymbol type,
        Location location,
        string[] allTriggers,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        // ZSM0003: triggers used exactly once (only meaningful if more than one trigger is used total)
        var triggerCounts = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var trigger in allTriggers)
        {
            if (triggerCounts.TryGetValue(trigger, out var count))
                triggerCounts[trigger] = count + 1;
            else
                triggerCounts[trigger] = 1;
        }

        // Only flag single-use triggers when at least one other trigger appears more than once
        // (a typo tends to appear once while the "real" trigger appears multiple times)
        var anyMultiUse = false;
        foreach (var kv in triggerCounts)
        {
            if (kv.Value > 1)
            {
                anyMultiUse = true;
                break;
            }
        }

        if (anyMultiUse)
        {
            foreach (var kv in triggerCounts)
            {
                if (kv.Value == 1)
                {
                    diagnostics.Add(Diagnostic.Create(
                        StateMachineDiagnostics.SingleUseTrigger, location,
                        kv.Key, type.Name));
                }
            }
        }
    }
}
