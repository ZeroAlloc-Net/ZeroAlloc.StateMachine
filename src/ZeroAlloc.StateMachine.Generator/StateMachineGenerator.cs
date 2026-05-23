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
    private const string StateMachineGroupAttributeFqn   = "ZeroAlloc.StateMachine.StateMachineGroupAttribute";
    private const string TransitionAttributeMetadataName = "TransitionAttribute`2";
    private const string TerminalAttributeMetadataName   = "TerminalAttribute`1";
    private const string CompositeStateAttributeMetadataName = "CompositeStateAttribute`1";
    private const string HistoryStateAttributeMetadataName   = "HistoryStateAttribute`1";
    private const string StateMachineAttributeMetadataName   = "StateMachineAttribute";
    private const string StateMachineGroupAttributeMetadataName = "StateMachineGroupAttribute";
    private const string StateMachinePartAttributeMetadataName  = "StateMachinePartAttribute`2";

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

        context.RegisterSourceOutput(
            models.Combine(context.CompilationProvider),
            static (ctx, tuple) => EmitStateMachine(ctx, tuple.Left, tuple.Right));

        var groupModels = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                StateMachineGroupAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => ParseGroup(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        context.RegisterSourceOutput(groupModels, static (ctx, model) =>
        {
            foreach (var diag in model.Diagnostics)
                ctx.ReportDiagnostic(diag);

            if (model.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error))
                return;

            var source = StateMachineGroupWriter.Write(model);
            var hintName = model.Namespace is null
                ? $"{model.ClassName}.Group.g.cs"
                : $"{model.Namespace}_{model.ClassName}.Group.g.cs";
            ctx.AddSource(hintName, source);
        });
    }

    private static void EmitStateMachine(SourceProductionContext ctx, StateMachineModel model, Compilation compilation)
    {
        foreach (var diag in model.Diagnostics)
            ctx.ReportDiagnostic(diag);

        // Do not emit source if any diagnostic is a hard error — the model is invalid
        if (model.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error))
            return;

        // Skip emit when there are no transitions — the model was only built so that
        // AnalyzeDiagnostics could fire ZSM0020 (Diagram = true on an empty machine).
        if (model.Transitions.IsEmpty)
            return;

        System.Func<string, StateMachineModel?> resolver = fqn =>
        {
            var clean = fqn.StartsWith("global::", StringComparison.Ordinal) ? fqn.Substring(8) : fqn;
            var sym = compilation.GetTypeByMetadataName(clean);
            return sym is null ? null : BuildModelFromSymbol(sym);
        };

        var source = StateMachineWriter.Write(model, resolver);
        var hintName = model.Namespace is null
            ? $"{model.ClassName}.g.cs"
            : $"{model.Namespace}_{model.ClassName}.g.cs";
        ctx.AddSource(hintName, source);
    }

    private static StateMachineGroupModel? ParseGroup(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol type) return null;
        ct.ThrowIfCancellationRequested();

        var ns = type.ContainingNamespace.IsGlobalNamespace
                 ? null
                 : type.ContainingNamespace.ToDisplayString();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        var groupAttr = ctx.Attributes[0];
        var diagram = groupAttr.NamedArguments
            .FirstOrDefault(kv => string.Equals(kv.Key, "Diagram", StringComparison.Ordinal)).Value.Value is true;

        var parts = CollectGroupParts(type);

        ct.ThrowIfCancellationRequested();
        AnalyzeGroupDiagnostics(type, parts, diagram, diagnostics);

        return new StateMachineGroupModel(
            ns, type.Name, parts,
            Diagram: diagram,
            diagnostics.ToImmutable());
    }

    private readonly record struct PartDeclaration(
        string Name, string InitialState,
        string StateFqn, string StateShort,
        string TriggerFqn, string TriggerShort);

    private static ImmutableArray<StateMachinePartModel> CollectGroupParts(INamedTypeSymbol type)
    {
        var partDeclarations = CollectPartDeclarations(type);
        var transitionsByPart = BucketTransitionsByPart(type, partDeclarations);

        var result = ImmutableArray.CreateBuilder<StateMachinePartModel>(partDeclarations.Length);
        foreach (var pb in partDeclarations)
        {
            var transitions = transitionsByPart[pb.Name].ToImmutable();
            result.Add(new StateMachinePartModel(
                pb.Name, pb.InitialState, pb.StateFqn, pb.StateShort,
                pb.TriggerFqn, pb.TriggerShort, transitions));
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<PartDeclaration> CollectPartDeclarations(INamedTypeSymbol type)
    {
        var partBuilders = ImmutableArray.CreateBuilder<PartDeclaration>();

        foreach (var attr in type.GetAttributes())
        {
            var ac = attr.AttributeClass;
            if (ac is null) continue;
            if (!string.Equals(ac.MetadataName, StateMachinePartAttributeMetadataName, StringComparison.Ordinal)) continue;
            if (ac.TypeArguments.Length != 2) continue;

            var name = attr.NamedArguments
                .FirstOrDefault(kv => string.Equals(kv.Key, "Name", StringComparison.Ordinal)).Value.Value as string;
            var initial = GetEnumMemberName(attr, "InitialState", ac.TypeArguments[0]);
            if (string.IsNullOrEmpty(name) || initial is null) continue;

            partBuilders.Add(new PartDeclaration(
                Name: name!,
                InitialState: initial,
                StateFqn: ac.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                StateShort: ac.TypeArguments[0].Name,
                TriggerFqn: ac.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                TriggerShort: ac.TypeArguments[1].Name));
        }

        return partBuilders.ToImmutable();
    }

    private static System.Collections.Generic.Dictionary<string, ImmutableArray<TransitionModel>.Builder> BucketTransitionsByPart(
        INamedTypeSymbol type,
        ImmutableArray<PartDeclaration> partDeclarations)
    {
        var transitionsByPart = new System.Collections.Generic.Dictionary<string, ImmutableArray<TransitionModel>.Builder>(StringComparer.Ordinal);
        foreach (var pb in partDeclarations)
            transitionsByPart[pb.Name] = ImmutableArray.CreateBuilder<TransitionModel>();

        foreach (var attr in type.GetAttributes())
        {
            var ac = attr.AttributeClass;
            if (ac is null) continue;
            if (!string.Equals(ac.MetadataName, TransitionAttributeMetadataName, StringComparison.Ordinal)) continue;
            if (ac.TypeArguments.Length != 2) continue;

            var partName = attr.NamedArguments
                .FirstOrDefault(kv => string.Equals(kv.Key, "Part", StringComparison.Ordinal)).Value.Value as string;
            if (partName is null) continue;
            if (!transitionsByPart.TryGetValue(partName, out var bucket)) continue;

            var from = GetEnumMemberName(attr, "From", ac.TypeArguments[0]);
            var on   = GetEnumMemberName(attr, "On",   ac.TypeArguments[1]);
            var to   = GetEnumMemberName(attr, "To",   ac.TypeArguments[0]);
            if (from is null || on is null || to is null) continue;

            var hasGuard = attr.NamedArguments
                .FirstOrDefault(kv => string.Equals(kv.Key, "When", StringComparison.Ordinal)).Value.Value is true;
            var afterMs = attr.NamedArguments
                .FirstOrDefault(kv => string.Equals(kv.Key, "AfterMs", StringComparison.Ordinal)).Value.Value is int ms ? ms : 0;

            bucket.Add(new TransitionModel(from, on, to, hasGuard, afterMs, partName));
        }

        return transitionsByPart;
    }

    private static void AnalyzeGroupDiagnostics(
        INamedTypeSymbol type,
        ImmutableArray<StateMachinePartModel> parts,
        bool diagram,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var location = type.Locations.Length > 0 ? type.Locations[0] : Location.None;

        AnalyzeGroupExclusivity(type, location, diagnostics);
        AnalyzeGroupEmpty(type, parts, location, diagnostics);
        AnalyzeDuplicatePartNames(type, parts, location, diagnostics);
        AnalyzeUnknownTransitionParts(type, parts, location, diagnostics);
        AnalyzeCompositeInGroup(type, location, diagnostics);

        var anyTransition = parts.Any(static p => !p.Transitions.IsEmpty);
        if (diagram && !anyTransition)
        {
            diagnostics.Add(Diagnostic.Create(
                StateMachineDiagnostics.EmptyDiagramRequest, location, type.Name));
        }
    }

    // ZSM0014: [StateMachine] and [StateMachineGroup] on the same class
    private static void AnalyzeGroupExclusivity(
        INamedTypeSymbol type, Location location,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var hasStateMachine = type.GetAttributes().Any(a =>
            string.Equals(a.AttributeClass?.MetadataName, StateMachineAttributeMetadataName, StringComparison.Ordinal));
        if (hasStateMachine)
        {
            diagnostics.Add(Diagnostic.Create(
                StateMachineDiagnostics.StateMachineAndGroupExclusive, location, type.Name));
        }
    }

    // ZSM0017: [StateMachineGroup] with zero [StateMachinePart]
    private static void AnalyzeGroupEmpty(
        INamedTypeSymbol type, ImmutableArray<StateMachinePartModel> parts,
        Location location, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (parts.IsEmpty)
        {
            diagnostics.Add(Diagnostic.Create(
                StateMachineDiagnostics.EmptyStateMachineGroup, location, type.Name));
        }
    }

    // ZSM0015: duplicate Name on [StateMachinePart]
    private static void AnalyzeDuplicatePartNames(
        INamedTypeSymbol type, ImmutableArray<StateMachinePartModel> parts,
        Location location, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var p in parts)
        {
            if (!seen.Add(p.Name))
            {
                diagnostics.Add(Diagnostic.Create(
                    StateMachineDiagnostics.DuplicateStateMachinePartName, location, type.Name, p.Name));
            }
        }
    }

    // ZSM0016: [Transition].Part references unknown part (or is null when class is a group)
    private static void AnalyzeUnknownTransitionParts(
        INamedTypeSymbol type, ImmutableArray<StateMachinePartModel> parts,
        Location location, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var partNames = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var p in parts) partNames.Add(p.Name);

        foreach (var attr in type.GetAttributes())
        {
            var ac = attr.AttributeClass;
            if (ac is null) continue;
            if (!string.Equals(ac.MetadataName, TransitionAttributeMetadataName, StringComparison.Ordinal)) continue;
            if (ac.TypeArguments.Length != 2) continue;

            var partName = attr.NamedArguments
                .FirstOrDefault(kv => string.Equals(kv.Key, "Part", StringComparison.Ordinal)).Value.Value as string;

            if (partName is null || !partNames.Contains(partName))
            {
                diagnostics.Add(Diagnostic.Create(
                    StateMachineDiagnostics.TransitionPartUnknown, location,
                    partName ?? "<null>", type.Name));
            }
        }
    }

    // ZSM0018: [CompositeState] on a [StateMachineGroup]
    private static void AnalyzeCompositeInGroup(
        INamedTypeSymbol type, Location location,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var hasComposite = type.GetAttributes().Any(a =>
            string.Equals(a.AttributeClass?.MetadataName, CompositeStateAttributeMetadataName, StringComparison.Ordinal));
        if (hasComposite)
        {
            diagnostics.Add(Diagnostic.Create(
                StateMachineDiagnostics.CompositeStateInGroup, location, type.Name));
        }
    }

    /// <summary>
    /// Build a <see cref="StateMachineModel"/> from a raw <see cref="INamedTypeSymbol"/> without
    /// going through <see cref="GeneratorAttributeSyntaxContext"/>. Used by the Mermaid diagram
    /// writer to resolve sub-FSM types referenced by composite states at parent-emit time.
    /// </summary>
    /// <remarks>
    /// This does not run diagnostic analysis — the returned model's
    /// <see cref="StateMachineModel.Diagnostics"/> is always empty. Sub-FSMs that lack the
    /// <c>[StateMachine]</c> attribute, lack transitions, or lack a resolvable initial state
    /// return <c>null</c>.
    /// </remarks>
    internal static StateMachineModel? BuildModelFromSymbol(INamedTypeSymbol type)
    {
        var smAttr = type.GetAttributes()
            .FirstOrDefault(a => string.Equals(a.AttributeClass?.MetadataName, StateMachineAttributeMetadataName, StringComparison.Ordinal));
        if (smAttr is null) return null;

        var initialState = smAttr.NamedArguments
            .FirstOrDefault(kv => string.Equals(kv.Key, "InitialState", StringComparison.Ordinal)).Value.Value as string ?? string.Empty;
        var concurrent = smAttr.NamedArguments
            .FirstOrDefault(kv => string.Equals(kv.Key, "Concurrent", StringComparison.Ordinal)).Value.Value is true;
        var diagram = smAttr.NamedArguments
            .FirstOrDefault(kv => string.Equals(kv.Key, "Diagram", StringComparison.Ordinal)).Value.Value is true;

        var (transitions, terminalStates, compositeStates, historyStates,
             stateTypeFqn, stateTypeShort, triggerTypeFqn, triggerTypeShort)
            = CollectAttributes(type);

        if (transitions.IsEmpty) return null;
        if (stateTypeFqn is null || triggerTypeFqn is null) return null;
        if (string.IsNullOrEmpty(initialState)) return null;

        var ns = type.ContainingNamespace.IsGlobalNamespace
                 ? null
                 : type.ContainingNamespace.ToDisplayString();
        var isStruct = type.TypeKind == TypeKind.Struct;
        var hasUserCtor = type.InstanceConstructors
            .Any(c => !c.IsImplicitlyDeclared);

        return new StateMachineModel(
            ns, type.Name, isStruct,
            initialState, concurrent,
            stateTypeFqn, stateTypeShort!,
            triggerTypeFqn, triggerTypeShort!,
            transitions, terminalStates,
            compositeStates, historyStates,
            HasUserCtor: hasUserCtor,
            Diagram: diagram,
            Diagnostics: ImmutableArray<Diagnostic>.Empty);
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
        var diagram = smAttr.NamedArguments
            .FirstOrDefault(kv => string.Equals(kv.Key, "Diagram", StringComparison.Ordinal)).Value.Value is true;

        var (transitions, terminalStates, compositeStates, historyStates,
             stateTypeFqn, stateTypeShort, triggerTypeFqn, triggerTypeShort)
            = CollectAttributes(type);

        // If there are no transitions, normally skip — but if Diagram = true, we still
        // want AnalyzeDiagnostics to fire ZSM0020. The RegisterSourceOutput callback
        // short-circuits on empty transitions so the writer is never invoked.
        if (transitions.IsEmpty && !diagram) return null;
        if (!transitions.IsEmpty && (stateTypeFqn is null || triggerTypeFqn is null)) return null;
        if (string.IsNullOrEmpty(initialState)) return null;

        var ns       = type.ContainingNamespace.IsGlobalNamespace
                     ? null
                     : type.ContainingNamespace.ToDisplayString();
        var isStruct = type.TypeKind == TypeKind.Struct;
        var hasUserCtor = type.InstanceConstructors
            .Any(c => !c.IsImplicitlyDeclared);
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        ct.ThrowIfCancellationRequested();
        AnalyzeDiagnostics(initialState, transitions, terminalStates,
            compositeStates, historyStates,
            stateTypeShort ?? string.Empty,
            triggerTypeFqn ?? string.Empty,
            triggerTypeShort ?? string.Empty,
            type, isStruct, concurrent, diagram, diagnostics);

        return new StateMachineModel(
            ns, type.Name, isStruct,
            initialState, concurrent,
            stateTypeFqn ?? string.Empty,
            stateTypeShort ?? string.Empty,
            triggerTypeFqn ?? string.Empty,
            triggerTypeShort ?? string.Empty,
            transitions, terminalStates,
            compositeStates, historyStates,
            HasUserCtor: hasUserCtor,
            Diagram: diagram,
            diagnostics.ToImmutable());
    }

    private static (
        ImmutableArray<TransitionModel> Transitions,
        ImmutableArray<string> TerminalStates,
        ImmutableArray<CompositeStateModel> CompositeStates,
        ImmutableArray<HistoryStateModel> HistoryStates,
        string? StateTypeFqn,
        string? StateTypeShort,
        string? TriggerTypeFqn,
        string? TriggerTypeShort)
        CollectAttributes(INamedTypeSymbol type)
    {
        var transitions    = ImmutableArray.CreateBuilder<TransitionModel>();
        var terminalStates = ImmutableArray.CreateBuilder<string>();
        var compositeStates = ImmutableArray.CreateBuilder<CompositeStateModel>();
        var historyStates   = ImmutableArray.CreateBuilder<HistoryStateModel>();
        string? stateTypeFqn    = null;
        string? stateTypeShort  = null;
        string? triggerTypeFqn  = null;
        string? triggerTypeShort = null;

        // Walk ALL attributes on the type to find [Transition<,>], [Terminal<>], [CompositeState<>], [HistoryState<>]
        foreach (var attr in type.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null) continue;

            var metadataName = attrClass.MetadataName;

            if (string.Equals(metadataName, TransitionAttributeMetadataName, StringComparison.Ordinal) &&
                attrClass.TypeArguments.Length == 2)
            {
                CollectTransition(attr, attrClass, transitions,
                    ref stateTypeFqn, ref stateTypeShort, ref triggerTypeFqn, ref triggerTypeShort);
            }
            else if (string.Equals(metadataName, TerminalAttributeMetadataName, StringComparison.Ordinal) &&
                     attrClass.TypeArguments.Length == 1)
            {
                var stateName = GetEnumMemberName(attr, "State", attrClass.TypeArguments[0]);
                if (stateName is not null) terminalStates.Add(stateName);
            }
            else if (string.Equals(metadataName, CompositeStateAttributeMetadataName, StringComparison.Ordinal) &&
                     attrClass.TypeArguments.Length == 1)
            {
                CollectCompositeState(attr, attrClass, compositeStates);
            }
            else if (string.Equals(metadataName, HistoryStateAttributeMetadataName, StringComparison.Ordinal) &&
                     attrClass.TypeArguments.Length == 1)
            {
                var stateName = GetEnumMemberName(attr, "State", attrClass.TypeArguments[0]);
                if (stateName is not null)
                    historyStates.Add(new HistoryStateModel(stateName));
            }
        }

        return (transitions.ToImmutable(), terminalStates.ToImmutable(),
                compositeStates.ToImmutable(), historyStates.ToImmutable(),
                stateTypeFqn, stateTypeShort, triggerTypeFqn, triggerTypeShort);
    }

    private static void CollectTransition(
        AttributeData attr,
        INamedTypeSymbol attrClass,
        ImmutableArray<TransitionModel>.Builder transitions,
        ref string? stateTypeFqn,
        ref string? stateTypeShort,
        ref string? triggerTypeFqn,
        ref string? triggerTypeShort)
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
        var afterMs = attr.NamedArguments
            .FirstOrDefault(kv => string.Equals(kv.Key, "AfterMs", StringComparison.Ordinal)).Value.Value is int ms ? ms : 0;
        var part = attr.NamedArguments
            .FirstOrDefault(kv => string.Equals(kv.Key, "Part", StringComparison.Ordinal)).Value.Value as string;

        if (from is not null && on is not null && to is not null)
            transitions.Add(new TransitionModel(from, on, to, hasGuard, afterMs, part));
    }

    private static void CollectCompositeState(
        AttributeData attr,
        INamedTypeSymbol attrClass,
        ImmutableArray<CompositeStateModel>.Builder compositeStates)
    {
        var stateName = GetEnumMemberName(attr, "State", attrClass.TypeArguments[0]);
        var subMachineSymbol = attr.NamedArguments
            .FirstOrDefault(kv => string.Equals(kv.Key, "SubMachine", StringComparison.Ordinal))
            .Value.Value as INamedTypeSymbol;

        if (stateName is null || subMachineSymbol is null) return;

        // Resolve the sub-machine's TState by walking its [Transition<TState, TTrigger>] attributes.
        // If the sub-machine is malformed (no transitions), this returns null; Task 5's diagnostics
        // (ZSM0006) will catch the bad declaration separately. Use a placeholder FQN for now so the
        // model stays well-typed.
        var subStateTypeFqn = ResolveSubMachineStateTypeFqn(subMachineSymbol) ?? "global::object";

        compositeStates.Add(new CompositeStateModel(
            State: stateName,
            SubMachineFqn: subMachineSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            SubMachineShort: subMachineSymbol.Name,
            SubMachineStateTypeFqn: subStateTypeFqn));
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

    private static string? ResolveSubMachineStateTypeFqn(INamedTypeSymbol subMachineType)
    {
        foreach (var attr in subMachineType.GetAttributes())
        {
            var ac = attr.AttributeClass;
            if (ac is null) continue;
            if (string.Equals(ac.MetadataName, TransitionAttributeMetadataName, StringComparison.Ordinal) &&
                ac.TypeArguments.Length == 2)
            {
                return ac.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }
        return null;
    }

    private static void AnalyzeDiagnostics(
        string initialState,
        ImmutableArray<TransitionModel> transitions,
        ImmutableArray<string> terminalStates,
        ImmutableArray<CompositeStateModel> compositeStates,
        ImmutableArray<HistoryStateModel> historyStates,
        string stateTypeShort,
        string triggerTypeFqn,
        string triggerTypeShort,
        INamedTypeSymbol type,
        bool isStruct,
        bool concurrent,
        bool diagram,
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
        AnalyzeCompositeStates(compositeStates, historyStates, terminalStates,
            stateTypeShort, triggerTypeFqn, triggerTypeShort, type, concurrent, diagnostics);
        AnalyzeTimedTransitions(transitions, stateTypeShort, type, concurrent, diagnostics);
        AnalyzeDisposeConflict(type, transitions, diagnostics);
        AnalyzeEmptyDiagramRequest(diagram, transitions, type, diagnostics);
    }

    private static void AnalyzeEmptyDiagramRequest(
        bool diagram,
        ImmutableArray<TransitionModel> transitions,
        INamedTypeSymbol type,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (!diagram) return;
        if (!transitions.IsEmpty) return;

        var location = type.Locations.Length > 0 ? type.Locations[0] : Location.None;
        diagnostics.Add(Diagnostic.Create(
            StateMachineDiagnostics.EmptyDiagramRequest, location, type.Name));
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

    private static void AnalyzeCompositeStates(
        ImmutableArray<CompositeStateModel> compositeStates,
        ImmutableArray<HistoryStateModel> historyStates,
        ImmutableArray<string> terminalStates,
        string stateTypeShort,
        string parentTriggerTypeFqn,
        string parentTriggerTypeShort,
        INamedTypeSymbol type,
        bool concurrent,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var location = type.Locations.Length > 0 ? type.Locations[0] : Location.None;

        if (AnalyzeCompositeConcurrent(compositeStates, type, concurrent, location, diagnostics))
            return; // ZSM0005 fired — model is invalid, skip remaining composite analysis

        var seenStates = AnalyzeCompositeDuplicates(compositeStates, stateTypeShort, type, location, diagnostics);
        AnalyzeCompositeInvalidStates(compositeStates, stateTypeShort, type, location, diagnostics);
        AnalyzeCompositeSubMachineValidity(compositeStates, stateTypeShort,
            parentTriggerTypeFqn, parentTriggerTypeShort, type, location, diagnostics);
        AnalyzeOrphanedHistory(historyStates, seenStates, stateTypeShort, type, location, diagnostics);
        AnalyzeCompositeTerminalConflict(compositeStates, terminalStates, stateTypeShort, type, location, diagnostics);
    }

    private static void AnalyzeTimedTransitions(
        ImmutableArray<TransitionModel> transitions,
        string stateTypeShort,
        INamedTypeSymbol type,
        bool concurrent,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var location = type.Locations.Length > 0 ? type.Locations[0] : Location.None;

        foreach (var t in transitions)
        {
            if (t.AfterMs == 0) continue;

            if (t.AfterMs < 0)
            {
                diagnostics.Add(Diagnostic.Create(
                    StateMachineDiagnostics.TimedTransitionInvalidDuration, location,
                    stateTypeShort, t.From, t.On, t.To, t.AfterMs, type.Name));
                continue;
            }

            if (!concurrent)
            {
                diagnostics.Add(Diagnostic.Create(
                    StateMachineDiagnostics.TimedTransitionRequiresConcurrent, location,
                    stateTypeShort, t.From, t.On, t.To, t.AfterMs, type.Name));
            }
        }
    }

    private static void AnalyzeDisposeConflict(
        INamedTypeSymbol type,
        ImmutableArray<TransitionModel> transitions,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var hasTimed = transitions.Any(static t => t.AfterMs > 0);
        if (!hasTimed) return;

        var location = type.Locations.Length > 0 ? type.Locations[0] : Location.None;

        foreach (var member in type.GetMembers("Dispose").OfType<IMethodSymbol>())
        {
            if (member.IsImplicitlyDeclared) continue;
            // Conflict if signature isn't public void Dispose() with no params.
            var isCompatible =
                member.DeclaredAccessibility == Accessibility.Public &&
                member.ReturnsVoid &&
                member.Parameters.Length == 0;
            if (!isCompatible)
            {
                diagnostics.Add(Diagnostic.Create(
                    StateMachineDiagnostics.DisposeSignatureConflict, location,
                    type.Name));
                return; // one diagnostic per type is enough
            }
        }
    }

    // ZSM0005: composite + concurrent
    private static bool AnalyzeCompositeConcurrent(
        ImmutableArray<CompositeStateModel> compositeStates,
        INamedTypeSymbol type,
        bool concurrent,
        Location location,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (concurrent && !compositeStates.IsEmpty)
        {
            diagnostics.Add(Diagnostic.Create(
                StateMachineDiagnostics.CompositeStateOnConcurrentMachine, location,
                type.Name));
            return true;
        }
        return false;
    }

    // ZSM0009: duplicate composite declarations
    private static System.Collections.Generic.HashSet<string> AnalyzeCompositeDuplicates(
        ImmutableArray<CompositeStateModel> compositeStates,
        string stateTypeShort,
        INamedTypeSymbol type,
        Location location,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var seenStates = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var cs in compositeStates)
        {
            if (!seenStates.Add(cs.State))
            {
                diagnostics.Add(Diagnostic.Create(
                    StateMachineDiagnostics.DuplicateCompositeState, location,
                    type.Name, stateTypeShort, cs.State));
            }
        }
        return seenStates;
    }

    // ZSM0008: composite state value not in TState
    private static void AnalyzeCompositeInvalidStates(
        ImmutableArray<CompositeStateModel> compositeStates,
        string stateTypeShort,
        INamedTypeSymbol type,
        Location location,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        INamedTypeSymbol? stateEnum = null;
        foreach (var attr in type.GetAttributes())
        {
            var ac = attr.AttributeClass;
            if (ac is not null &&
                string.Equals(ac.MetadataName, TransitionAttributeMetadataName, StringComparison.Ordinal) &&
                ac.TypeArguments.Length == 2 &&
                ac.TypeArguments[0] is INamedTypeSymbol stEnum)
            {
                stateEnum = stEnum;
                break;
            }
        }
        if (stateEnum is null) return;

        var validStates = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var m in stateEnum.GetMembers().OfType<IFieldSymbol>())
            validStates.Add(m.Name);
        foreach (var cs in compositeStates)
        {
            if (!validStates.Contains(cs.State))
            {
                diagnostics.Add(Diagnostic.Create(
                    StateMachineDiagnostics.CompositeStateInvalidStateValue, location,
                    cs.State, type.Name, stateTypeShort));
            }
        }
    }

    // ZSM0006 + ZSM0007: sub-machine validity
    private static void AnalyzeCompositeSubMachineValidity(
        ImmutableArray<CompositeStateModel> compositeStates,
        string stateTypeShort,
        string parentTriggerTypeFqn,
        string parentTriggerTypeShort,
        INamedTypeSymbol type,
        Location location,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        foreach (var cs in compositeStates)
        {
            var subTypeSymbol = ResolveSubMachineSymbol(type, cs.State);
            if (subTypeSymbol is null) continue;

            var hasStateMachineAttr = subTypeSymbol.GetAttributes().Any(a =>
                string.Equals(a.AttributeClass?.MetadataName, StateMachineAttributeMetadataName, StringComparison.Ordinal));
            if (!hasStateMachineAttr)
            {
                diagnostics.Add(Diagnostic.Create(
                    StateMachineDiagnostics.SubMachineIsNotStateMachine, location,
                    stateTypeShort, cs.State, type.Name, subTypeSymbol.Name));
                continue;
            }

            var subTriggerFqn = ResolveSubMachineTriggerTypeFqn(subTypeSymbol);
            if (subTriggerFqn is not null &&
                !string.Equals(subTriggerFqn, parentTriggerTypeFqn, StringComparison.Ordinal))
            {
                var subTriggerShort = subTriggerFqn.Substring(subTriggerFqn.LastIndexOf('.') + 1);
                diagnostics.Add(Diagnostic.Create(
                    StateMachineDiagnostics.SubMachineTriggerMismatch, location,
                    stateTypeShort, cs.State, type.Name, subTypeSymbol.Name,
                    subTriggerShort, parentTriggerTypeShort));
            }
        }
    }

    // ZSM0010: [HistoryState] without [CompositeState]
    private static void AnalyzeOrphanedHistory(
        ImmutableArray<HistoryStateModel> historyStates,
        System.Collections.Generic.HashSet<string> seenStates,
        string stateTypeShort,
        INamedTypeSymbol type,
        Location location,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        foreach (var hs in historyStates)
        {
            if (!seenStates.Contains(hs.State))
            {
                diagnostics.Add(Diagnostic.Create(
                    StateMachineDiagnostics.HistoryWithoutComposite, location,
                    stateTypeShort, hs.State, type.Name));
            }
        }
    }

    // ZSM0011: composite + [Terminal] on same state
    private static void AnalyzeCompositeTerminalConflict(
        ImmutableArray<CompositeStateModel> compositeStates,
        ImmutableArray<string> terminalStates,
        string stateTypeShort,
        INamedTypeSymbol type,
        Location location,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var terminalSet = new System.Collections.Generic.HashSet<string>(terminalStates, StringComparer.Ordinal);
        foreach (var cs in compositeStates)
        {
            if (terminalSet.Contains(cs.State))
            {
                diagnostics.Add(Diagnostic.Create(
                    StateMachineDiagnostics.CompositeAndTerminalOnSameState, location,
                    stateTypeShort, cs.State, type.Name));
            }
        }
    }

    private static INamedTypeSymbol? ResolveSubMachineSymbol(INamedTypeSymbol parentType, string compositeStateName)
    {
        foreach (var attr in parentType.GetAttributes())
        {
            var ac = attr.AttributeClass;
            if (ac is null) continue;
            if (!string.Equals(ac.MetadataName, CompositeStateAttributeMetadataName, StringComparison.Ordinal))
                continue;
            if (ac.TypeArguments.Length != 1) continue;
            var stateName = GetEnumMemberName(attr, "State", ac.TypeArguments[0]);
            if (!string.Equals(stateName, compositeStateName, StringComparison.Ordinal)) continue;
            if (attr.NamedArguments.FirstOrDefault(kv => string.Equals(kv.Key, "SubMachine", StringComparison.Ordinal))
                                  .Value.Value is INamedTypeSymbol s)
            {
                return s;
            }
        }
        return null;
    }

    private static string? ResolveSubMachineTriggerTypeFqn(INamedTypeSymbol subMachineType)
    {
        foreach (var attr in subMachineType.GetAttributes())
        {
            var ac = attr.AttributeClass;
            if (ac is null) continue;
            if (string.Equals(ac.MetadataName, TransitionAttributeMetadataName, StringComparison.Ordinal) &&
                ac.TypeArguments.Length == 2)
            {
                return ac.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }
        return null;
    }
}
