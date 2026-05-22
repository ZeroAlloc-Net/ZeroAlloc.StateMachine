# Composite states + shallow history Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Land `ZeroAlloc.StateMachine` backlog items B1 (hierarchical / nested states) and B2 (shallow history) — a new `[CompositeState<TState>(State = X, SubMachine = typeof(SubFsm))]` + `[HistoryState<TState>(State = X)]` attribute pair, plus the generator extensions that wire sub-FSMs into the parent's `TryFire` dispatch and capture/restore the sub-state across composite re-entries.

**Architecture:** Two new public attribute types. `StateMachineModel` gains composite + history metadata via a Roslyn pass that walks `[CompositeState]` / `[HistoryState]` attributes. The generator emits, on every `[StateMachine]` class, two new `internal` methods `Reset()` / `ResetTo(TState)` (state-population mechanics that do NOT fire `OnExit`/`OnEnter`). On parent classes with composites, it additionally emits one `_subFsm_{State}` field per composite, per-composite history fields, a two-step `TryFire` (sub-FSM first then parent's own switch), and extended `Fire` that captures history on exit and resets/restores sub-FSM on enter. New diagnostics `ZSM0005`–`ZSM0011` cover declaration errors.

**Tech Stack:** .NET 10 / netstandard2.0 (Roslyn source generator targets), Roslyn `IIncrementalGenerator`, `Microsoft.CodeAnalysis.PublicApiAnalyzers` (RS0016/RS0017 enforce additive PublicAPI), xUnit + VerifyXunit (existing snapshot test convention via `tests/.../GeneratorSnapshotTests.cs` + `TestHelper.cs`).

**Design doc:** `docs/plans/2026-05-22-composite-states-history-design.md` (committed in `7158b3b`)

**Working branch:** `feat/composite-states-and-history` (already created off `main`; design doc commit `7158b3b` is the current HEAD).

**Key context:**
- The current model lives at `src/ZeroAlloc.StateMachine.Generator/StateMachineModel.cs` — an immutable `record` with positional params. Extending it means adding new positional params + matching constructor call.
- The current generator entry point is `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs`'s `Parse` + `CollectAttributes`. New attributes are walked via `type.GetAttributes()` (same pattern as `[Transition]` / `[Terminal]`).
- Emit shape lives in `src/ZeroAlloc.StateMachine.Generator/StateMachineWriter.cs` — a `StringBuilder` walker with separate `WriteConcurrentBody` / `WriteNonConcurrentBody` paths.
- Tests use `TestHelper.Verify<StateMachineGenerator>(source)` for snapshot testing (VerifyXunit; first run writes `.received.cs`, second run requires the file renamed to `.verified.cs`).
- Existing diagnostics max ID is `ZSM0004`; new ones start at `ZSM0005`.

---

## Task 1: Add `CompositeStateAttribute<TState>` + `HistoryStateAttribute<TState>` runtime types

**Files:**
- Create: `src/ZeroAlloc.StateMachine/CompositeStateAttribute.cs`
- Create: `src/ZeroAlloc.StateMachine/HistoryStateAttribute.cs`
- Modify: `src/ZeroAlloc.StateMachine/PublicAPI.Unshipped.txt`

**Step 1: Add `CompositeStateAttribute.cs`**

```csharp
namespace ZeroAlloc.StateMachine;

using System;

/// <summary>
/// Declares that a state of the enclosing state machine is a composite — when the machine
/// is in <see cref="State"/>, triggers are first dispatched to the sub-machine instance
/// (a <c>[StateMachine]</c> partial class identified by <see cref="SubMachine"/>) before
/// falling through to the parent's own transition table.
/// </summary>
/// <typeparam name="TState">The state enum type of the enclosing state machine.</typeparam>
/// <remarks>
/// <para>The sub-machine must:</para>
/// <list type="bullet">
///   <item>be a <c>partial</c> class with its own <c>[StateMachine]</c> attribute;</item>
///   <item>declare transitions using the SAME <c>TTrigger</c> as the parent (its own <c>TState</c> is independent);</item>
///   <item>NOT itself be in concurrent mode (composite states are sequential-only — see <c>ZSM0005</c>).</item>
/// </list>
/// <para>Composite states are mutually exclusive with <c>[Terminal]</c> on the same state (see <c>ZSM0011</c>).</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public sealed class CompositeStateAttribute<TState> : Attribute
    where TState : struct, Enum
{
    /// <summary>Parent state whose dispatch is delegated to the sub-machine.</summary>
    public required TState State { get; init; }

    /// <summary>Type of the sub-machine — must be a <c>[StateMachine]</c> partial class with the same <c>TTrigger</c>.</summary>
    public required Type SubMachine { get; init; }
}
```

**Step 2: Add `HistoryStateAttribute.cs`**

```csharp
namespace ZeroAlloc.StateMachine;

using System;

/// <summary>
/// Declares shallow history on a composite state. When the composite is re-entered after
/// having been previously exited, the sub-machine resumes at its last leaf state (the state
/// it was in at the moment of exit) instead of resetting to its declared initial state.
/// </summary>
/// <typeparam name="TState">The state enum type of the enclosing state machine.</typeparam>
/// <remarks>
/// Must accompany a <c>[CompositeState(State = X)]</c> on the same class — declaring
/// <c>[HistoryState(State = X)]</c> alone emits <c>ZSM0010</c>. History is shallow only:
/// nested sub-machines are always reset to their initial state when their containing
/// sub-machine is restored.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public sealed class HistoryStateAttribute<TState> : Attribute
    where TState : struct, Enum
{
    /// <summary>Composite state that should remember its sub-machine's last leaf state across exits and re-entries.</summary>
    public required TState State { get; init; }
}
```

**Step 3: Update `PublicAPI.Unshipped.txt`**

Append (alphabetical order — paste into the right position):

```
ZeroAlloc.StateMachine.CompositeStateAttribute<TState>
ZeroAlloc.StateMachine.CompositeStateAttribute<TState>.CompositeStateAttribute() -> void
ZeroAlloc.StateMachine.CompositeStateAttribute<TState>.State.get -> TState
ZeroAlloc.StateMachine.CompositeStateAttribute<TState>.State.init -> void
ZeroAlloc.StateMachine.CompositeStateAttribute<TState>.SubMachine.get -> System.Type!
ZeroAlloc.StateMachine.CompositeStateAttribute<TState>.SubMachine.init -> void
ZeroAlloc.StateMachine.HistoryStateAttribute<TState>
ZeroAlloc.StateMachine.HistoryStateAttribute<TState>.HistoryStateAttribute() -> void
ZeroAlloc.StateMachine.HistoryStateAttribute<TState>.State.get -> TState
ZeroAlloc.StateMachine.HistoryStateAttribute<TState>.State.init -> void
```

If `RS0016`/`RS0017` fires during build, accept the analyzer's suggested form verbatim — it's the source of truth for nullable-annotation placement.

**Step 4: Verify build**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.StateMachine
dotnet build src/ZeroAlloc.StateMachine/ZeroAlloc.StateMachine.csproj -c Release
```

Expected: 0 warnings, 0 errors. `TreatWarningsAsErrors=true` is repo-wide via `Directory.Build.props`, so PublicAPI mismatches fail the build.

**Step 5: Commit**

```bash
git add src/ZeroAlloc.StateMachine/CompositeStateAttribute.cs \
        src/ZeroAlloc.StateMachine/HistoryStateAttribute.cs \
        src/ZeroAlloc.StateMachine/PublicAPI.Unshipped.txt
git commit -m "feat: add [CompositeState] and [HistoryState] attributes (B1+B2 runtime types)

Two new public attribute types that the generator (next commits) will pick
up to wire hierarchical state machines:

  - CompositeStateAttribute<TState>(State, SubMachine) — declares that the
    given state delegates dispatch to a sub-FSM.
  - HistoryStateAttribute<TState>(State) — declares shallow history for a
    composite, restoring the sub-FSM's leaf state on re-entry.

Generator wiring lands in subsequent commits. PublicAPI changes are
strictly additive."
```

---

## Task 2: Add diagnostic descriptors `ZSM0005`–`ZSM0011`

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineDiagnostics.cs`

**Step 1: Append the new descriptors**

Add to the end of the `StateMachineDiagnostics` class (after `StructConcurrentNotSupported`):

```csharp
    public static readonly DiagnosticDescriptor CompositeStateOnConcurrentMachine = new(
        id:                 "ZSM0005",
        title:              "Composite state on a concurrent machine",
        messageFormat:      "[CompositeState] on '{0}' is not supported in concurrent mode. Remove Concurrent = true or flatten the hierarchy.",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "Composite states require sequential dispatch; concurrent mode's CAS-based transitions cannot be made atomic across nested machines.");

    public static readonly DiagnosticDescriptor SubMachineIsNotStateMachine = new(
        id:                 "ZSM0006",
        title:              "Sub-machine is not a [StateMachine]",
        messageFormat:      "[CompositeState(State = {0}.{1})] on '{2}': SubMachine type '{3}' is not a [StateMachine] partial class.",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "The SubMachine must be a partial class annotated with [StateMachine].");

    public static readonly DiagnosticDescriptor SubMachineTriggerMismatch = new(
        id:                 "ZSM0007",
        title:              "Sub-machine trigger type mismatch",
        messageFormat:      "[CompositeState(State = {0}.{1})] on '{2}': SubMachine '{3}' declares trigger type '{4}' but parent uses '{5}'. Hierarchies must share a single TTrigger.",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "Sub-machines share the parent's TTrigger so that TryFire can dispatch identical trigger values across levels.");

    public static readonly DiagnosticDescriptor CompositeStateInvalidStateValue = new(
        id:                 "ZSM0008",
        title:              "Composite state value not declared in TState",
        messageFormat:      "[CompositeState(State = {0})] on '{1}': '{0}' is not a member of the parent's state enum '{2}'.",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "The State value must be a declared member of the same TState enum used by the parent's transitions.");

    public static readonly DiagnosticDescriptor DuplicateCompositeState = new(
        id:                 "ZSM0009",
        title:              "Duplicate composite state",
        messageFormat:      "[CompositeState] on '{0}': state '{1}.{2}' is declared as composite more than once. Each parent state can have at most one sub-machine.",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "Multiple [CompositeState] attributes targeting the same State value are ambiguous.");

    public static readonly DiagnosticDescriptor HistoryWithoutComposite = new(
        id:                 "ZSM0010",
        title:              "History state without composite",
        messageFormat:      "[HistoryState(State = {0}.{1})] on '{2}': no matching [CompositeState(State = {0}.{1})] declared. History only makes sense for composite states.",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "Add [CompositeState(State = X, SubMachine = ...)] or remove the [HistoryState(State = X)] declaration.");

    public static readonly DiagnosticDescriptor CompositeAndTerminalOnSameState = new(
        id:                 "ZSM0011",
        title:              "Composite state cannot also be terminal",
        messageFormat:      "State '{0}.{1}' on '{2}' is declared both [CompositeState] and [Terminal]. A composite state has internal dynamics and cannot be terminal.",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "Remove either the [CompositeState] or the [Terminal] declaration for this state.");
```

**Step 2: Verify the generator project still builds**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
```

Expected: 0 errors. (The descriptors are not used yet; build only verifies syntax.)

**Step 3: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineDiagnostics.cs
git commit -m "feat(generator): add diagnostic descriptors ZSM0005-ZSM0011 for composite states

Seven new error-severity diagnostics covering declaration mistakes for
[CompositeState] / [HistoryState]:

  ZSM0005: composite on concurrent machine
  ZSM0006: SubMachine type isn't [StateMachine]
  ZSM0007: TTrigger mismatch between parent and sub
  ZSM0008: composite state value not in parent's TState
  ZSM0009: duplicate composite declaration for the same state
  ZSM0010: [HistoryState] without matching [CompositeState]
  ZSM0011: composite state declared [Terminal] (contradictory)

Descriptors only — wiring (detection + report) lands in subsequent commits."
```

---

## Task 3: Extend `StateMachineModel` with composite + history metadata

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineModel.cs`
- Create: `src/ZeroAlloc.StateMachine.Generator/CompositeStateModel.cs`
- Create: `src/ZeroAlloc.StateMachine.Generator/HistoryStateModel.cs`

**Step 1: Add `CompositeStateModel.cs`**

```csharp
namespace ZeroAlloc.StateMachine.Generator;

/// <summary>Single [CompositeState] declaration captured during parsing.</summary>
/// <param name="State">Short enum member name (e.g. "Loading").</param>
/// <param name="SubMachineFqn">Fully-qualified type name of the sub-machine (e.g. "global::MyApp.LoadingFsm").</param>
/// <param name="SubMachineShort">Short type name (e.g. "LoadingFsm").</param>
/// <param name="SubMachineStateTypeFqn">Fully-qualified TState of the sub-machine (e.g. "global::MyApp.LoadingState").</param>
internal sealed record CompositeStateModel(
    string State,
    string SubMachineFqn,
    string SubMachineShort,
    string SubMachineStateTypeFqn);
```

**Step 2: Add `HistoryStateModel.cs`**

```csharp
namespace ZeroAlloc.StateMachine.Generator;

/// <summary>Single [HistoryState] declaration captured during parsing.</summary>
/// <param name="State">Short enum member name of the composite that gets shallow history.</param>
internal sealed record HistoryStateModel(string State);
```

**Step 3: Extend `StateMachineModel.cs`**

Append two new positional params (preserve existing ordering for backward compatibility within the generator):

```csharp
namespace ZeroAlloc.StateMachine.Generator;

using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

internal sealed record StateMachineModel(
    string? Namespace,
    string ClassName,
    bool IsStruct,
    string InitialState,
    bool Concurrent,
    string StateTypeFqn,
    string StateTypeShort,
    string TriggerTypeFqn,
    string TriggerTypeShort,
    ImmutableArray<TransitionModel> Transitions,
    ImmutableArray<string> TerminalStates,
    ImmutableArray<CompositeStateModel> CompositeStates,     // NEW
    ImmutableArray<HistoryStateModel> HistoryStates,         // NEW
    ImmutableArray<Diagnostic> Diagnostics
);
```

**Step 4: Update the generator's `Parse` callsite to pass empty arrays for now**

In `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs`, find the `new StateMachineModel(...)` call at line ~73 and add the two empty arrays before `diagnostics.ToImmutable()`:

```csharp
return new StateMachineModel(
    ns, type.Name, isStruct,
    initialState, concurrent,
    stateTypeFqn, stateTypeShort!,
    triggerTypeFqn, triggerTypeShort!,
    transitions, terminalStates,
    ImmutableArray<CompositeStateModel>.Empty,    // NEW — filled by Task 4
    ImmutableArray<HistoryStateModel>.Empty,      // NEW — filled by Task 4
    diagnostics.ToImmutable());
```

**Step 5: Verify build + existing tests still pass**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ZeroAlloc.StateMachine.Generator.Tests.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ZeroAlloc.StateMachine.Tests.csproj -c Release
```

Expected: 0 errors, all existing tests still pass. No new functionality — just model + plumbing.

**Step 6: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineModel.cs \
        src/ZeroAlloc.StateMachine.Generator/CompositeStateModel.cs \
        src/ZeroAlloc.StateMachine.Generator/HistoryStateModel.cs \
        src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs
git commit -m "feat(generator): extend model with composite + history metadata

Adds CompositeStateModel + HistoryStateModel records and threads
ImmutableArray<...> fields onto StateMachineModel. Parse currently
passes Empty arrays; attribute collection lands in the next commit."
```

---

## Task 4: Collect `[CompositeState]` + `[HistoryState]` attributes during parse

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs`

**Step 1: Add new metadata-name constants**

Near the top of the class, after `TerminalAttributeMetadataName`:

```csharp
private const string CompositeStateAttributeMetadataName = "CompositeStateAttribute`1";
private const string HistoryStateAttributeMetadataName   = "HistoryStateAttribute`1";
private const string StateMachineAttributeMetadataName   = "StateMachineAttribute";
```

**Step 2: Extend `CollectAttributes` return shape**

Change the return tuple from 6-tuple to 8-tuple. New signature:

```csharp
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
```

Inside, add two new builders and walk the `[CompositeState<>]` / `[HistoryState<>]` attributes:

```csharp
var compositeStates = ImmutableArray.CreateBuilder<CompositeStateModel>();
var historyStates   = ImmutableArray.CreateBuilder<HistoryStateModel>();

// ... within the existing foreach loop on attr.GetAttributes() ...

else if (string.Equals(metadataName, CompositeStateAttributeMetadataName, StringComparison.Ordinal) &&
         attrClass.TypeArguments.Length == 1)
{
    var stateName = GetEnumMemberName(attr, "State", attrClass.TypeArguments[0]);
    var subMachineSymbol = attr.NamedArguments
        .FirstOrDefault(kv => string.Equals(kv.Key, "SubMachine", StringComparison.Ordinal))
        .Value.Value as INamedTypeSymbol;

    if (stateName is not null && subMachineSymbol is not null)
    {
        // Sub-machine's TState comes from ITS [StateMachine]'s [Transition<TState, TTrigger>] attributes.
        var subStateTypeFqn = ResolveSubMachineStateTypeFqn(subMachineSymbol);
        compositeStates.Add(new CompositeStateModel(
            State: stateName,
            SubMachineFqn: subMachineSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            SubMachineShort: subMachineSymbol.Name,
            SubMachineStateTypeFqn: subStateTypeFqn ?? "global::object" /* placeholder; diagnostic in Task 5 if null */));
    }
}
else if (string.Equals(metadataName, HistoryStateAttributeMetadataName, StringComparison.Ordinal) &&
         attrClass.TypeArguments.Length == 1)
{
    var stateName = GetEnumMemberName(attr, "State", attrClass.TypeArguments[0]);
    if (stateName is not null)
        historyStates.Add(new HistoryStateModel(stateName));
}
```

**Step 3: Add `ResolveSubMachineStateTypeFqn` helper**

```csharp
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
```

(There's a similar helper needed for resolving the sub-machine's TTrigger for diagnostic `ZSM0007` — see Task 5.)

**Step 4: Update the return statement**

```csharp
return (transitions.ToImmutable(), terminalStates.ToImmutable(),
        compositeStates.ToImmutable(), historyStates.ToImmutable(),
        stateTypeFqn, stateTypeShort, triggerTypeFqn, triggerTypeShort);
```

**Step 5: Update the caller in `Parse` to pass the new arrays into the model**

```csharp
var (transitions, terminalStates, compositeStates, historyStates,
     stateTypeFqn, stateTypeShort, triggerTypeFqn, triggerTypeShort)
    = CollectAttributes(type);

// ...

return new StateMachineModel(
    ns, type.Name, isStruct,
    initialState, concurrent,
    stateTypeFqn, stateTypeShort!,
    triggerTypeFqn, triggerTypeShort!,
    transitions, terminalStates,
    compositeStates, historyStates,    // CHANGED — was ImmutableArray.Empty
    diagnostics.ToImmutable());
```

**Step 6: Verify build + existing tests still pass**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release
```

Expected: 0 errors, all existing tests still pass (no behavior change yet — generator still ignores `compositeStates`/`historyStates`).

**Step 7: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs
git commit -m "feat(generator): parse [CompositeState] + [HistoryState] attributes

Walks GetAttributes() (same pattern as [Transition] / [Terminal]) to
collect composite + history declarations into StateMachineModel.

Resolution of the sub-machine's TState FQN walks the sub-machine's
[Transition<TState, TTrigger>] attributes to find its declared TState
type — generator-time only, no reflection at runtime.

Emit/diagnostic wiring lands in the next two commits."
```

---

## Task 5: Add diagnostic detection (ZSM0005-ZSM0011)

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs`

**Step 1: Extend `AnalyzeDiagnostics` signature + body**

Pass `compositeStates`, `historyStates`, the type symbol, and the trigger type info to a new analyzer:

```csharp
AnalyzeDiagnostics(initialState, transitions, terminalStates,
    compositeStates, historyStates,                                 // NEW
    stateTypeShort!, triggerTypeFqn!, triggerTypeShort!,            // NEW for trigger-mismatch reporting
    type, isStruct, concurrent, diagnostics);
```

**Step 2: Add new analyzer method `AnalyzeCompositeStates`**

Append to `StateMachineGenerator.cs`:

```csharp
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

    // ZSM0005: composite + concurrent
    if (concurrent && !compositeStates.IsEmpty)
    {
        diagnostics.Add(Diagnostic.Create(
            StateMachineDiagnostics.CompositeStateOnConcurrentMachine, location,
            type.Name));
        return; // skip remaining composite analysis — model is invalid
    }

    // ZSM0009: duplicate composite declarations
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

    // ZSM0008: composite state value not in TState
    // Resolve the parent's TState enum symbol and check each composite's State is a member.
    INamedTypeSymbol? stateEnum = null;
    foreach (var attr in type.GetAttributes())
    {
        var ac = attr.AttributeClass;
        if (ac is not null &&
            string.Equals(ac.MetadataName, "TransitionAttribute`2", StringComparison.Ordinal) &&
            ac.TypeArguments.Length == 2 &&
            ac.TypeArguments[0] is INamedTypeSymbol stEnum)
        {
            stateEnum = stEnum;
            break;
        }
    }
    if (stateEnum is not null)
    {
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

    // ZSM0006 + ZSM0007: sub-machine validity (not [StateMachine], trigger mismatch)
    foreach (var cs in compositeStates)
    {
        // Re-resolve the sub-machine type from the attribute to inspect its declarations.
        var subTypeSymbol = ResolveSubMachineSymbol(type, cs.State);
        if (subTypeSymbol is null) continue; // can't re-resolve — model already partially broken

        var hasStateMachineAttr = subTypeSymbol.GetAttributes().Any(a =>
            string.Equals(a.AttributeClass?.MetadataName, StateMachineAttributeMetadataName, StringComparison.Ordinal));
        if (!hasStateMachineAttr)
        {
            diagnostics.Add(Diagnostic.Create(
                StateMachineDiagnostics.SubMachineIsNotStateMachine, location,
                stateTypeShort, cs.State, type.Name, subTypeSymbol.Name));
            continue;
        }

        // Check trigger type matches parent's.
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

    // ZSM0010: [HistoryState] without [CompositeState]
    foreach (var hs in historyStates)
    {
        if (!seenStates.Contains(hs.State))
        {
            diagnostics.Add(Diagnostic.Create(
                StateMachineDiagnostics.HistoryWithoutComposite, location,
                stateTypeShort, hs.State, type.Name));
        }
    }

    // ZSM0011: composite + [Terminal] on same state
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
```

**Step 3: Call `AnalyzeCompositeStates` from `AnalyzeDiagnostics`**

```csharp
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
}
```

**Step 4: Verify build**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
```

Expected: 0 errors. Existing tests still pass (no composite-state tests exist yet).

**Step 5: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs
git commit -m "feat(generator): emit diagnostics ZSM0005-ZSM0011 for composite-state errors

Wires the seven new descriptors:
  - ZSM0005 short-circuits remaining composite analysis if concurrent.
  - ZSM0006-ZSM0007 walk the sub-machine type to verify it's [StateMachine]
    and shares TTrigger.
  - ZSM0008 validates State against the parent's TState enum members.
  - ZSM0009 catches duplicate State values across multiple [CompositeState].
  - ZSM0010 flags orphan [HistoryState] declarations.
  - ZSM0011 catches the composite-AND-terminal contradiction."
```

---

## Task 6: Emit `Reset()` + `ResetTo(TState)` on every `[StateMachine]` class

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineWriter.cs`

**Step 1: Add a helper that emits both methods**

Append to `StateMachineWriter.cs` (private static):

```csharp
private static void WriteResetMechanics(StringBuilder sb, StateMachineModel m)
{
    var st = m.StateTypeFqn;

    sb.AppendLine($"    /// <summary>Resets the machine to its declared initial state. Does NOT fire OnExit/OnEnter — state-population only.</summary>");
    sb.AppendLine($"    internal void Reset()");
    sb.AppendLine($"    {{");
    sb.AppendLine($"        _state = {st}.{m.InitialState};");

    // If this class itself has composites, reset each sub-FSM to its initial.
    foreach (var c in m.CompositeStates)
    {
        sb.AppendLine($"        _subFsm_{c.State}.Reset();");
    }

    sb.AppendLine($"    }}");
    sb.AppendLine();

    sb.AppendLine($"    /// <summary>Sets the machine to <paramref name=\"state\"/>. Does NOT fire OnExit/OnEnter — state-population only.</summary>");
    sb.AppendLine($"    /// <remarks>If <paramref name=\"state\"/> is itself a composite, the sub-FSM is reset to its initial state (shallow history contract).</remarks>");
    sb.AppendLine($"    internal void ResetTo({st} state)");
    sb.AppendLine($"    {{");
    sb.AppendLine($"        _state = state;");

    if (m.CompositeStates.Length > 0)
    {
        sb.AppendLine($"        switch (state)");
        sb.AppendLine($"        {{");
        foreach (var c in m.CompositeStates)
        {
            sb.AppendLine($"            case {st}.{c.State}: _subFsm_{c.State}.Reset(); break;");
        }
        sb.AppendLine($"            default: break;");
        sb.AppendLine($"        }}");
    }

    sb.AppendLine($"    }}");
}
```

**Step 2: Call `WriteResetMechanics` from both `WriteNonConcurrentBody` and `WriteConcurrentBody`**

In `WriteNonConcurrentBody`, after the `Fire` helper and before the partial dispatchers (around the existing `WriteOnExitDispatcher` call):

```csharp
WriteResetMechanics(sb, m);
sb.AppendLine();
```

In `WriteConcurrentBody`, append the same call at the appropriate position (after the CAS-based `TryFire` body). Concurrent machines also get `Reset` / `ResetTo` (uniform) but composite-fields branches don't fire for them (`CompositeStates` will always be empty in concurrent mode due to `ZSM0005`).

**Step 3: Run existing snapshot tests — they'll fail because the snapshot includes new methods**

```bash
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
```

Expected: snapshot mismatch failures (new `Reset`/`ResetTo` methods now appear in generated output). The `.received.cs` files in `Snapshots/` will show the new output.

**Step 4: Accept the snapshot deltas**

For each test that emits both methods (basic machine, guard machine, etc.), inspect the `.received.cs` and rename it to `.verified.cs` (or use VerifyXunit's tooling). If the new emit looks correct, accept all snapshot updates.

```bash
# In each Snapshots/ directory:
for f in tests/ZeroAlloc.StateMachine.Generator.Tests/Snapshots/*.received.cs; do
    mv "$f" "${f%.received.cs}.verified.cs"
done
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
```

Expected: all snapshot tests pass.

**Step 5: Verify runtime tests still pass**

```bash
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release
```

Expected: all existing runtime tests still pass. The `internal` `Reset`/`ResetTo` methods don't affect any current consumer.

**Step 6: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineWriter.cs \
        tests/ZeroAlloc.StateMachine.Generator.Tests/Snapshots/
git commit -m "feat(generator): emit Reset() + ResetTo(TState) on every [StateMachine] class

Two new internal methods on every generated FSM. Reset() sets _state to
InitialState. ResetTo(state) sets _state to the given value, then — if
the value is itself a composite — calls Reset() on its sub-FSM
(enforces the shallow-history contract: each level remembers its direct
sub-state only).

Neither fires OnExit/OnEnter — they're state-population mechanics, not
transitions. Internal scope keeps them out of every consumer's public
API surface. Existing snapshot tests updated to reflect the new emit
shape; runtime tests unchanged."
```

---

## Task 7: Emit composite-state machinery on parent classes

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineWriter.cs`

**Step 1: Add sub-FSM field + history-field emission**

Insert after the `_state` field block in `WriteNonConcurrentBody`:

```csharp
// One sub-FSM instance per [CompositeState].
foreach (var c in m.CompositeStates)
{
    sb.AppendLine($"    private readonly {c.SubMachineFqn} _subFsm_{c.State} = new();");
}

// One history pair per [HistoryState] (only emitted when matching [CompositeState] exists).
var historySet = new System.Collections.Generic.HashSet<string>(
    m.HistoryStates.Select(h => h.State), StringComparer.Ordinal);
foreach (var c in m.CompositeStates)
{
    if (historySet.Contains(c.State))
    {
        sb.AppendLine($"    private {c.SubMachineStateTypeFqn} _history_{c.State};");
        sb.AppendLine($"    private bool _hasHistory_{c.State};");
    }
}

if (m.CompositeStates.Length > 0 || m.HistoryStates.Length > 0)
    sb.AppendLine();
```

(Requires `using System.Linq;` and `using System.Collections.Generic;` at the top of the file — verify they're already present.)

**Step 2: Add `TryFireSubMachine` emission**

After `WriteCurrent` (before `TryFire`), conditionally emit if any composite exists:

```csharp
if (m.CompositeStates.Length > 0)
{
    sb.AppendLine($"    private bool TryFireSubMachine({m.TriggerTypeFqn} trigger) => _state switch");
    sb.AppendLine($"    {{");
    foreach (var c in m.CompositeStates)
    {
        sb.AppendLine($"        {m.StateTypeFqn}.{c.State} => _subFsm_{c.State}.TryFire(trigger),");
    }
    sb.AppendLine($"        _ => false");
    sb.AppendLine($"    }};");
    sb.AppendLine();
}
```

**Step 3: Make `TryFire` two-step when composites exist**

Modify the `TryFire` emission to check the sub first:

```csharp
sb.AppendLine($"    public bool TryFire({tr} trigger)");
sb.AppendLine($"    {{");
if (m.CompositeStates.Length > 0)
{
    sb.AppendLine($"        if (TryFireSubMachine(trigger)) return true;");
    sb.AppendLine();
}
sb.AppendLine($"        return (Current, trigger) switch");
sb.AppendLine($"        {{");
foreach (var t in m.Transitions)
{
    // ... existing transition emission ...
}
sb.AppendLine($"            _ => false");
sb.AppendLine($"        }};");
sb.AppendLine($"    }}");
```

**Step 4: Extend `Fire` to capture history + reset/restore sub-FSM**

Modify the `Fire` helper:

```csharp
sb.AppendLine($"    private bool Fire({st} from, {st} to, {tr} trigger)");
sb.AppendLine($"    {{");
sb.AppendLine($"        OnExit(from, trigger);");

// Capture history on exit of a composite that has [HistoryState].
foreach (var c in m.CompositeStates.Where(c => historySet.Contains(c.State)))
{
    sb.AppendLine($"        if (from == {st}.{c.State})");
    sb.AppendLine($"        {{");
    sb.AppendLine($"            _history_{c.State} = _subFsm_{c.State}.Current;");
    sb.AppendLine($"            _hasHistory_{c.State} = true;");
    sb.AppendLine($"        }}");
}

sb.AppendLine($"        _state = to;");

// Position sub-FSM on entering a composite.
foreach (var c in m.CompositeStates)
{
    sb.AppendLine($"        if (to == {st}.{c.State})");
    sb.AppendLine($"        {{");
    if (historySet.Contains(c.State))
    {
        sb.AppendLine($"            if (_hasHistory_{c.State}) _subFsm_{c.State}.ResetTo(_history_{c.State});");
        sb.AppendLine($"            else                       _subFsm_{c.State}.Reset();");
    }
    else
    {
        sb.AppendLine($"            _subFsm_{c.State}.Reset();");
    }
    sb.AppendLine($"        }}");
}

sb.AppendLine($"        OnEnter(to, from);");
sb.AppendLine($"        return true;");
sb.AppendLine($"    }}");
```

**Step 5: Verify build (no tests written for composites yet)**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
```

Expected: 0 errors. Existing snapshot tests still pass (no composite in those test sources, so the new emit branches don't fire and the existing output is unchanged).

**Step 6: Run existing tests**

```bash
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release
```

Expected: all pass.

**Step 7: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineWriter.cs
git commit -m "feat(generator): emit composite-state machinery on parents

For [StateMachine] classes that declare one or more [CompositeState]:

  - One readonly sub-FSM field per composite (eager construction in
    parent ctor — zero per-transition allocation).
  - One history-pair (last-state + has-history) per [HistoryState].
  - TryFire becomes two-step: TryFireSubMachine first, then parent's
    own switch. When parent isn't in any composite, the sub switch
    falls through with one enum compare.
  - Fire extended to capture history on exit and reset/restore the
    sub-FSM on enter.

Existing flat-machine emit unchanged (composite branches only fire
when CompositeStates is non-empty). Tests for the new behavior land
in the next commits."
```

---

## Task 8: Add snapshot tests for the four key composite shapes

**Files:**
- Create: `tests/ZeroAlloc.StateMachine.Generator.Tests/CompositeStateGeneratorTests.cs`

**Step 1: Write the test file**

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.StateMachine.Generator.Tests;

public class CompositeStateGeneratorTests
{
    [Fact]
    public Task Basic_OneComposite_NoHistory()
    {
        var source = """
            using ZeroAlloc.StateMachine;

            namespace MyApp;

            public enum ParentState { Idle, Loading, Done }
            public enum LoadingState { Fetching, Parsing }
            public enum Trigger      { Start, DataReceived, Cancel, Complete }

            [StateMachine(InitialState = nameof(LoadingState.Fetching))]
            [Transition<LoadingState, Trigger>(From = LoadingState.Fetching, On = Trigger.DataReceived, To = LoadingState.Parsing)]
            public partial class LoadingFsm { }

            [StateMachine(InitialState = nameof(ParentState.Idle))]
            [Transition<ParentState, Trigger>(From = ParentState.Idle,    On = Trigger.Start,    To = ParentState.Loading)]
            [Transition<ParentState, Trigger>(From = ParentState.Loading, On = Trigger.Cancel,   To = ParentState.Idle)]
            [Transition<ParentState, Trigger>(From = ParentState.Loading, On = Trigger.Complete, To = ParentState.Done)]
            [CompositeState<ParentState>(State = ParentState.Loading, SubMachine = typeof(LoadingFsm))]
            [Terminal<ParentState>(State = ParentState.Done)]
            public partial class ParentMachine { }
            """;

        return TestHelper.Verify<StateMachineGenerator>(source);
    }

    [Fact]
    public Task WithHistory_RestoresLeaf()
    {
        var source = """
            using ZeroAlloc.StateMachine;

            namespace MyApp;

            public enum ParentState { Idle, Loading }
            public enum LoadingState { Fetching, Parsing }
            public enum Trigger      { Start, DataReceived, Suspend, Resume }

            [StateMachine(InitialState = nameof(LoadingState.Fetching))]
            [Transition<LoadingState, Trigger>(From = LoadingState.Fetching, On = Trigger.DataReceived, To = LoadingState.Parsing)]
            public partial class LoadingFsm { }

            [StateMachine(InitialState = nameof(ParentState.Idle))]
            [Transition<ParentState, Trigger>(From = ParentState.Idle,    On = Trigger.Start,   To = ParentState.Loading)]
            [Transition<ParentState, Trigger>(From = ParentState.Loading, On = Trigger.Suspend, To = ParentState.Idle)]
            [Transition<ParentState, Trigger>(From = ParentState.Idle,    On = Trigger.Resume,  To = ParentState.Loading)]
            [CompositeState<ParentState>(State = ParentState.Loading, SubMachine = typeof(LoadingFsm))]
            [HistoryState<ParentState>(State = ParentState.Loading)]
            public partial class ParentMachine { }
            """;

        return TestHelper.Verify<StateMachineGenerator>(source);
    }

    [Fact]
    public Task Nested_TwoLevelsDeep()
    {
        var source = """
            using ZeroAlloc.StateMachine;

            namespace MyApp;

            public enum L1 { A, B }
            public enum L2 { X, Y }
            public enum L3 { P, Q }
            public enum Trigger { Tick, Reset }

            [StateMachine(InitialState = nameof(L3.P))]
            [Transition<L3, Trigger>(From = L3.P, On = Trigger.Tick, To = L3.Q)]
            public partial class LeafFsm { }

            [StateMachine(InitialState = nameof(L2.X))]
            [Transition<L2, Trigger>(From = L2.X, On = Trigger.Tick, To = L2.Y)]
            [CompositeState<L2>(State = L2.X, SubMachine = typeof(LeafFsm))]
            public partial class MidFsm { }

            [StateMachine(InitialState = nameof(L1.A))]
            [Transition<L1, Trigger>(From = L1.A, On = Trigger.Reset, To = L1.B)]
            [CompositeState<L1>(State = L1.A, SubMachine = typeof(MidFsm))]
            public partial class TopFsm { }
            """;

        return TestHelper.Verify<StateMachineGenerator>(source);
    }

    [Fact]
    public Task MultipleComposites_OneParent()
    {
        var source = """
            using ZeroAlloc.StateMachine;

            namespace MyApp;

            public enum Outer { Idle, Connecting, Authenticated }
            public enum Conn  { Resolving, Handshaking }
            public enum Auth  { ValidatingToken, Renewing }
            public enum T     { Begin, Done, Restart }

            [StateMachine(InitialState = nameof(Conn.Resolving))]
            [Transition<Conn, T>(From = Conn.Resolving, On = T.Done, To = Conn.Handshaking)]
            public partial class ConnFsm { }

            [StateMachine(InitialState = nameof(Auth.ValidatingToken))]
            [Transition<Auth, T>(From = Auth.ValidatingToken, On = T.Done, To = Auth.Renewing)]
            public partial class AuthFsm { }

            [StateMachine(InitialState = nameof(Outer.Idle))]
            [Transition<Outer, T>(From = Outer.Idle,         On = T.Begin, To = Outer.Connecting)]
            [Transition<Outer, T>(From = Outer.Connecting,   On = T.Done,  To = Outer.Authenticated)]
            [Transition<Outer, T>(From = Outer.Authenticated, On = T.Restart, To = Outer.Idle)]
            [CompositeState<Outer>(State = Outer.Connecting,    SubMachine = typeof(ConnFsm))]
            [CompositeState<Outer>(State = Outer.Authenticated, SubMachine = typeof(AuthFsm))]
            public partial class Machine { }
            """;

        return TestHelper.Verify<StateMachineGenerator>(source);
    }
}
```

**Step 2: Run the new tests**

```bash
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release \
  --filter "FullyQualifiedName~CompositeStateGeneratorTests"
```

Expected: 4 test failures (VerifyXunit produces `.received.cs` files for each test on first run).

**Step 3: Inspect each `.received.cs` for correctness**

For each test in `tests/ZeroAlloc.StateMachine.Generator.Tests/Snapshots/CompositeStateGeneratorTests.*.received.cs`, manually verify:

- Sub-FSM field declared with `readonly = new()`
- `Reset()` and `ResetTo` emitted on every generated class
- `TryFireSubMachine` emitted on parent
- Two-step `TryFire` in parent
- `Fire` captures history on exit (where applicable)
- `Fire` resets/restores sub on enter
- No regressions in flat-machine emit

If correct, accept by renaming `.received.cs` → `.verified.cs`:

```bash
for f in tests/ZeroAlloc.StateMachine.Generator.Tests/Snapshots/CompositeStateGeneratorTests.*.received.cs; do
    mv "$f" "${f%.received.cs}.verified.cs"
done
```

**Step 4: Re-run; verify pass**

```bash
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
```

Expected: all tests (existing + 4 new) pass.

**Step 5: Commit**

```bash
git add tests/ZeroAlloc.StateMachine.Generator.Tests/CompositeStateGeneratorTests.cs \
        tests/ZeroAlloc.StateMachine.Generator.Tests/Snapshots/
git commit -m "test(generator): snapshot tests for composite + history emit shapes

Four scenarios:
  - Basic_OneComposite_NoHistory
  - WithHistory_RestoresLeaf
  - Nested_TwoLevelsDeep
  - MultipleComposites_OneParent

Snapshots verified manually then committed via VerifyXunit's
.received.cs → .verified.cs flow."
```

---

## Task 9: Add diagnostic tests for ZSM0005-ZSM0011

**Files:**
- Modify: `tests/ZeroAlloc.StateMachine.Generator.Tests/DiagnosticTests.cs`

**Step 1: Append seven tests, one per diagnostic ID**

```csharp
[Fact]
public async Task ZSM0005_CompositeOnConcurrent_EmitsError()
{
    var source = """
        using ZeroAlloc.StateMachine;
        namespace MyApp;

        public enum SubState { A }
        public enum State { X, Y }
        public enum Trigger { Go }

        [StateMachine(InitialState = nameof(SubState.A))]
        [Transition<SubState, Trigger>(From = SubState.A, On = Trigger.Go, To = SubState.A)]
        public partial class SubFsm { }

        [StateMachine(InitialState = nameof(State.X), Concurrent = true)]
        [Transition<State, Trigger>(From = State.X, On = Trigger.Go, To = State.Y)]
        [CompositeState<State>(State = State.X, SubMachine = typeof(SubFsm))]
        public partial class Parent { }
        """;

    var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
    Assert.Contains(diags, d => d.Id == "ZSM0005");
}

// Repeat for ZSM0006-ZSM0011 with deliberate-broken sources.
```

Provide one test per diagnostic following the same pattern. Each test:
- Constructs a source string with the minimal broken declaration that triggers exactly one new diagnostic.
- Calls `TestHelper.GetDiagnostics<StateMachineGenerator>(source)`.
- Asserts `Contains(diags, d => d.Id == "ZSMnnnn")`.

**Step 2: Run the diagnostic tests**

```bash
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release \
  --filter "FullyQualifiedName~ZSM00"
```

Expected: 7 new tests pass.

**Step 3: Commit**

```bash
git add tests/ZeroAlloc.StateMachine.Generator.Tests/DiagnosticTests.cs
git commit -m "test(generator): diagnostic tests for ZSM0005-ZSM0011

One [Fact] per new diagnostic with a minimal broken source that
triggers exactly the expected ID. Verifies the generator's diagnostic
emission catches each declaration error."
```

---

## Task 10: Add runtime behavior tests

**Files:**
- Create: `tests/ZeroAlloc.StateMachine.Tests/CompositeStateTests.cs`

**Step 1: Define the test fixture FSMs**

Above the test class, define a realistic hierarchy used by the tests:

```csharp
namespace ZeroAlloc.StateMachine.Tests;

using ZeroAlloc.StateMachine;

public enum LoadingState { Fetching, Parsing }
public enum Outer        { Idle, Loading, Done }
public enum HierTrigger  { Start, DataReceived, Suspend, Resume, Complete }

[StateMachine(InitialState = nameof(LoadingState.Fetching))]
[Transition<LoadingState, HierTrigger>(From = LoadingState.Fetching, On = HierTrigger.DataReceived, To = LoadingState.Parsing)]
public partial class TestLoadingFsm { }

[StateMachine(InitialState = nameof(Outer.Idle))]
[Transition<Outer, HierTrigger>(From = Outer.Idle,    On = HierTrigger.Start,    To = Outer.Loading)]
[Transition<Outer, HierTrigger>(From = Outer.Loading, On = HierTrigger.Suspend,  To = Outer.Idle)]
[Transition<Outer, HierTrigger>(From = Outer.Idle,    On = HierTrigger.Resume,   To = Outer.Loading)]
[Transition<Outer, HierTrigger>(From = Outer.Loading, On = HierTrigger.Complete, To = Outer.Done)]
[CompositeState<Outer>(State = Outer.Loading, SubMachine = typeof(TestLoadingFsm))]
[HistoryState<Outer>(State = Outer.Loading)]
public partial class TestHierMachine { }
```

(Adjust enum names if they collide with existing types in `RuntimeTests.cs`.)

**Step 2: Write nine tests covering the scenarios from the design**

```csharp
public class CompositeStateTests
{
    [Fact]
    public void Sub_handles_trigger_when_parent_in_composite()
    {
        var m = new TestHierMachine();
        Assert.True(m.TryFire(HierTrigger.Start));            // Outer: Idle -> Loading
        Assert.True(m.TryFire(HierTrigger.DataReceived));     // Sub: Fetching -> Parsing
        Assert.Equal(Outer.Loading, m.Current);               // Parent unchanged
    }

    [Fact]
    public void Sub_rejects_parent_falls_through()
    {
        var m = new TestHierMachine();
        m.TryFire(HierTrigger.Start);                          // Loading
        Assert.True(m.TryFire(HierTrigger.Complete));          // Sub has no transition; parent fires.
        Assert.Equal(Outer.Done, m.Current);
    }

    [Fact]
    public void History_capture_and_restore()
    {
        var m = new TestHierMachine();
        m.TryFire(HierTrigger.Start);
        m.TryFire(HierTrigger.DataReceived);                   // Sub at Parsing
        m.TryFire(HierTrigger.Suspend);                        // Loading -> Idle; history captured.
        Assert.Equal(Outer.Idle, m.Current);

        m.TryFire(HierTrigger.Resume);                         // Idle -> Loading; history restored.
        // Re-entering Loading should NOT reset the sub to Fetching — it should restore Parsing.
        // (Verified indirectly: firing DataReceived again should be REJECTED because no transition
        //  exists FROM Parsing on DataReceived in TestLoadingFsm.)
        Assert.False(m.TryFire(HierTrigger.DataReceived));
        Assert.Equal(Outer.Loading, m.Current);
    }

    [Fact]
    public void First_enter_no_history_starts_at_sub_initial()
    {
        var m = new TestHierMachine();
        m.TryFire(HierTrigger.Start);                          // First Loading enter; no history.
        // Sub should be at Fetching (initial). DataReceived must succeed.
        Assert.True(m.TryFire(HierTrigger.DataReceived));
    }

    // ... etc. Five more tests for: nested dispatch, shallow history reset, Reset doesn't fire
    // OnEnter/OnExit, ResetTo doesn't fire either, and parent OnExit/OnEnter fire only on parent
    // transitions.
}
```

(Complete the remaining five tests following the same pattern; each covers one scenario from Section 3 of the design doc.)

**Step 3: Run the new tests**

```bash
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release \
  --filter "FullyQualifiedName~CompositeStateTests"
```

Expected: 9 new tests pass.

**Step 4: Run the full test suites; everything still green**

```bash
dotnet test -c Release
```

Expected: all existing tests + all new tests pass.

**Step 5: Commit**

```bash
git add tests/ZeroAlloc.StateMachine.Tests/CompositeStateTests.cs
git commit -m "test: runtime tests for composite + history scenarios

Nine [Fact]s covering the scenarios from the design document:

  - Sub handles trigger when parent in composite.
  - Sub rejects → parent falls through.
  - History capture on exit, restore on re-enter.
  - No history on first enter → sub starts at initial.
  - Nested composite three levels deep dispatches correctly.
  - Shallow history resets inner sub-FSMs to initial.
  - Reset() does not fire OnExit/OnEnter.
  - ResetTo() does not fire OnExit/OnEnter.
  - Parent OnExit/OnEnter fire only on parent-level transitions.

Test fixture defines a Loading composite with shallow history, used
across the scenarios."
```

---

## Task 11: Documentation

**Files:**
- Create: `docs/core-concepts/composite-states.md`
- Modify: `docs/attributes.md`
- Optionally: create `docs/diagnostics/ZSM0005.md` ... `ZSM0011.md` following the convention of any existing per-diagnostic docs (check `docs/diagnostics/` first).

**Step 1: Write `docs/core-concepts/composite-states.md`**

Cover:
- What composite states are (motivating example)
- Declaring `[CompositeState]` + `[HistoryState]`
- Dispatch order (sub-first; UML default)
- History semantics (shallow only)
- Nested composites
- `Reset()` / `ResetTo()` mechanics + their non-OnExit/OnEnter contract
- ZSM0005 (no concurrent + composite)

**Step 2: Extend `docs/attributes.md`** with sections for the two new attributes mirroring the format of existing entries.

**Step 3: Verify docs render (if there's a docs build)**

If the docs build through MkDocs/Docusaurus, run it. Otherwise, just read the rendered Markdown manually.

**Step 4: Commit**

```bash
git add docs/
git commit -m "docs: composite states + shallow history

New core-concepts page walking through declaration, dispatch order,
history, and Reset/ResetTo mechanics. attributes.md gains entries for
[CompositeState] and [HistoryState]."
```

---

## Task 12: Push branch + open PR

**Step 1: Sanity check commit history**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.StateMachine
git log --oneline origin/main..HEAD
```

Expected (newest first):

1. `docs: composite states + shallow history`
2. `test: runtime tests for composite + history scenarios`
3. `test(generator): diagnostic tests for ZSM0005-ZSM0011`
4. `test(generator): snapshot tests for composite + history emit shapes`
5. `feat(generator): emit composite-state machinery on parents`
6. `feat(generator): emit Reset() + ResetTo(TState) on every [StateMachine] class`
7. `feat(generator): emit diagnostics ZSM0005-ZSM0011 for composite-state errors`
8. `feat(generator): parse [CompositeState] + [HistoryState] attributes`
9. `feat(generator): extend model with composite + history metadata`
10. `feat(generator): add diagnostic descriptors ZSM0005-ZSM0011 for composite states`
11. `feat: add [CompositeState] and [HistoryState] attributes (B1+B2 runtime types)`
12. `docs(plans): design for composite states + shallow history (backlog B1+B2)`

**Step 2: Push + open PR**

```bash
git push -u origin feat/composite-states-and-history

gh pr create --title "feat: composite states + shallow history (backlog B1+B2)" --body "$(cat <<'EOF'
## Summary

Graduates \`ZeroAlloc.StateMachine\` backlog items B1 (hierarchical / nested states) and B2 (shallow history) as a single architectural addition.

- New public attributes \`[CompositeState<TState>(State, SubMachine)]\` + \`[HistoryState<TState>(State)]\`.
- New generator-emitted methods \`Reset()\` / \`ResetTo(TState)\` on every \`[StateMachine]\` class.
- Two-step \`TryFire\` (sub-FSM first, then parent's own switch) on classes with composites.
- Shallow history: each level remembers its direct sub-state only; \`ResetTo\` recursively \`Reset\`s inner sub-FSMs.
- Seven new error-severity diagnostics \`ZSM0005\`–\`ZSM0011\` covering declaration mistakes.
- Composite + concurrent is diagnosed as incompatible (\`ZSM0005\`).

## Design

\`docs/plans/2026-05-22-composite-states-history-design.md\` (committed in the first commit of this branch).

## Test plan

- [ ] CI green: build + tests + aot-smoke + api-compat
- [ ] 4 snapshot tests verify the emit shapes for basic / history / nested / multi-composite scenarios
- [ ] 7 diagnostic tests verify each new ZSM00nn fires under its specific broken-source scenario
- [ ] 9 runtime tests verify dispatch + history end-to-end
- [ ] PublicAPI is strictly additive (RS0016/RS0017 catch any regression)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

**Step 3: Watch CI**

```bash
sleep 30
gh pr checks $(gh pr view --json number --jq .number)
```

If green, the implementation is ready for the user to admin-merge.

---

## Verification checklist (before merge)

- [ ] Task 1: Two new attributes declared; PublicAPI.Unshipped.txt updated; build clean.
- [ ] Task 2: Seven new diagnostic descriptors in `StateMachineDiagnostics.cs`.
- [ ] Task 3: `StateMachineModel` extended; existing tests green.
- [ ] Task 4: `[CompositeState]` / `[HistoryState]` parsed into `compositeStates` / `historyStates`.
- [ ] Task 5: All seven diagnostics correctly fire on broken sources (proven by Task 9).
- [ ] Task 6: `Reset()` + `ResetTo(TState)` emitted on every `[StateMachine]` class; snapshot tests updated.
- [ ] Task 7: Composite machinery emitted only on classes with composites; flat machines unchanged.
- [ ] Task 8: 4 snapshot tests verified manually and committed.
- [ ] Task 9: 7 diagnostic tests pass.
- [ ] Task 10: 9 runtime tests pass.
- [ ] Task 11: Docs added.
- [ ] Task 12: PR opened with green CI.

## Out of scope (per design doc)

- Deep history (recursive restore of nested sub-FSM state).
- Composite + concurrent mode (diagnosed away via ZSM0005).
- Composite-specific entry/exit hooks (`OnEnterCompositeX` distinct from `OnEnterStateX`).
- Public state snapshotting/restoration API.
- Mermaid diagram output for hierarchical machines (that's backlog B4, separate).
