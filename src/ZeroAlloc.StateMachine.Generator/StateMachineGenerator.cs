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
            .Select(static (m, _) => m!)
            .WithTrackingName("StateMachines");

        context.RegisterSourceOutput(models, static (ctx, model) => EmitStateMachine(ctx, model));

        var groupModels = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                StateMachineGroupAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => ParseGroup(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .WithTrackingName("StateMachineGroups");

        context.RegisterSourceOutput(groupModels, static (ctx, model) =>
        {
            foreach (var diag in model.Diagnostics)
                ctx.ReportDiagnostic(diag.ToDiagnostic());

            if (model.Diagnostics.Any(static d => d.IsError))
                return;

            var source = StateMachineGroupWriter.Write(model);
            ctx.AddSource(model.HintName, source);
        });
    }

    private static void EmitStateMachine(SourceProductionContext ctx, StateMachineModel model)
    {
        foreach (var diag in model.Diagnostics)
            ctx.ReportDiagnostic(diag.ToDiagnostic());

        // Do not emit source if any diagnostic is a hard error — the model is invalid
        if (model.Diagnostics.Any(static d => d.IsError))
            return;

        // Skip emit when there are no transitions — the model was only built so that
        // AnalyzeDiagnostics could fire ZSM0020 (Diagram = true on an empty machine).
        if (model.Transitions.IsEmpty)
            return;

        // Sub-machines were resolved while parsing, so this step needs no compilation: combined
        // with the compilation it ran again, and re-emitted every machine, on every edit.
        System.Func<string, StateMachineModel?> resolver = fqn =>
        {
            foreach (var sub in model.SubMachines)
            {
                if (string.Equals(sub.Fqn, fqn, StringComparison.Ordinal))
                    return sub.Model;
            }
            return null;
        };

        var source = StateMachineWriter.Write(model, resolver);
        ctx.AddSource(model.HintName, source);
    }

    private static StateMachineGroupModel? ParseGroup(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol type) return null;
        ct.ThrowIfCancellationRequested();

        var ns = type.ContainingNamespace.IsGlobalNamespace
                 ? null
                 : type.ContainingNamespace.ToDisplayString();
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();

        var groupAttr = ctx.Attributes[0];
        var diagram = groupAttr.NamedArguments
            .FirstOrDefault(kv => string.Equals(kv.Key, "Diagram", StringComparison.Ordinal)).Value.Value is true;

        var parts = CollectGroupParts(type);
        var hasUserCtor = type.InstanceConstructors.Any(c => !c.IsImplicitlyDeclared);

        ct.ThrowIfCancellationRequested();
        AnalyzeGroupDiagnostics(type, groupAttr, parts, diagram, diagnostics);

        return new StateMachineGroupModel(
            ns, type.Name, HintNames.ForHost(type, ".Group.g.cs"), parts,
            HasUserCtor: hasUserCtor,
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
        AttributeData groupAttr,
        ImmutableArray<StateMachinePartModel> parts,
        bool diagram,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        AnalyzeGroupExclusivity(type, diagnostics);
        AnalyzeGroupEmpty(type, groupAttr, parts, diagnostics);
        AnalyzeDuplicatePartNames(type, diagnostics);
        AnalyzeUnknownTransitionParts(type, parts, diagnostics);
        AnalyzeCompositeInGroup(type, diagnostics);

        var anyTransition = parts.Any(static p => !p.Transitions.IsEmpty);
        if (diagram && !anyTransition)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                StateMachineDiagnostics.EmptyDiagramRequest,
                GetNamedArgumentLocation(groupAttr, "Diagram", type), type.Name));
        }

        // Every [StateMachinePart] is concurrent, so a guard on any part's transition is dropped.
        AnalyzeGuardsOnConcurrentMachine(type, concurrent: true, diagnostics);

        var hasTimedInGroup = parts.Any(static p => p.Transitions.Any(static t => t.AfterMs > 0));
        AnalyzeMissingHookConstructorInvocation(type, hasTimedInGroup, diagnostics);
    }

    // ZSM0014: [StateMachine] and [StateMachineGroup] on the same class, at the [StateMachine]
    private static void AnalyzeGroupExclusivity(
        INamedTypeSymbol type,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        var stateMachineAttr = type.GetAttributes().FirstOrDefault(a =>
            string.Equals(a.AttributeClass?.MetadataName, StateMachineAttributeMetadataName, StringComparison.Ordinal));
        if (stateMachineAttr is not null)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                StateMachineDiagnostics.StateMachineAndGroupExclusive,
                GetAttributeLocation(stateMachineAttr, type), type.Name));
        }
    }

    // ZSM0017: [StateMachineGroup] with zero [StateMachinePart], at the [StateMachineGroup]
    private static void AnalyzeGroupEmpty(
        INamedTypeSymbol type, AttributeData groupAttr,
        ImmutableArray<StateMachinePartModel> parts,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        if (parts.IsEmpty)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                StateMachineDiagnostics.EmptyStateMachineGroup,
                GetAttributeLocation(groupAttr, type), type.Name));
        }
    }

    // ZSM0015: duplicate Name on [StateMachinePart], at the Name of each later duplicate. Walks
    // the attributes CollectPartDeclarations keeps, in the same order, so a part it drops for a
    // missing Name or InitialState is not reported here either.
    private static void AnalyzeDuplicatePartNames(
        INamedTypeSymbol type,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
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

            if (!seen.Add(name!))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    StateMachineDiagnostics.DuplicateStateMachinePartName,
                    GetNamedArgumentLocation(attr, "Name", type), type.Name, name));
            }
        }
    }

    // ZSM0016: [Transition].Part references unknown part (or is null when class is a group), at
    // the Part argument, or at the [Transition] when it has none
    private static void AnalyzeUnknownTransitionParts(
        INamedTypeSymbol type, ImmutableArray<StateMachinePartModel> parts,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
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
                diagnostics.Add(DiagnosticInfo.Create(
                    StateMachineDiagnostics.TransitionPartUnknown,
                    GetNamedArgumentLocation(attr, "Part", type),
                    partName ?? "<null>", type.Name));
            }
        }
    }

    // ZSM0018: [CompositeState] on a [StateMachineGroup], at the first [CompositeState]
    private static void AnalyzeCompositeInGroup(
        INamedTypeSymbol type,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        var compositeAttr = type.GetAttributes().FirstOrDefault(a =>
            string.Equals(a.AttributeClass?.MetadataName, CompositeStateAttributeMetadataName, StringComparison.Ordinal));
        if (compositeAttr is not null)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                StateMachineDiagnostics.CompositeStateInGroup,
                GetAttributeLocation(compositeAttr, type), type.Name));
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
        var hasUserParameterlessCtor = type.InstanceConstructors
            .Any(c => !c.IsImplicitlyDeclared && c.Parameters.IsEmpty);

        return new StateMachineModel(
            ns, type.Name, HintNames.ForHost(type, ".g.cs"), isStruct,
            initialState, concurrent,
            stateTypeFqn, stateTypeShort!,
            triggerTypeFqn, triggerTypeShort!,
            transitions, terminalStates,
            compositeStates, historyStates,
            HasUserCtor: hasUserCtor,
            HasUserParameterlessCtor: hasUserParameterlessCtor,
            Diagram: diagram,
            SubMachines: EquatableArray<SubMachineModel>.Empty,
            Diagnostics: EquatableArray<DiagnosticInfo>.Empty);
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
        var hasUserParameterlessCtor = type.InstanceConstructors
            .Any(c => !c.IsImplicitlyDeclared && c.Parameters.IsEmpty);
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();

        ct.ThrowIfCancellationRequested();
        AnalyzeDiagnostics(initialState, transitions, terminalStates,
            historyStates,
            stateTypeShort ?? string.Empty,
            triggerTypeFqn ?? string.Empty,
            triggerTypeShort ?? string.Empty,
            type, smAttr, isStruct, concurrent, diagram, diagnostics);

        var subMachines = diagram && !compositeStates.IsEmpty
            ? CollectSubMachines(type)
            : ImmutableArray<SubMachineModel>.Empty;

        return new StateMachineModel(
            ns, type.Name, HintNames.ForHost(type, ".g.cs"), isStruct,
            initialState, concurrent,
            stateTypeFqn ?? string.Empty,
            stateTypeShort ?? string.Empty,
            triggerTypeFqn ?? string.Empty,
            triggerTypeShort ?? string.Empty,
            transitions, terminalStates,
            compositeStates, historyStates,
            HasUserCtor: hasUserCtor,
            HasUserParameterlessCtor: hasUserParameterlessCtor,
            Diagram: diagram,
            SubMachines: subMachines,
            Diagnostics: diagnostics.ToImmutable());
    }

    /// <summary>
    /// Every sub-machine the Mermaid diagram of <paramref name="type"/> expands: those of its
    /// composite states, and theirs in turn. Each is listed once, which also stops a cycle.
    /// </summary>
    private static ImmutableArray<SubMachineModel> CollectSubMachines(INamedTypeSymbol type)
    {
        var result = ImmutableArray.CreateBuilder<SubMachineModel>();
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        var pending = new System.Collections.Generic.Stack<INamedTypeSymbol>();
        pending.Push(type);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var attr in current.GetAttributes())
            {
                if (!string.Equals(attr.AttributeClass?.MetadataName, CompositeStateAttributeMetadataName, StringComparison.Ordinal))
                    continue;
                if (attr.NamedArguments.FirstOrDefault(kv => string.Equals(kv.Key, "SubMachine", StringComparison.Ordinal))
                        .Value.Value is not INamedTypeSymbol sub)
                    continue;

                var fqn = sub.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (!seen.Add(fqn)) continue;

                var model = BuildModelFromSymbol(sub);
                if (model is null) continue;

                result.Add(new SubMachineModel(fqn, model));
                pending.Push(sub);
            }
        }

        return result.ToImmutable();
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
            SubMachineStateTypeFqn: subStateTypeFqn,
            SubMachineIsStruct: subMachineSymbol.TypeKind == TypeKind.Struct));
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
        ImmutableArray<HistoryStateModel> historyStates,
        string stateTypeShort,
        string triggerTypeFqn,
        string triggerTypeShort,
        INamedTypeSymbol type,
        AttributeData smAttr,
        bool isStruct,
        bool concurrent,
        bool diagram,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        // ZSM0004: at the Concurrent argument a struct cannot honour
        if (isStruct && concurrent)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                StateMachineDiagnostics.StructConcurrentNotSupported,
                GetNamedArgumentLocation(smAttr, "Concurrent", type),
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

        AnalyzeReachability(initialState, terminalStates, stateTypeShort, type, allFromStates, allToStates, diagnostics);
        AnalyzeTriggerUsage(type, allTriggers, diagnostics);
        AnalyzeCompositeStates(historyStates, terminalStates,
            stateTypeShort, triggerTypeFqn, triggerTypeShort, type, concurrent, diagnostics);
        AnalyzeTimedTransitions(stateTypeShort, type, concurrent, diagnostics);
        AnalyzeDisposeConflict(type, transitions, diagnostics);
        AnalyzeEmptyDiagramRequest(diagram, transitions, type, smAttr, diagnostics);
        AnalyzeGuardsOnConcurrentMachine(type, concurrent, diagnostics);

        var hasTimed = transitions.Any(static t => t.AfterMs > 0);
        AnalyzeMissingHookConstructorInvocation(type, hasTimed, diagnostics);
    }

    // ZSM0022: When = true on a concurrent machine. The writer emits no guard for it, so the
    // transition fires unconditionally; report it at the When argument so the user sees which
    // edge lost its guard and can suppress it per transition.
    private static void AnalyzeGuardsOnConcurrentMachine(
        INamedTypeSymbol type,
        bool concurrent,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        if (!concurrent) return;

        foreach (var attr in type.GetAttributes())
        {
            var ac = attr.AttributeClass;
            if (ac is null) continue;
            if (!string.Equals(ac.MetadataName, TransitionAttributeMetadataName, StringComparison.Ordinal)) continue;
            if (ac.TypeArguments.Length != 2) continue;

            var hasGuard = attr.NamedArguments
                .FirstOrDefault(kv => string.Equals(kv.Key, "When", StringComparison.Ordinal)).Value.Value is true;
            if (!hasGuard) continue;

            var from = GetEnumMemberName(attr, "From", ac.TypeArguments[0]);
            var on   = GetEnumMemberName(attr, "On",   ac.TypeArguments[1]);
            var to   = GetEnumMemberName(attr, "To",   ac.TypeArguments[0]);
            if (from is null || on is null || to is null) continue;

            diagnostics.Add(DiagnosticInfo.Create(
                StateMachineDiagnostics.GuardIgnoredOnConcurrentMachine,
                GetNamedArgumentLocation(attr, "When", type),
                ac.TypeArguments[0].Name, from, ac.TypeArguments[1].Name, on, to, type.Name));
        }
    }

    /// <summary>
    /// The named argument <paramref name="name"/> of <paramref name="attr"/>, or the attribute
    /// when it has no such argument. The location is in the attribute's own syntax tree, so
    /// <c>#pragma warning disable</c> around the attribute covers it.
    /// </summary>
    private static Location GetNamedArgumentLocation(AttributeData attr, string name, INamedTypeSymbol type)
    {
        if (attr.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax syntax)
        {
            var argument = syntax.ArgumentList?.Arguments.FirstOrDefault(a =>
                string.Equals(a.NameEquals?.Name.Identifier.ValueText, name, StringComparison.Ordinal));
            return (argument ?? (SyntaxNode)syntax).GetLocation();
        }

        return GetTypeLocation(type);
    }

    private static Location GetAttributeLocation(AttributeData attr, INamedTypeSymbol type) =>
        attr.ApplicationSyntaxReference is { } reference
            ? Location.Create(reference.SyntaxTree, reference.Span)
            : GetTypeLocation(type);

    // Fallback for an attribute without syntax, which one applied in source never is. The
    // generator only runs on types declared in source, so the type has a source location.
    private static Location GetTypeLocation(INamedTypeSymbol type) => type.Locations[0];

    /// <summary>
    /// The [Transition] attributes <see cref="CollectTransition"/> keeps, in declaration order,
    /// with their resolved From, On and To.
    /// </summary>
    private static System.Collections.Generic.IEnumerable<(AttributeData Attr, string From, string On, string To)> TransitionAttributes(
        INamedTypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            var ac = attr.AttributeClass;
            if (ac is null) continue;
            if (!string.Equals(ac.MetadataName, TransitionAttributeMetadataName, StringComparison.Ordinal)) continue;
            if (ac.TypeArguments.Length != 2) continue;

            var from = GetEnumMemberName(attr, "From", ac.TypeArguments[0]);
            var on   = GetEnumMemberName(attr, "On",   ac.TypeArguments[1]);
            var to   = GetEnumMemberName(attr, "To",   ac.TypeArguments[0]);
            if (from is null || on is null || to is null) continue;

            yield return (attr, from, on, to);
        }
    }

    /// <summary>
    /// The first [Transition] whose From, On or To, as <paramref name="selector"/> picks it,
    /// is <paramref name="value"/>, at its <paramref name="argument"/>.
    /// </summary>
    private static Location GetTransitionArgumentLocation(
        INamedTypeSymbol type, string argument,
        System.Func<(AttributeData Attr, string From, string On, string To), string> selector, string value)
    {
        foreach (var t in TransitionAttributes(type))
        {
            if (string.Equals(selector(t), value, StringComparison.Ordinal))
                return GetNamedArgumentLocation(t.Attr, argument, type);
        }
        return GetTypeLocation(type);
    }

    /// <summary>
    /// The single-type-argument attributes named <paramref name="metadataName"/> whose State
    /// resolves, in declaration order.
    /// </summary>
    private static System.Collections.Generic.IEnumerable<(AttributeData Attr, string State)> StateAttributes(
        INamedTypeSymbol type, string metadataName)
    {
        foreach (var attr in type.GetAttributes())
        {
            var ac = attr.AttributeClass;
            if (ac is null) continue;
            if (!string.Equals(ac.MetadataName, metadataName, StringComparison.Ordinal)) continue;
            if (ac.TypeArguments.Length != 1) continue;

            var state = GetEnumMemberName(attr, "State", ac.TypeArguments[0]);
            if (state is null) continue;

            yield return (attr, state);
        }
    }

    private static void AnalyzeEmptyDiagramRequest(
        bool diagram,
        ImmutableArray<TransitionModel> transitions,
        INamedTypeSymbol type,
        AttributeData smAttr,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        if (!diagram) return;
        if (!transitions.IsEmpty) return;

        diagnostics.Add(DiagnosticInfo.Create(
            StateMachineDiagnostics.EmptyDiagramRequest,
            GetNamedArgumentLocation(smAttr, "Diagram", type), type.Name));
    }

    private static void AnalyzeMissingHookConstructorInvocation(
        INamedTypeSymbol type,
        bool hasTimedEdges,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        if (!hasTimedEdges) return;

        var userCtors = type.InstanceConstructors
            .Where(c => !c.IsImplicitlyDeclared)
            .ToArray();
        if (userCtors.Length == 0) return;

        foreach (var ctor in userCtors)
        {
            if (CtorInvokesHookConstructor(ctor)) return;
        }

        // At the first user constructor, since none of them arms the initial-state timers.
        diagnostics.Add(DiagnosticInfo.Create(
            StateMachineDiagnostics.MissingHookConstructorInvocation, userCtors[0].Locations[0], type.Name));
    }

    private static bool CtorInvokesHookConstructor(IMethodSymbol ctor)
    {
        foreach (var syntaxRef in ctor.DeclaringSyntaxReferences)
        {
            var node = syntaxRef.GetSyntax();
            if (node is null) continue;

            // Walk the ctor body's descendant invocations; look for HookConstructor(),
            // this.HookConstructor(), or base.HookConstructor().
            foreach (var inv in node.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>())
            {
                var name = inv.Expression switch
                {
                    Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax id => id.Identifier.ValueText,
                    Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax
                    {
                        Expression: Microsoft.CodeAnalysis.CSharp.Syntax.ThisExpressionSyntax or Microsoft.CodeAnalysis.CSharp.Syntax.BaseExpressionSyntax,
                        Name: Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax memberId
                    } => memberId.Identifier.ValueText,
                    _ => null,
                };
                if (string.Equals(name, "HookConstructor", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static void AnalyzeReachability(
        string initialState,
        ImmutableArray<string> terminalStates,
        string stateTypeShort,
        INamedTypeSymbol type,
        System.Collections.Generic.HashSet<string> allFromStates,
        System.Collections.Generic.HashSet<string> allToStates,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        // ZSM0001: states that appear in From but never in To and are not InitialState → unreachable
        foreach (var fromState in allFromStates)
        {
            if (!allToStates.Contains(fromState) &&
                !string.Equals(fromState, initialState, StringComparison.Ordinal))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    StateMachineDiagnostics.UnreachableState,
                    GetTransitionArgumentLocation(type, "From", static t => t.From, fromState),
                    fromState, type.Name));
            }
        }

        // ZSM0002: states that appear in To but never in From and are not declared [Terminal]
        var terminalSet = new System.Collections.Generic.HashSet<string>(terminalStates, StringComparer.Ordinal);
        foreach (var toState in allToStates)
        {
            if (!allFromStates.Contains(toState) && !terminalSet.Contains(toState))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    StateMachineDiagnostics.SinkState,
                    GetTransitionArgumentLocation(type, "To", static t => t.To, toState),
                    toState, type.Name, stateTypeShort));
            }
        }
    }

    private static void AnalyzeTriggerUsage(
        INamedTypeSymbol type,
        string[] allTriggers,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        // ZSM0003: a trigger used in exactly one transition whose name is close to a trigger
        // used in several. A typo appears once, next to the real trigger it was meant to be.
        // A single-use trigger with a distinct name is a normal exit event and is not flagged.
        var triggerCounts = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
        var distinctTriggers = new System.Collections.Generic.List<string>();
        foreach (var trigger in allTriggers)
        {
            if (triggerCounts.TryGetValue(trigger, out var count))
            {
                triggerCounts[trigger] = count + 1;
            }
            else
            {
                triggerCounts[trigger] = 1;
                distinctTriggers.Add(trigger);
            }
        }

        // Distinct triggers are walked in first-use order so the diagnostics are deterministic.
        for (var c = 0; c < distinctTriggers.Count; c++)
        {
            var candidate = distinctTriggers[c];
            if (triggerCounts[candidate] != 1) continue;

            string? intended = null;
            var bestDistance = int.MaxValue;
            for (var r = 0; r < distinctTriggers.Count; r++)
            {
                var reused = distinctTriggers[r];
                if (triggerCounts[reused] < 2) continue;

                var maxDistance = MaxTypoDistance(candidate, reused);
                var distance = TriggerNameDistance(candidate, reused, maxDistance);
                // Strictly closer only: on a tie the reused trigger that appears first wins.
                if (distance <= maxDistance && distance < bestDistance)
                {
                    intended = reused;
                    bestDistance = distance;
                }
            }

            if (intended is not null)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    StateMachineDiagnostics.SingleUseTrigger,
                    GetTransitionArgumentLocation(type, "On", static t => t.On, candidate),
                    candidate, type.Name, intended));
            }
        }
    }

    // Names of five or more characters tolerate two edits; shorter names only one, so that
    // unrelated short names such as Tap and Trip are not mistaken for typos of each other.
    private static int MaxTypoDistance(string a, string b)
        => Math.Min(a.Length, b.Length) >= 5 ? 2 : 1;

    /// <summary>
    /// Case-insensitive Damerau-Levenshtein distance, optimal string alignment variant:
    /// insertions, deletions, substitutions and transpositions of adjacent characters each
    /// cost one edit. Returns <paramref name="maxDistance"/> + 1 without computing the table
    /// when the length difference alone already exceeds it.
    /// </summary>
    private static int TriggerNameDistance(string a, string b, int maxDistance)
    {
        if (Math.Abs(a.Length - b.Length) > maxDistance) return maxDistance + 1;

        // Three rolling rows: two rows back is needed for the transposition case.
        var previousPrevious = new int[b.Length + 1];
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var ai = char.ToUpperInvariant(a[i - 1]);
            for (var j = 1; j <= b.Length; j++)
            {
                var bj = char.ToUpperInvariant(b[j - 1]);
                var cost = ai == bj ? 0 : 1;
                var value = Math.Min(
                    Math.Min(previous[j] + 1, current[j - 1] + 1),
                    previous[j - 1] + cost);
                if (i > 1 && j > 1 &&
                    ai == char.ToUpperInvariant(b[j - 2]) &&
                    char.ToUpperInvariant(a[i - 2]) == bj)
                {
                    value = Math.Min(value, previousPrevious[j - 2] + 1);
                }

                current[j] = value;
            }

            var recycled = previousPrevious;
            previousPrevious = previous;
            previous = current;
            current = recycled;
        }

        return previous[b.Length];
    }

    private static void AnalyzeCompositeStates(
        ImmutableArray<HistoryStateModel> historyStates,
        ImmutableArray<string> terminalStates,
        string stateTypeShort,
        string parentTriggerTypeFqn,
        string parentTriggerTypeShort,
        INamedTypeSymbol type,
        bool concurrent,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        var composites = CompositeAttributes(type);

        if (AnalyzeCompositeConcurrent(composites, type, concurrent, diagnostics))
            return; // ZSM0005 fired — model is invalid, skip remaining composite analysis

        var seenStates = AnalyzeCompositeDuplicates(composites, stateTypeShort, type, diagnostics);
        AnalyzeCompositeInvalidStates(composites, stateTypeShort, type, diagnostics);
        AnalyzeCompositeSubMachineValidity(composites, stateTypeShort,
            parentTriggerTypeFqn, parentTriggerTypeShort, type, diagnostics);
        AnalyzeOrphanedHistory(historyStates, seenStates, stateTypeShort, type, diagnostics);
        AnalyzeCompositeTerminalConflict(composites, terminalStates, stateTypeShort, type, diagnostics);
    }

    /// <summary>
    /// The [CompositeState] attributes <see cref="CollectCompositeState"/> keeps, in the same
    /// order, with their resolved State and SubMachine, so each finding can point at its own.
    /// </summary>
    private static ImmutableArray<(AttributeData Attr, string State, INamedTypeSymbol SubMachine)> CompositeAttributes(
        INamedTypeSymbol type)
    {
        var result = ImmutableArray.CreateBuilder<(AttributeData Attr, string State, INamedTypeSymbol SubMachine)>();
        foreach (var (attr, state) in StateAttributes(type, CompositeStateAttributeMetadataName))
        {
            if (attr.NamedArguments.FirstOrDefault(kv => string.Equals(kv.Key, "SubMachine", StringComparison.Ordinal))
                    .Value.Value is INamedTypeSymbol subMachine)
            {
                result.Add((attr, state, subMachine));
            }
        }
        return result.ToImmutable();
    }

    private static void AnalyzeTimedTransitions(
        string stateTypeShort,
        INamedTypeSymbol type,
        bool concurrent,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        // The transitions CollectTransition keeps, walked from their attributes so each finding
        // points at the AfterMs argument of its own transition.
        foreach (var (attr, from, on, to) in TransitionAttributes(type))
        {
            var afterMs = attr.NamedArguments
                .FirstOrDefault(kv => string.Equals(kv.Key, "AfterMs", StringComparison.Ordinal)).Value.Value is int ms ? ms : 0;
            if (afterMs == 0) continue;

            var location = GetNamedArgumentLocation(attr, "AfterMs", type);
            if (afterMs < 0)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    StateMachineDiagnostics.TimedTransitionInvalidDuration, location,
                    stateTypeShort, from, on, to, afterMs, type.Name));
                continue;
            }

            if (!concurrent)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    StateMachineDiagnostics.TimedTransitionRequiresConcurrent, location,
                    stateTypeShort, from, on, to, afterMs, type.Name));
            }
        }
    }

    private static void AnalyzeDisposeConflict(
        INamedTypeSymbol type,
        ImmutableArray<TransitionModel> transitions,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        var hasTimed = transitions.Any(static t => t.AfterMs > 0);
        if (!hasTimed) return;

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
                // At the conflicting Dispose itself.
                diagnostics.Add(DiagnosticInfo.Create(
                    StateMachineDiagnostics.DisposeSignatureConflict, member.Locations[0],
                    type.Name));
                return; // one diagnostic per type is enough
            }
        }
    }

    // ZSM0005: composite + concurrent, at the first [CompositeState]
    private static bool AnalyzeCompositeConcurrent(
        ImmutableArray<(AttributeData Attr, string State, INamedTypeSymbol SubMachine)> composites,
        INamedTypeSymbol type,
        bool concurrent,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        if (concurrent && !composites.IsEmpty)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                StateMachineDiagnostics.CompositeStateOnConcurrentMachine,
                GetAttributeLocation(composites[0].Attr, type),
                type.Name));
            return true;
        }
        return false;
    }

    // ZSM0009: duplicate composite declarations, at each later duplicate
    private static System.Collections.Generic.HashSet<string> AnalyzeCompositeDuplicates(
        ImmutableArray<(AttributeData Attr, string State, INamedTypeSymbol SubMachine)> composites,
        string stateTypeShort,
        INamedTypeSymbol type,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        var seenStates = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var cs in composites)
        {
            if (!seenStates.Add(cs.State))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    StateMachineDiagnostics.DuplicateCompositeState,
                    GetAttributeLocation(cs.Attr, type),
                    type.Name, stateTypeShort, cs.State));
            }
        }
        return seenStates;
    }

    // ZSM0008: composite state value not in TState, at its State argument
    private static void AnalyzeCompositeInvalidStates(
        ImmutableArray<(AttributeData Attr, string State, INamedTypeSymbol SubMachine)> composites,
        string stateTypeShort,
        INamedTypeSymbol type,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
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
        foreach (var cs in composites)
        {
            if (!validStates.Contains(cs.State))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    StateMachineDiagnostics.CompositeStateInvalidStateValue,
                    GetNamedArgumentLocation(cs.Attr, "State", type),
                    cs.State, type.Name, stateTypeShort));
            }
        }
    }

    // ZSM0006 + ZSM0007: sub-machine validity, at the SubMachine argument
    private static void AnalyzeCompositeSubMachineValidity(
        ImmutableArray<(AttributeData Attr, string State, INamedTypeSymbol SubMachine)> composites,
        string stateTypeShort,
        string parentTriggerTypeFqn,
        string parentTriggerTypeShort,
        INamedTypeSymbol type,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        foreach (var cs in composites)
        {
            var subTypeSymbol = cs.SubMachine;
            var location = GetNamedArgumentLocation(cs.Attr, "SubMachine", type);

            var hasStateMachineAttr = subTypeSymbol.GetAttributes().Any(a =>
                string.Equals(a.AttributeClass?.MetadataName, StateMachineAttributeMetadataName, StringComparison.Ordinal));
            if (!hasStateMachineAttr)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    StateMachineDiagnostics.SubMachineIsNotStateMachine, location,
                    stateTypeShort, cs.State, type.Name, subTypeSymbol.Name));
                continue;
            }

            var subTriggerFqn = ResolveSubMachineTriggerTypeFqn(subTypeSymbol);
            if (subTriggerFqn is not null &&
                !string.Equals(subTriggerFqn, parentTriggerTypeFqn, StringComparison.Ordinal))
            {
                var subTriggerShort = subTriggerFqn.Substring(subTriggerFqn.LastIndexOf('.') + 1);
                diagnostics.Add(DiagnosticInfo.Create(
                    StateMachineDiagnostics.SubMachineTriggerMismatch, location,
                    stateTypeShort, cs.State, type.Name, subTypeSymbol.Name,
                    subTriggerShort, parentTriggerTypeShort));
            }
        }
    }

    // ZSM0010: [HistoryState] without [CompositeState], at the [HistoryState]
    private static void AnalyzeOrphanedHistory(
        ImmutableArray<HistoryStateModel> historyStates,
        System.Collections.Generic.HashSet<string> seenStates,
        string stateTypeShort,
        INamedTypeSymbol type,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        if (historyStates.IsEmpty) return;

        foreach (var (attr, state) in StateAttributes(type, HistoryStateAttributeMetadataName))
        {
            if (!seenStates.Contains(state))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    StateMachineDiagnostics.HistoryWithoutComposite,
                    GetAttributeLocation(attr, type),
                    stateTypeShort, state, type.Name));
            }
        }
    }

    // ZSM0011: composite + [Terminal] on same state, at the [Terminal]
    private static void AnalyzeCompositeTerminalConflict(
        ImmutableArray<(AttributeData Attr, string State, INamedTypeSymbol SubMachine)> composites,
        ImmutableArray<string> terminalStates,
        string stateTypeShort,
        INamedTypeSymbol type,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        if (terminalStates.IsEmpty) return;

        foreach (var cs in composites)
        {
            foreach (var (attr, state) in StateAttributes(type, TerminalAttributeMetadataName))
            {
                if (!string.Equals(state, cs.State, StringComparison.Ordinal)) continue;

                diagnostics.Add(DiagnosticInfo.Create(
                    StateMachineDiagnostics.CompositeAndTerminalOnSameState,
                    GetAttributeLocation(attr, type),
                    stateTypeShort, cs.State, type.Name));
                break;
            }
        }
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
