# Mermaid diagram export + initial-state arm Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Land `ZeroAlloc.StateMachine` backlog item B4 (opt-in Mermaid `stateDiagram-v2` diagram emitted as a `public const string MermaidDiagram` on every `[StateMachine(Diagram = true)]` / `[StateMachineGroup(Diagram = true)]` class) and close the v1.4 follow-up by emitting initial-state timer-arm calls in the generated constructor and from `Reset()` / `ResetTo(state)`.

**Architecture:** One new optional property (`Diagram`) on each of the two top-level attributes. A new `MermaidDiagramWriter` walks the model and emits a Mermaid `stateDiagram-v2` body (initial / transitions / terminals / composites nested / history pseudo-state / group parts as top-level `state X { … }` blocks / timed-annotated / guard-labeled). The arm-on-construct fix lands as a generator-emitted `private void ArmInitialStateTimers()` helper plus a `partial void HookConstructor()` invocation pattern: the generator emits the partial-void implementing declaration that arms timers; it also emits a default parameterless ctor calling that hook only when the user has NOT declared their own ctor. `Reset()` and `ResetTo(state)` gain a trailing `ArmInitialStateTimers()` call. Two new diagnostics — `ZSM0020` (Diagram = true on a class with zero transitions) and `ZSM0021` (user-declared ctor doesn't invoke `HookConstructor()`).

**Tech Stack:** .NET 10 / netstandard2.0 (Roslyn source generator targets), Roslyn `IIncrementalGenerator`, `Microsoft.CodeAnalysis.PublicApiAnalyzers` (RS0016/RS0017), xUnit + VerifyXunit (existing snapshot test convention via `tests/.../GeneratorSnapshotTests.cs` + `TestHelper.cs`).

**Design doc:** `docs/plans/2026-05-23-mermaid-export-and-initial-arm-design.md` (committed in `a31eb88`)

**Working branch:** `feat/mermaid-export-and-initial-arm` (already created off `main`; design doc commit `a31eb88` is the current HEAD).

**Key context:**
- v1.4 just shipped (PR #27) adding B3 (timed transitions) + B5 (concurrent state parts). The generator already has `StateMachineWriter.cs` (526 lines), `StateMachineGroupWriter.cs` (193 lines), `StateMachineGenerator.cs` (857 lines), `StateMachineDiagnostics.cs` (177 lines).
- Existing race-safe lazy-timer pattern uses `Interlocked.CompareExchange(ref _field, __new, null) ?? __new` + `if (!ReferenceEquals(__t, __new)) __new.Dispose();` followed by an unconditional `__t.Change(AfterMs, Timeout.Infinite)`. Initial-arm code reuses this same pattern.
- The `Current` property on single-machine classes exposes the current state as a read-only `TState`. On groups, each part has its own `<Name>Current` property.
- Existing diagnostics max ID is `ZSM0021` — wait, current max is `ZSM0019` (v1.4's set). New ones in this PR start at `ZSM0020`.
- `TreatWarningsAsErrors=true` repo-wide via `Directory.Build.props`. PublicAPI mismatches (RS0016/RS0017) WILL fail the build.
- Repo conventions:
  - MA0051 (Meziantou): methods may not exceed 60 lines.
  - RS1032 (CodeAnalysis): diagnostic `messageFormat` strings must be single sentences (no interior `.` unless paired with a trailing `.`).
  - HLQ012 (NetFabric.Hyperlinq): avoid named tuples in foreach over `List<...>`; prefer `record struct` carriers.
  - MA0006: prefer `string.Equals(a, b, StringComparison.Ordinal)` over `==` on strings (treated as error).
  - All four surfaced during v1.4 implementation. Methods/code should anticipate them.
- Snapshot tests use `TestHelper.Verify<StateMachineGenerator>(source)` (VerifyXunit). First run writes `.received.cs`; rename to `.verified.cs` to lock the snapshot. Hint names: `{ns}_{ClassName}.g.cs` for single-machine emit, `{ns}_{ClassName}.Group.g.cs` for groups.

---

## Task 1: Add `Diagram` property to `[StateMachine]` + `[StateMachineGroup]`

**Files:**
- Modify: `src/ZeroAlloc.StateMachine/StateMachineAttribute.cs`
- Modify: `src/ZeroAlloc.StateMachine/StateMachineGroupAttribute.cs`
- Modify: `src/ZeroAlloc.StateMachine/PublicAPI.Unshipped.txt`

**Step 1: Add `Diagram` to `StateMachineAttribute.cs`**

Append after the existing `Concurrent` property:

```csharp
    /// <summary>
    /// When <c>true</c>, the generator emits a <c>public const string MermaidDiagram</c>
    /// on the partial containing a Mermaid <c>stateDiagram-v2</c> rendering of the
    /// machine's transitions. Composite sub-FSMs render as nested <c>state X { ... }</c>
    /// blocks; timed edges annotate with <c>(after Nms)</c>; guards annotate with
    /// <c>[guard]</c>; terminal states render as <c>X --> [*]</c>.
    /// Default: <c>false</c>.
    /// </summary>
    public bool Diagram { get; init; } = false;
```

**Step 2: Add `Diagram` to `StateMachineGroupAttribute.cs`**

Replace the empty body with:

```csharp
    /// <summary>
    /// When <c>true</c>, the generator emits a <c>public const string MermaidDiagram</c>
    /// on the group partial. Each <see cref="StateMachinePartAttribute{TState, TTrigger}"/>
    /// renders as a top-level <c>state {Name} { ... }</c> block; transitions, terminals,
    /// timed edges, and guards render per the standard Mermaid rules.
    /// Default: <c>false</c>.
    /// </summary>
    public bool Diagram { get; init; } = false;
```

**Step 3: Update `PublicAPI.Unshipped.txt`**

Append (alphabetical order within the existing attribute blocks):

```
ZeroAlloc.StateMachine.StateMachineAttribute.Diagram.get -> bool
ZeroAlloc.StateMachine.StateMachineAttribute.Diagram.init -> void
ZeroAlloc.StateMachine.StateMachineGroupAttribute.Diagram.get -> bool
ZeroAlloc.StateMachine.StateMachineGroupAttribute.Diagram.init -> void
```

If `RS0016`/`RS0017` fires, accept the analyzer's suggested form verbatim.

**Step 4: Verify build**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.StateMachine
dotnet build src/ZeroAlloc.StateMachine/ZeroAlloc.StateMachine.csproj -c Release
```

Expected: 0 warnings, 0 errors.

**Step 5: Commit**

```bash
git add src/ZeroAlloc.StateMachine/StateMachineAttribute.cs \
        src/ZeroAlloc.StateMachine/StateMachineGroupAttribute.cs \
        src/ZeroAlloc.StateMachine/PublicAPI.Unshipped.txt
git commit -m "$(cat <<'EOF'
feat: add Diagram property to [StateMachine] and [StateMachineGroup] (B4 runtime surface)

Two new opt-in boolean properties on the top-level attributes. When set to
true, the generator (next commits) emits a public const string MermaidDiagram
containing the FSM's stateDiagram-v2 rendering. Default false — existing v1.4
declarations are strictly additive.

Generator wiring lands in subsequent commits.
EOF
)"
```

Use git's heredoc form. End each commit message with the standard
`Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>` trailer.

---

## Task 2: Add diagnostic descriptors `ZSM0020` + `ZSM0021`

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineDiagnostics.cs`

**Step 1: Append the new descriptors**

Add to the end of the `StateMachineDiagnostics` class (after `DisposeSignatureConflict` which is the last v1.4 entry):

```csharp
    public static readonly DiagnosticDescriptor EmptyDiagramRequest = new(
        id:                 "ZSM0020",
        title:              "[StateMachine(Diagram = true)] on a class with zero transitions",
        messageFormat:      "'{0}' declares Diagram = true but has no transitions; the emitted MermaidDiagram would be empty",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:        "Either remove Diagram = true or add at least one [Transition] (or [StateMachinePart] with its own transitions for a group).");

    public static readonly DiagnosticDescriptor MissingHookConstructorInvocation = new(
        id:                 "ZSM0021",
        title:              "User-declared constructor must call HookConstructor()",
        messageFormat:      "'{0}' has at least one timed transition AND a user-declared constructor that does not invoke HookConstructor(). Add a HookConstructor() call so the generator can arm initial-state timers",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "When timed transitions are present and the user declares their own constructor, that constructor must call the generator-emitted partial void HookConstructor() to arm initial-state timers.");
```

> **RS1032 note:** Both messageFormat strings have an interior `.` (and `;` in 0020) — the trailing period in 0021 makes RS1032 happy with multi-clause; 0020's single-clause shape with no interior period (the `;` is fine) is also accepted. If RS1032 fires anyway, append a trailing `.` to keep the closest match to the original semantic, mirroring the Task 3 deviation from the v1.4 plan.

**Step 2: Verify build**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
```

Expected: 0 errors.

**Step 3: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineDiagnostics.cs
git commit -m "$(cat <<'EOF'
feat(generator): add diagnostic descriptors ZSM0020 + ZSM0021 (B4 + initial-arm)

Two new diagnostics:

  ZSM0020 (Warning): Diagram = true declared on a class with zero
                     transitions — emitted MermaidDiagram would be empty.
  ZSM0021 (Error):   user-declared ctor on a class with timed transitions
                     must invoke HookConstructor() so initial-state timers arm.

Descriptors only — wiring (detection + report) lands in subsequent commits.
EOF
)"
```

Add the co-author trailer.

---

## Task 3: Add `Diagram` flag to `StateMachineModel` + `StateMachineGroupModel`

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineModel.cs`
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineGroupModel.cs`

**Step 1: Extend `StateMachineModel`**

Add `bool Diagram` as a positional param immediately before the trailing `Diagnostics`:

```csharp
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
    ImmutableArray<CompositeStateModel> CompositeStates,
    ImmutableArray<HistoryStateModel> HistoryStates,
    bool Diagram,                                   // NEW
    ImmutableArray<Diagnostic> Diagnostics
);
```

**Step 2: Extend `StateMachineGroupModel`**

```csharp
internal sealed record StateMachineGroupModel(
    string? Namespace,
    string ClassName,
    ImmutableArray<StateMachinePartModel> Parts,
    bool Diagram,                                   // NEW
    ImmutableArray<Diagnostic> Diagnostics
);
```

**Step 3: Update the single-machine constructor call in `StateMachineGenerator.Parse`**

In `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs`, find the `new StateMachineModel(...)` call inside `Parse` (currently the last expression of that method). Insert `false` as the new positional arg right before `diagnostics.ToImmutable()`:

```csharp
return new StateMachineModel(
    ns, type.Name, isStruct,
    initialState, concurrent,
    stateTypeFqn, stateTypeShort!,
    triggerTypeFqn, triggerTypeShort!,
    transitions, terminalStates,
    compositeStates, historyStates,
    diagram: false,    // NEW — Task 4 reads the actual value
    diagnostics.ToImmutable());
```

Use the named-argument form so a future reader can find the parse site.

**Step 4: Update the group constructor call in `StateMachineGenerator.ParseGroup`**

In the same file, find the `new StateMachineGroupModel(...)` call in `ParseGroup`:

```csharp
return new StateMachineGroupModel(
    ns, type.Name, parts,
    diagram: false,    // NEW — Task 4 reads the actual value
    diagnostics.ToImmutable());
```

**Step 5: Verify build + existing tests still pass**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ZeroAlloc.StateMachine.Generator.Tests.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ZeroAlloc.StateMachine.Tests.csproj -c Release
```

Expected: 32/32 + 26/26 tests pass. Behavior change: none yet (Diagram defaults to false).

**Step 6: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineModel.cs \
        src/ZeroAlloc.StateMachine.Generator/StateMachineGroupModel.cs \
        src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs
git commit -m "$(cat <<'EOF'
feat(generator): extend models with Diagram flag

Adds a bool Diagram positional param to StateMachineModel and
StateMachineGroupModel. Parse / ParseGroup pass false for now; the
attribute-named-arg read lands in Task 4.

Existing snapshots and runtime tests are byte-identical — Diagram = false
means no emit change.
EOF
)"
```

---

## Task 4: Read `Diagram` from the attribute's named args

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs`

**Step 1: Read `Diagram` in `Parse` (single-machine path)**

In `Parse`, find the line that reads the `Concurrent` named arg:

```csharp
var concurrent = smAttr.NamedArguments
    .FirstOrDefault(kv => string.Equals(kv.Key, "Concurrent", StringComparison.Ordinal)).Value.Value is true;
```

Immediately after it, add:

```csharp
var diagram = smAttr.NamedArguments
    .FirstOrDefault(kv => string.Equals(kv.Key, "Diagram", StringComparison.Ordinal)).Value.Value is true;
```

Then update the `new StateMachineModel(...)` call: replace `diagram: false,` with `diagram: diagram,`.

**Step 2: Read `Diagram` in `ParseGroup`**

In `ParseGroup`, near the top (right after the namespace + diagnostics-builder setup):

```csharp
var groupAttr = ctx.Attributes[0];
var diagram = groupAttr.NamedArguments
    .FirstOrDefault(kv => string.Equals(kv.Key, "Diagram", StringComparison.Ordinal)).Value.Value is true;
```

Then update the `new StateMachineGroupModel(...)` call: replace `diagram: false,` with `diagram: diagram,`.

**Step 3: Verify build + tests**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release
```

Expected: 32/32 + 26/26 pass.

**Step 4: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs
git commit -m "$(cat <<'EOF'
feat(generator): parse Diagram named arg on [StateMachine] + [StateMachineGroup]

Reads the Diagram boolean from each top-level attribute and threads it into
the corresponding model. Defaults to false when absent. No writer changes
yet — emit-when-true lands in Tasks 10 + 11.
EOF
)"
```

---

## Task 5: Detect ZSM0020 (Diagram = true on a class with zero transitions)

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs`

**Step 1: Add the analyzer**

Append to `StateMachineGenerator` (after `AnalyzeDisposeConflict`, the last v1.4 analyzer):

```csharp
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
```

**Step 2: Wire it from `AnalyzeDiagnostics`** (single-machine path)

Inside `AnalyzeDiagnostics`, append after the existing `AnalyzeDisposeConflict(...)` call:

```csharp
        AnalyzeEmptyDiagramRequest(diagram, transitions, type, diagnostics);
```

You'll need to add `bool diagram` to `AnalyzeDiagnostics`'s signature and propagate it from the `Parse` callsite. Add the param near the end of the existing signature (before the trailing `diagnostics` builder).

**Step 3: Wire it from `AnalyzeGroupDiagnostics`** (group path)

In `AnalyzeGroupDiagnostics`, append after the existing analyzers:

```csharp
        var anyTransition = parts.Any(static p => !p.Transitions.IsEmpty);
        if (diagram && !anyTransition)
        {
            diagnostics.Add(Diagnostic.Create(
                StateMachineDiagnostics.EmptyDiagramRequest, location, type.Name));
        }
```

Add `bool diagram` to `AnalyzeGroupDiagnostics`'s signature and propagate from `ParseGroup`.

**Step 4: Verify build + tests**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release
```

Expected: 32/32 + 26/26 pass.

**Step 5: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs
git commit -m "$(cat <<'EOF'
feat(generator): detect ZSM0020 (empty Diagram request)

Fires when Diagram = true is declared on a class with zero transitions
(or a group with no transitions across all parts). The emitted
MermaidDiagram would be empty / useless.
EOF
)"
```

---

## Task 6: Create `MermaidDiagramWriter.cs` — flat machine rendering

**Files:**
- Create: `src/ZeroAlloc.StateMachine.Generator/MermaidDiagramWriter.cs`

**Step 1: Add the writer**

```csharp
namespace ZeroAlloc.StateMachine.Generator;

using System.Linq;
using System.Text;

/// <summary>
/// Emits Mermaid stateDiagram-v2 body content from a <see cref="StateMachineModel"/> or
/// <see cref="StateMachineGroupModel"/>. The output is the diagram body only — callers wrap
/// it in a <c>public const string MermaidDiagram = "..."</c> literal in the generated partial.
/// </summary>
internal static class MermaidDiagramWriter
{
    /// <summary>Emit a Mermaid stateDiagram-v2 body for a single-machine model.</summary>
    public static string Write(StateMachineModel m)
    {
        var sb = new StringBuilder();
        sb.AppendLine("stateDiagram-v2");

        WriteIndented(sb, m, indent: "    ");

        return sb.ToString().TrimEnd();
    }

    private static void WriteIndented(StringBuilder sb, StateMachineModel m, string indent)
    {
        // Initial-state marker.
        sb.Append(indent).Append("[*] --> ").AppendLine(m.InitialState);

        // Transitions.
        foreach (var t in m.Transitions)
        {
            sb.Append(indent);
            sb.Append(t.From).Append(" --> ").Append(t.To).Append(": ").Append(t.On);
            if (t.AfterMs > 0)
                sb.Append(" (after ").Append(t.AfterMs).Append("ms)");
            if (t.HasGuard)
                sb.Append(" [guard]");
            sb.AppendLine();
        }

        // Terminal states.
        foreach (var s in m.TerminalStates)
        {
            sb.Append(indent).Append(s).AppendLine(" --> [*]");
        }
    }
}
```

> **MA0051 note:** `WriteIndented` is ~15 lines; will grow in Tasks 7-8 (composites + history) and Task 9 (groups). Watch the 60-line ceiling and split into helpers when needed.

**Step 2: Verify build**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
```

Expected: 0/0 (the writer is unused; build only verifies syntax).

**Step 3: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/MermaidDiagramWriter.cs
git commit -m "$(cat <<'EOF'
feat(generator): add MermaidDiagramWriter for flat-machine rendering

Initial implementation covers the flat-machine case:
  - stateDiagram-v2 header
  - [*] --> InitialState marker
  - From --> To: Trigger transitions
  - (after Nms) annotation for timed edges
  - [guard] annotation for When = true
  - Terminal --> [*] markers for [Terminal]

Composite nesting, history pseudo-states, and group rendering land in
subsequent commits. Writer is currently unwired — emit-when-Diagram=true
lands in Task 10.
EOF
)"
```

---

## Task 7: Add composite-state nesting to `MermaidDiagramWriter`

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/MermaidDiagramWriter.cs`
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs` (expose a helper to walk sub-FSM transitions)

**Step 1: Add a sub-FSM model accessor**

The composite render needs to walk the sub-FSM's transitions. The existing parser has a `ResolveSubMachineSymbol(parent, stateName)` helper. Add a new public method `BuildSubMachineModel(INamedTypeSymbol subType)` to `StateMachineGenerator` that returns a `StateMachineModel?` for a sub-FSM. It walks the sub-FSM's `[Transition]` / `[CompositeState]` / `[HistoryState]` / `[Terminal]` / `[StateMachine]` attributes the same way `Parse` does, just without the `GeneratorAttributeSyntaxContext` (it takes a raw `INamedTypeSymbol`).

Actually simpler: factor the model-building logic out of `Parse` into a new private `BuildModelFromSymbol(INamedTypeSymbol type)` method that returns `StateMachineModel?`. `Parse` calls it with the matched type; the diagram writer also calls it (via a new `internal` accessor) with each composite's sub-FSM type.

Outline of the refactor:

```csharp
internal static StateMachineModel? BuildModelFromSymbol(INamedTypeSymbol type)
{
    // Find the [StateMachine] attribute on this type.
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

    var ns       = type.ContainingNamespace.IsGlobalNamespace
                 ? null
                 : type.ContainingNamespace.ToDisplayString();
    var isStruct = type.TypeKind == TypeKind.Struct;

    return new StateMachineModel(
        ns, type.Name, isStruct,
        initialState, concurrent,
        stateTypeFqn, stateTypeShort!,
        triggerTypeFqn, triggerTypeShort!,
        transitions, terminalStates,
        compositeStates, historyStates,
        diagram: diagram,
        ImmutableArray<Diagnostic>.Empty);  // diagnostics not relevant for sub-FSM diagram
}
```

Update `Parse` to call this helper and then run diagnostic analysis on the returned model (or inline-compose — your call).

**Step 2: Extend `MermaidDiagramWriter.WriteIndented` to render composites**

Update `WriteIndented` to take an optional `Func<string, StateMachineModel?> resolveSubMachine` parameter (so the test code can stub it). Walk `m.CompositeStates`. For each composite state, emit a `state {StateName} { ... }` block:

```csharp
foreach (var c in m.CompositeStates)
{
    var subModel = resolveSubMachine?.Invoke(c.SubMachineFqn);
    if (subModel is null) continue;

    sb.Append(indent).Append("state ").Append(c.State).AppendLine(" {");
    WriteIndented(sb, subModel, indent + "    ");
    sb.Append(indent).AppendLine("}");
}
```

The `Write(StateMachineModel m)` public entry now needs a sub-machine resolver. Change its signature:

```csharp
public static string Write(StateMachineModel m, System.Func<string, StateMachineModel?> resolveSubMachine)
{
    var sb = new StringBuilder();
    sb.AppendLine("stateDiagram-v2");
    WriteIndented(sb, m, indent: "    ", resolveSubMachine);
    return sb.ToString().TrimEnd();
}
```

The wiring code in `StateMachineWriter` (Task 10) will pass a closure that resolves sub-machine FQNs to `StateMachineModel` instances via `StateMachineGenerator.BuildModelFromSymbol`.

> **MA0051 note:** `WriteIndented` now scans transitions, terminals, AND composites. Split it into `WriteIndented`, `WriteTransitions`, `WriteTerminals`, `WriteComposites` to stay well under 60 lines.

**Step 3: Verify build**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release
```

Expected: 32/32 + 26/26 pass.

**Step 4: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/MermaidDiagramWriter.cs \
        src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs
git commit -m "$(cat <<'EOF'
feat(generator): render composite states as nested state blocks in MermaidDiagramWriter

Composite states render as Mermaid 'state X { ... }' blocks containing
the sub-FSM's transitions / terminals / further-nested composites. The
sub-FSM model is resolved by walking the sub-machine's [StateMachine] +
[Transition] attributes via a new BuildModelFromSymbol helper.

Recursive: a sub-FSM that itself has composites renders correctly.
EOF
)"
```

---

## Task 8: Add history pseudo-state rendering

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/MermaidDiagramWriter.cs`

**Step 1: Annotate history-marked composites**

Inside `WriteComposites` (the helper extracted in Task 7), after opening a composite block whose state is also in `m.HistoryStates`, emit a history pseudo-state line BEFORE recursing into the sub-FSM body:

```csharp
foreach (var c in m.CompositeStates)
{
    var subModel = resolveSubMachine?.Invoke(c.SubMachineFqn);
    if (subModel is null) continue;

    sb.Append(indent).Append("state ").Append(c.State).AppendLine(" {");

    var hasHistory = m.HistoryStates.Any(h => string.Equals(h.State, c.State, StringComparison.Ordinal));
    if (hasHistory)
    {
        sb.Append(indent).AppendLine("    state H as History");
    }

    WriteIndented(sb, subModel, indent + "    ", resolveSubMachine);
    sb.Append(indent).AppendLine("}");
}
```

(Mermaid `state H as History` is the standard syntax for a shallow-history pseudo-state. The arrow into H is implicit — Mermaid renders the H marker without an explicit `[*] --> H` line.)

**Step 2: Verify build + tests**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release
```

Expected: 32/32 + 26/26 pass.

**Step 3: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/MermaidDiagramWriter.cs
git commit -m "$(cat <<'EOF'
feat(generator): render shallow history pseudo-state inside composite blocks

For any composite state that also has a matching [HistoryState] declaration,
the diagram includes a 'state H as History' pseudo-state line at the top
of the composite's nested block.
EOF
)"
```

---

## Task 9: Add group rendering to `MermaidDiagramWriter`

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/MermaidDiagramWriter.cs`

**Step 1: Add group entry point**

Add a new public method:

```csharp
public static string Write(StateMachineGroupModel m)
{
    var sb = new StringBuilder();
    sb.AppendLine("stateDiagram-v2");

    foreach (var p in m.Parts)
    {
        sb.Append("    state ").Append(p.Name).AppendLine(" {");
        WritePart(sb, p, indent: "        ");
        sb.AppendLine("    }");
    }

    return sb.ToString().TrimEnd();
}

private static void WritePart(StringBuilder sb, StateMachinePartModel p, string indent)
{
    sb.Append(indent).Append("[*] --> ").AppendLine(p.InitialState);

    foreach (var t in p.Transitions)
    {
        sb.Append(indent);
        sb.Append(t.From).Append(" --> ").Append(t.To).Append(": ").Append(t.On);
        if (t.AfterMs > 0)
            sb.Append(" (after ").Append(t.AfterMs).Append("ms)");
        if (t.HasGuard)
            sb.Append(" [guard]");
        sb.AppendLine();
    }

    // Groups never have composites or history (ZSM0018 blocks composites in groups);
    // groups also don't have a TerminalStates field on the part model.
}
```

> **Note:** Groups don't render terminals because `StateMachinePartModel` doesn't track them — that field lives on `StateMachineModel` only. If a need arises, it can be added in a follow-up.

**Step 2: Verify build + tests**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release
```

Expected: 32/32 + 26/26 pass.

**Step 3: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/MermaidDiagramWriter.cs
git commit -m "$(cat <<'EOF'
feat(generator): render [StateMachineGroup] as top-level state blocks in MermaidDiagramWriter

Each [StateMachinePart] becomes a 'state {Name} { ... }' block at the
top level of the stateDiagram-v2. Per-part initial state + transitions
+ timed annotations + guards render the same way as flat-machine emit.
EOF
)"
```

---

## Task 10: Wire `MermaidDiagramWriter` into `StateMachineWriter`

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineWriter.cs`

**Step 1: Add a `WriteMermaidDiagram` helper**

Append:

```csharp
    private static void WriteMermaidDiagram(StringBuilder sb, StateMachineModel m,
        System.Func<string, StateMachineModel?> resolveSubMachine)
    {
        if (!m.Diagram) return;

        var diagram = MermaidDiagramWriter.Write(m, resolveSubMachine);
        sb.AppendLine();
        sb.AppendLine($"    /// <summary>Mermaid stateDiagram-v2 representation of this state machine.</summary>");
        sb.Append($"    public const string MermaidDiagram = ");
        AppendQuotedMultiline(sb, diagram);
        sb.AppendLine(";");
    }

    private static void AppendQuotedMultiline(StringBuilder sb, string raw)
    {
        // Emit the diagram as a verbatim string literal: @"...".
        // Quotes inside the diagram body are doubled.
        sb.Append("@\"");
        sb.Append(raw.Replace("\"", "\"\""));
        sb.Append('"');
    }
```

We use a verbatim `@"..."` string for maximum compatibility with `netstandard2.0` (no raw-literal support in older language versions).

**Step 2: Wire it from `Write(StateMachineModel)`**

In the main `Write` method (top of `StateMachineWriter`), just before the final `sb.AppendLine("}");`, call:

```csharp
        WriteMermaidDiagram(sb, model, /* resolver: */ null!);  // placeholder; real resolver wired below
```

Actually we can't use `null!` because the writer might try to deref it. Better: have the entry-point accept the resolver as a param:

```csharp
public static string Write(StateMachineModel model,
    System.Func<string, StateMachineModel?>? resolveSubMachine = null)
```

Or simpler: just pass a static "always-null" lambda — `static _ => (StateMachineModel?)null` — when no resolver is supplied. The MermaidDiagramWriter handles a null return from the resolver (composite block isn't emitted; users see a flat reference). Decide once during implementation.

For the generator path that has the `Compilation` available (i.e., `RegisterSourceOutput`), pass a closure that resolves FQN → `StateMachineModel?` via `StateMachineGenerator.BuildModelFromSymbol`. That closure needs a `Compilation` reference to do `compilation.GetTypeByMetadataName(fqn)`.

Easiest threading: change the generator's `RegisterSourceOutput` callback signature so it has access to the `Compilation`. The current pipeline uses `models` directly. We need to combine `models` with `context.CompilationProvider`:

```csharp
context.RegisterSourceOutput(
    models.Combine(context.CompilationProvider),
    static (ctx, tuple) =>
    {
        var (model, compilation) = tuple;
        // ... existing diagnostic reporting ...

        var resolver = (string fqn) =>
        {
            var sym = compilation.GetTypeByMetadataName(fqn.Replace("global::", ""));
            return sym is null ? null : StateMachineGenerator.BuildModelFromSymbol(sym);
        };

        var source = StateMachineWriter.Write(model, resolver);
        // ... existing AddSource ...
    });
```

> **Caveat:** the `Combine` call breaks the incremental-cacheability of the pipeline (changing any file in the compilation invalidates the model). This is acceptable for the diagram case because the diagram is only emitted for `Diagram = true` classes; the cache penalty is paid only by those. Mention this in the commit body.

**Step 3: Verify build + tests**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release
```

Expected: 32/32 + 26/26 pass. Existing snapshots byte-identical (no fixtures use `Diagram = true`).

**Step 4: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineWriter.cs \
        src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs
git commit -m "$(cat <<'EOF'
feat(generator): emit MermaidDiagram const from StateMachineWriter when Diagram=true

The generator pipeline now combines the per-class model with the
CompilationProvider so the diagram writer can resolve composite sub-FSM
types to their parsed models for nested rendering. The diagram is
emitted as a public const string MermaidDiagram using a verbatim @"..."
literal for netstandard2.0 compatibility.

Caching note: Combine with CompilationProvider partially invalidates
incremental caching for [StateMachine] classes; the cost is paid only
when Diagram = true (existing tests still pass byte-identical).
EOF
)"
```

---

## Task 11: Wire `MermaidDiagramWriter` into `StateMachineGroupWriter`

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineGroupWriter.cs`
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs` (parallel pipeline change)

**Step 1: Emit `MermaidDiagram` from the group writer**

In `StateMachineGroupWriter.Write(StateMachineGroupModel m)`, after the last per-part block but before the closing `}`:

```csharp
        if (m.Diagram)
        {
            var diagram = MermaidDiagramWriter.Write(m);
            sb.AppendLine();
            sb.AppendLine($"    /// <summary>Mermaid stateDiagram-v2 representation of this state-machine group.</summary>");
            sb.Append($"    public const string MermaidDiagram = ");
            sb.Append("@\"").Append(diagram.Replace("\"", "\"\"")).Append('"');
            sb.AppendLine(";");
        }
```

Groups don't need a sub-machine resolver (parts can't contain composites — ZSM0018 enforces).

**Step 2: No pipeline change for groups**

The group pipeline doesn't need to combine with `CompilationProvider` because groups never render sub-FSMs.

**Step 3: Verify build + tests**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release
```

Expected: 32/32 + 26/26 pass.

**Step 4: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineGroupWriter.cs
git commit -m "$(cat <<'EOF'
feat(generator): emit MermaidDiagram const from StateMachineGroupWriter when Diagram=true

Groups don't have composites (ZSM0018 forbids them), so no sub-machine
resolver is needed; the group writer calls MermaidDiagramWriter.Write
directly with the group model.
EOF
)"
```

---

## Task 12: Emit `ArmInitialStateTimers` helper in `StateMachineWriter`

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineWriter.cs`

**Step 1: Add the helper emitter**

Append:

```csharp
    private static void WriteArmInitialStateTimers(StringBuilder sb, StateMachineModel m)
    {
        if (!HasAnyTimedEdge(m)) return;

        var st = m.StateTypeFqn;

        sb.AppendLine();
        sb.AppendLine($"    /// <summary>Arms timers for any timed edges whose From state matches the current state.</summary>");
        sb.AppendLine($"    private void ArmInitialStateTimers()");
        sb.AppendLine($"    {{");
        sb.AppendLine($"        var current = Current;");

        foreach (var t in m.Transitions)
        {
            if (t.AfterMs == 0) continue;
            if (t.Part is not null) continue;  // group parts handled separately

            var field = $"_timer_{t.From}_{t.On}";
            sb.AppendLine($"        if (current == {st}.{t.From})");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            var __t = {field};");
            sb.AppendLine($"            if (__t is null)");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                var __new = new System.Threading.Timer(");
            sb.AppendLine($"                    static s => (({m.ClassName})s!).TryFire({m.TriggerTypeFqn}.{t.On}),");
            sb.AppendLine($"                    this, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);");
            sb.AppendLine($"                __t = System.Threading.Interlocked.CompareExchange(ref {field}, __new, null) ?? __new;");
            sb.AppendLine($"                if (!System.Object.ReferenceEquals(__t, __new)) __new.Dispose();");
            sb.AppendLine($"            }}");
            sb.AppendLine($"            __t.Change({t.AfterMs}, System.Threading.Timeout.Infinite);");
            sb.AppendLine($"        }}");
        }

        sb.AppendLine($"    }}");
    }
```

> **MA0051 note:** if the per-edge if-block emit pushes this over 60 lines (it shouldn't — each block is one method call), extract the inner per-edge emit into a `WriteArmBlockFor(StringBuilder, TransitionModel, string st, string tr, string className)` helper.

**Step 2: Wire it from `WriteConcurrentBody`**

In `WriteConcurrentBody`, after the existing `WriteDispose(sb, m);` (or wherever the concurrent body's tail block lives), append:

```csharp
        WriteArmInitialStateTimers(sb, m);
```

**Step 3: Verify build + tests**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release
```

Expected: 32/32 + 26/26 pass. (Existing snapshots don't have initial-state-armed timers — the Watchdog fixture starts in `Idle`, not `Working`. But the emit now includes a new `ArmInitialStateTimers` method on the Watchdog snapshot.)

> **Snapshot drift WARNING:** This task introduces a new private method on every concurrent class with timed edges. The existing v1.4 snapshots (`TimedTransitionGeneratorTests.SingleTimedEdge#MyApp_Watchdog.g.verified.cs`, etc.) will now have the new method appended. Re-verify these snapshots — inspect the new content, confirm it's correct, then update the `.verified.cs` files. Document the regen in the commit.

**Step 4: Update snapshots**

```bash
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release --filter "FullyQualifiedName~TimedTransitionGeneratorTests"
# Inspect the new .received.cs files in Snapshots/, verify the ArmInitialStateTimers
# emit is correct, then rename .received.cs -> .verified.cs.
```

Same for `StateMachineGroupGeneratorTests.TwoPartsOneTimedEdge` if it changes (it shouldn't — Task 15 wires the group path).

**Step 5: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineWriter.cs \
        tests/ZeroAlloc.StateMachine.Generator.Tests/Snapshots/
git commit -m "$(cat <<'EOF'
feat(generator): emit ArmInitialStateTimers helper in StateMachineWriter

A private method emitted on every concurrent class with timed edges. Walks
all timed edges; for each whose From matches Current, arms the timer using
the existing race-safe Interlocked.CompareExchange + dispose-of-loser pattern.

Snapshot regen: existing v1.4 SingleTimedEdge / MultipleTimedEdges snapshots
gained the new method. Inspected and verified.

Tasks 13-14 invoke this helper from the ctor and from Reset/ResetTo.
EOF
)"
```

---

## Task 13: Emit `HookConstructor` partial + default ctor in `StateMachineWriter`

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineWriter.cs`
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs` (detect user-declared ctor)

**Step 1: Detect whether the user declared their own ctor**

Add a new field to `StateMachineModel`: `bool HasUserCtor`. Update the record signature; update the call sites in `Parse` and `BuildModelFromSymbol` to detect:

```csharp
var hasUserCtor = type.InstanceConstructors
    .Any(c => !c.IsImplicitlyDeclared);
```

Pass it positionally into the constructor (right before `Diagnostics`).

**Step 2: Emit the partial + default ctor**

Append to `StateMachineWriter`:

```csharp
    private static void WriteHookConstructorAndCtor(StringBuilder sb, StateMachineModel m)
    {
        if (!HasAnyTimedEdge(m)) return;

        sb.AppendLine();
        sb.AppendLine($"    /// <summary>Generator-emitted partial hook invoked from the constructor. Arms initial-state timers.</summary>");
        sb.AppendLine($"    private void HookConstructor()");
        sb.AppendLine($"    {{");
        sb.AppendLine($"        ArmInitialStateTimers();");
        sb.AppendLine($"    }}");

        if (!m.HasUserCtor)
        {
            sb.AppendLine();
            sb.AppendLine($"    /// <summary>Default generator-emitted constructor; calls HookConstructor() to arm initial-state timers.</summary>");
            sb.AppendLine($"    public {m.ClassName}()");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        HookConstructor();");
            sb.AppendLine($"    }}");
        }
    }
```

Wire it from `WriteConcurrentBody`, after `WriteArmInitialStateTimers(sb, m);`:

```csharp
        WriteHookConstructorAndCtor(sb, m);
```

**Step 3: Verify build + tests + snapshot regen**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release
```

If any existing snapshot fails due to the new `HookConstructor()` + default ctor emit, inspect + rename `.received.cs` → `.verified.cs`. Document in the commit.

**Step 4: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineWriter.cs \
        src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs \
        src/ZeroAlloc.StateMachine.Generator/StateMachineModel.cs \
        tests/ZeroAlloc.StateMachine.Generator.Tests/Snapshots/
git commit -m "$(cat <<'EOF'
feat(generator): emit HookConstructor + default ctor in StateMachineWriter

For every concurrent class with at least one timed edge, the generator emits:
  - private void HookConstructor() — calls ArmInitialStateTimers().
  - public {ClassName}() — calls HookConstructor() — only when the user
    has NOT declared their own ctor (detected via type.InstanceConstructors).

If the user declares a ctor, they must invoke HookConstructor() themselves
or hit ZSM0021 (Task 16).

Snapshot regen: v1.4 SingleTimedEdge / MultipleTimedEdges now include the
new ctor + hook in the emit.
EOF
)"
```

---

## Task 14: Wire `Reset()` and `ResetTo(state)` to call `ArmInitialStateTimers`

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineWriter.cs`

**Step 1: Update `WriteResetMechanics`**

Inside the `Reset()` body, after the existing `_state = InitialState;` line (and the composite sub-FSM resets), append:

```csharp
        if (HasAnyTimedEdge(m))
        {
            sb.AppendLine($"        ArmInitialStateTimers();");
        }
```

Same change inside the `ResetTo(state)` body, after the existing `_state = state;` assignment.

**Step 2: Verify build + tests + snapshot regen**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release
```

Snapshot regen for the v1.4 timed-edge snapshots is expected. Inspect and rename.

**Step 3: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineWriter.cs \
        tests/ZeroAlloc.StateMachine.Generator.Tests/Snapshots/
git commit -m "$(cat <<'EOF'
feat(generator): Reset() and ResetTo(state) now arm initial-state timers

Both internal state-population methods now call ArmInitialStateTimers()
after assigning state. This closes the v1.4 caveat: any path that lands
on a state with a timed edge arms that edge's timer.

Snapshot regen: ArmInitialStateTimers() call now appears in the Reset
and ResetTo bodies in the v1.4 timed-edge snapshots.
EOF
)"
```

---

## Task 15: Emit per-part arm helpers + HookConstructor + group ctor in `StateMachineGroupWriter`

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineGroupWriter.cs`
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineGroupModel.cs` (add `HasUserCtor`)
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs` (detect user ctor in `ParseGroup`)

**Step 1: Add `HasUserCtor` to `StateMachineGroupModel`**

```csharp
internal sealed record StateMachineGroupModel(
    string? Namespace,
    string ClassName,
    ImmutableArray<StateMachinePartModel> Parts,
    bool Diagram,
    bool HasUserCtor,                                       // NEW
    ImmutableArray<Diagnostic> Diagnostics
);
```

Update `ParseGroup` to populate it (`type.InstanceConstructors.Any(c => !c.IsImplicitlyDeclared)`).

**Step 2: Emit per-part `ArmInitialStateTimers_<Name>()` helpers**

Inside `WritePartBody` (or after — your call), emit:

```csharp
    private static void WritePartArmInitialStateTimers(StringBuilder sb, string className, StateMachinePartModel p)
    {
        var hasTimed = p.Transitions.Any(static t => t.AfterMs > 0);
        if (!hasTimed) return;

        var st = p.StateTypeFqn;
        var tr = p.TriggerTypeFqn;

        sb.AppendLine();
        sb.AppendLine($"    private void ArmInitialStateTimers_{p.Name}()");
        sb.AppendLine($"    {{");
        sb.AppendLine($"        var current = {p.Name}Current;");
        foreach (var t in p.Transitions)
        {
            if (t.AfterMs == 0) continue;
            var field = $"_timer_{p.Name}_{t.From}_{t.On}";
            sb.AppendLine($"        if (current == {st}.{t.From})");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            var __t = {field};");
            sb.AppendLine($"            if (__t is null)");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                var __new = new System.Threading.Timer(");
            sb.AppendLine($"                    static s => (({className})s!).TryFire{p.Name}({tr}.{t.On}),");
            sb.AppendLine($"                    this, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);");
            sb.AppendLine($"                __t = System.Threading.Interlocked.CompareExchange(ref {field}, __new, null) ?? __new;");
            sb.AppendLine($"                if (!System.Object.ReferenceEquals(__t, __new)) __new.Dispose();");
            sb.AppendLine($"            }}");
            sb.AppendLine($"            __t.Change({t.AfterMs}, System.Threading.Timeout.Infinite);");
            sb.AppendLine($"        }}");
        }
        sb.AppendLine($"    }}");
    }
```

Call from `WritePartBody` after `WritePartHooks`.

**Step 3: Emit group-level `HookConstructor` and default ctor**

Append a new method `WriteGroupHookAndCtor`:

```csharp
    private static void WriteGroupHookAndCtor(StringBuilder sb, StateMachineGroupModel m)
    {
        var anyTimed = m.Parts.Any(static p => p.Transitions.Any(static t => t.AfterMs > 0));
        if (!anyTimed) return;

        sb.AppendLine();
        sb.AppendLine($"    /// <summary>Generator-emitted partial hook invoked from the constructor.</summary>");
        sb.AppendLine($"    private void HookConstructor()");
        sb.AppendLine($"    {{");
        foreach (var p in m.Parts)
        {
            if (p.Transitions.Any(static t => t.AfterMs > 0))
                sb.AppendLine($"        ArmInitialStateTimers_{p.Name}();");
        }
        sb.AppendLine($"    }}");

        if (!m.HasUserCtor)
        {
            sb.AppendLine();
            sb.AppendLine($"    public {m.ClassName}()");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        HookConstructor();");
            sb.AppendLine($"    }}");
        }
    }
```

Wire from `Write(StateMachineGroupModel)`, just before the closing `}`:

```csharp
        WriteGroupHookAndCtor(sb, m);
```

**Step 4: Verify build + tests + snapshot regen**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release
```

`TwoPartsOneTimedEdge` snapshot gains the new per-part arm helper + hook + default ctor. Inspect + rename.

**Step 5: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineGroupWriter.cs \
        src/ZeroAlloc.StateMachine.Generator/StateMachineGroupModel.cs \
        src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs \
        tests/ZeroAlloc.StateMachine.Generator.Tests/Snapshots/
git commit -m "$(cat <<'EOF'
feat(generator): emit per-part ArmInitialStateTimers + group ctor in StateMachineGroupWriter

For each part with at least one timed edge: a private
ArmInitialStateTimers_{PartName}() helper using the race-safe
lazy-init pattern. Plus a group-level HookConstructor that calls
each timed part's helper, and a default ctor invoking HookConstructor
when no user ctor exists.

Snapshot regen: TwoPartsOneTimedEdge gains the new arm helpers + ctor.
EOF
)"
```

---

## Task 16: Detect ZSM0021 (user-declared ctor without `HookConstructor()` invocation)

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs`

**Step 1: Add the analyzer**

Append:

```csharp
    private static void AnalyzeMissingHookConstructorInvocation(
        INamedTypeSymbol type,
        bool hasTimedEdges,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (!hasTimedEdges) return;

        var userCtors = type.InstanceConstructors
            .Where(c => !c.IsImplicitlyDeclared)
            .ToArray();
        if (userCtors.Length == 0) return;

        var location = type.Locations.Length > 0 ? type.Locations[0] : Location.None;

        foreach (var ctor in userCtors)
        {
            if (CtorInvokesHookConstructor(ctor)) return;
        }

        diagnostics.Add(Diagnostic.Create(
            StateMachineDiagnostics.MissingHookConstructorInvocation, location, type.Name));
    }

    private static bool CtorInvokesHookConstructor(IMethodSymbol ctor)
    {
        foreach (var syntaxRef in ctor.DeclaringSyntaxReferences)
        {
            var node = syntaxRef.GetSyntax();
            if (node is null) continue;

            // Walk the ctor body's descendant invocations; look for HookConstructor().
            foreach (var inv in node.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>())
            {
                if (inv.Expression is Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax id &&
                    string.Equals(id.Identifier.ValueText, "HookConstructor", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        return false;
    }
```

**Step 2: Wire from `AnalyzeDiagnostics`** (single-machine path)

Append:

```csharp
        var hasTimed = transitions.Any(static t => t.AfterMs > 0);
        AnalyzeMissingHookConstructorInvocation(type, hasTimed, diagnostics);
```

**Step 3: Wire from `AnalyzeGroupDiagnostics`** (group path)

Same logic — `hasTimed = m.Parts.Any(p => p.Transitions.Any(t => t.AfterMs > 0))`.

**Step 4: Verify build + tests**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release
```

Expected: 32/32 + 26/26 pass (no fixture currently has both timed edges + user-declared ctor).

**Step 5: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs
git commit -m "$(cat <<'EOF'
feat(generator): detect ZSM0021 (user-declared ctor must call HookConstructor)

Syntactic walk of every user-declared ctor's body looking for an
HookConstructor() invocation. If the class has at least one timed
edge AND a user ctor AND none of the user ctors invoke HookConstructor(),
fire ZSM0021.

Best-effort: indirect invocations (via helper methods) are not detected;
users can #pragma warning disable ZSM0021 for those edge cases.
EOF
)"
```

---

## Task 17: Snapshot tests for B4 (Mermaid diagram emit)

**Files:**
- Create: `tests/ZeroAlloc.StateMachine.Generator.Tests/MermaidDiagramGeneratorTests.cs`
- Create (via verify): `Snapshots/MermaidDiagramGeneratorTests.Flat_Diagram#MyApp_Order.g.verified.cs`
- Create (via verify): `Snapshots/MermaidDiagramGeneratorTests.Composite_Diagram#MyApp_Loading.g.verified.cs` + `*_LoadingFsm.g.verified.cs`
- Create (via verify): `Snapshots/MermaidDiagramGeneratorTests.Group_Diagram#MyApp_Device.Group.g.verified.cs`

**Step 1: Add the test fixture**

```csharp
namespace ZeroAlloc.StateMachine.Generator.Tests;

using System.Threading.Tasks;
using VerifyXunit;
using Xunit;

[UsesVerify]
public class MermaidDiagramGeneratorTests
{
    [Fact]
    public Task Flat_Diagram()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
namespace MyApp;

public enum OS { Idle, Submitted, Shipped }
public enum OT { Submit, Ship, Cancel }

[StateMachine(InitialState = ""Idle"", Diagram = true)]
[Transition<OS, OT>(From = OS.Idle, On = OT.Submit, To = OS.Submitted)]
[Transition<OS, OT>(From = OS.Submitted, On = OT.Ship, To = OS.Shipped, When = true)]
[Terminal<OS>(State = OS.Shipped)]
public partial class Order { }
";
        return TestHelper.Verify<StateMachineGenerator>(source);
    }

    [Fact]
    public Task Composite_Diagram()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
namespace MyApp;

public enum LoadingState { Fetching, Parsing, Done }
public enum AppTrigger { Begin, Tick, Complete }

[StateMachine(InitialState = ""Fetching"")]
[Transition<LoadingState, AppTrigger>(From = LoadingState.Fetching, On = AppTrigger.Tick,     To = LoadingState.Parsing)]
[Transition<LoadingState, AppTrigger>(From = LoadingState.Parsing,  On = AppTrigger.Complete, To = LoadingState.Done)]
[Terminal<LoadingState>(State = LoadingState.Done)]
public partial class LoadingFsm { }

public enum AppState { Idle, Loading, Ready }

[StateMachine(InitialState = ""Idle"", Diagram = true)]
[Transition<AppState, AppTrigger>(From = AppState.Idle,    On = AppTrigger.Begin,    To = AppState.Loading)]
[Transition<AppState, AppTrigger>(From = AppState.Loading, On = AppTrigger.Complete, To = AppState.Ready)]
[CompositeState<AppState>(State = AppState.Loading, SubMachine = typeof(LoadingFsm))]
[HistoryState<AppState>(State = AppState.Loading)]
public partial class App { }
";
        return TestHelper.Verify<StateMachineGenerator>(source);
    }

    [Fact]
    public Task Group_Diagram()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
namespace MyApp;

public enum OpS { Idle, Running }
public enum OpT { Start, Stop }
public enum ConnS { Disconnected, Connected }
public enum ConnT { Connect, Disconnect }

[StateMachineGroup(Diagram = true)]
[StateMachinePart<OpS,   OpT>(Name = ""Op"",   InitialState = OpS.Idle)]
[StateMachinePart<ConnS, ConnT>(Name = ""Conn"", InitialState = ConnS.Disconnected)]
[Transition<OpS,   OpT>(From = OpS.Idle,    On = OpT.Start, To = OpS.Running, Part = ""Op"")]
[Transition<OpS,   OpT>(From = OpS.Running, On = OpT.Stop,  To = OpS.Idle,    Part = ""Op"")]
[Transition<ConnS, ConnT>(From = ConnS.Disconnected, On = ConnT.Connect,    To = ConnS.Connected,    Part = ""Conn"")]
[Transition<ConnS, ConnT>(From = ConnS.Connected,    On = ConnT.Disconnect, To = ConnS.Disconnected, Part = ""Conn"")]
public partial class Device { }
";
        return TestHelper.Verify<StateMachineGenerator>(source);
    }
}
```

**Step 2: Run; expect 3 received files**

```bash
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release --filter "FullyQualifiedName~MermaidDiagramGeneratorTests"
```

Expected: 3 tests FAIL with "received but no verified" diff. Multiple `.received.cs` files (one per emitted source).

**Step 3: Inspect each `.received.cs`**

Verify each one:
- `Flat_Diagram` → `MermaidDiagram` const exists; contains `stateDiagram-v2`, `[*] --> Idle`, `Idle --> Submitted: Submit`, `Submitted --> Shipped: Ship [guard]`, `Shipped --> [*]`.
- `Composite_Diagram` → parent's diagram contains `state Loading {` block with `state H as History` and the sub-FSM's transitions; sub-FSM (`LoadingFsm`) does NOT have its own MermaidDiagram const (Diagram only set on App).
- `Group_Diagram` → contains two top-level `state Op { ... }` + `state Conn { ... }` blocks.

**Step 4: Rename `.received.cs` → `.verified.cs`**

```bash
cd tests/ZeroAlloc.StateMachine.Generator.Tests/Snapshots
# Rename each pair; the exact filenames depend on emitted hint names.
```

**Step 5: Re-run; expect 3 PASS**

```bash
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release --filter "FullyQualifiedName~MermaidDiagramGeneratorTests"
```

Test count: was 32, now 35.

**Step 6: Commit**

```bash
git add tests/ZeroAlloc.StateMachine.Generator.Tests/MermaidDiagramGeneratorTests.cs \
        tests/ZeroAlloc.StateMachine.Generator.Tests/Snapshots/MermaidDiagramGeneratorTests*.verified.cs
git commit -m "$(cat <<'EOF'
test(generator): snapshot tests for Mermaid diagram emit (B4)

Three scenarios:
  - Flat_Diagram: initial + transitions + guard-annotated + terminal
  - Composite_Diagram: parent diagram includes nested 'state X { ... }'
    block with 'state H as History' for the [HistoryState]
  - Group_Diagram: top-level 'state Op { ... }' + 'state Conn { ... }'

Snapshots assert correct Mermaid stateDiagram-v2 syntax, including
sub-FSM walking via the new BuildModelFromSymbol helper.
EOF
)"
```

---

## Task 18: Diagnostic tests for ZSM0020 + ZSM0021

**Files:**
- Modify: `tests/ZeroAlloc.StateMachine.Generator.Tests/DiagnosticTests.cs`

**Step 1: Append the new tests**

```csharp
    [Fact]
    public async Task ZSM0020_FiresWhen_Diagram_OnEmptyClass()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A } public enum T { Go }
[StateMachine(InitialState = ""A"", Diagram = true)]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.Contains(diags, d => string.Equals(d.Id, ""ZSM0020"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ZSM0020_FiresWhen_Diagram_OnEmptyGroup()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
[StateMachineGroup(Diagram = true)]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.Contains(diags, d => string.Equals(d.Id, ""ZSM0020"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ZSM0021_FiresWhen_UserCtor_DoesNotCall_HookConstructor()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B } public enum T { Go }
[StateMachine(InitialState = ""A"", Concurrent = true)]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, AfterMs = 1000)]
public partial class M
{
    public M(int x) { /* does NOT call HookConstructor */ }
}
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.Contains(diags, d => string.Equals(d.Id, ""ZSM0021"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ZSM0021_DoesNotFire_When_UserCtor_Calls_HookConstructor()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B } public enum T { Go }
[StateMachine(InitialState = ""A"", Concurrent = true)]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, AfterMs = 1000)]
public partial class M
{
    public M(int x) { HookConstructor(); }
}
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.DoesNotContain(diags, d => string.Equals(d.Id, ""ZSM0021"", StringComparison.Ordinal));
    }
```

(Use `string.Equals` with `StringComparison.Ordinal` per MA0006 conventions from the v1.4 Task 15 deviation.)

**Step 2: Run**

```bash
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release --filter "FullyQualifiedName~DiagnosticTests"
```

Expected: all 4 new tests PASS. Test count: was 35 (after Task 17), now 39.

**Step 3: Commit**

```bash
git add tests/ZeroAlloc.StateMachine.Generator.Tests/DiagnosticTests.cs
git commit -m "$(cat <<'EOF'
test(generator): diagnostic tests for ZSM0020 + ZSM0021

  ZSM0020: fires when [StateMachine(Diagram = true)] has no transitions.
  ZSM0020: fires when [StateMachineGroup(Diagram = true)] has no parts.
  ZSM0021: fires when user-declared ctor doesn't call HookConstructor().
  ZSM0021: negative — does NOT fire when user ctor invokes HookConstructor().
EOF
)"
```

---

## Task 19: Runtime tests for initial-state arm

**Files:**
- Create: `tests/ZeroAlloc.StateMachine.Tests/InitialStateArmTests.cs`

**Step 1: Add the test fixture**

```csharp
namespace ZeroAlloc.StateMachine.Tests;

using System.Threading.Tasks;
using Xunit;
using ZeroAlloc.StateMachine;

#pragma warning disable MA0048   // file holds multiple top-level types
#pragma warning disable ZSM0002  // sink states are intentional

public enum WatchState { Working, Dead }
public enum WatchTrigger { Timeout }

[StateMachine(InitialState = ""Working"", Concurrent = true)]
[Transition<WatchState, WatchTrigger>(From = WatchState.Working, On = WatchTrigger.Timeout, To = WatchState.Dead, AfterMs = 500)]
[Terminal<WatchState>(State = WatchState.Dead)]
public partial class InitialArmWatchdog { }

public class InitialStateArmTests
{
    [Fact]
    public async Task Constructor_arms_initial_state_timer()
    {
        using var w = new InitialArmWatchdog();
        Assert.Equal(WatchState.Working, w.Current);

        // Give the timer time to fire — no user TryFire call.
        await Task.Delay(1000);
        Assert.Equal(WatchState.Dead, w.Current);
    }

    [Fact]
    public async Task Reset_rearms_initial_state_timer()
    {
        using var w = new InitialArmWatchdog();
        await Task.Delay(1000);
        Assert.Equal(WatchState.Dead, w.Current);

        // Reset puts state back to Working; should re-arm the timer.
        // Reset is internal — access via reflection only if needed (or skip this test
        // and add a [Fact(Skip = ...)] if internal access is too fiddly).
        var resetMethod = typeof(InitialArmWatchdog).GetMethod(""Reset"",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(resetMethod);
        resetMethod!.Invoke(w, null);
        Assert.Equal(WatchState.Working, w.Current);

        await Task.Delay(1000);
        Assert.Equal(WatchState.Dead, w.Current);
    }
}
```

(`Reset` is `internal`, so the test reflects to invoke it. If that's awkward, mark the second test `[Fact(Skip = "Reset is internal; see snapshot test instead")]` and skip it.)

**Step 2: Run**

```bash
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release --filter "FullyQualifiedName~InitialStateArmTests"
```

Expected: both PASS. Runtime test count: was 26 (after v1.4), now 28.

**Step 3: Commit**

```bash
git add tests/ZeroAlloc.StateMachine.Tests/InitialStateArmTests.cs
git commit -m "$(cat <<'EOF'
test: runtime tests for initial-state arm (closes v1.4 caveat)

Two scenarios:
  - Constructor arms initial-state timer; no user TryFire needed.
  - Reset() re-arms after returning to the initial state.

Reset is internal; the test uses reflection to invoke it. Group runtime
coverage lands in a future test if a real consumer asks.
EOF
)"
```

---

## Task 20: Documentation updates

**Files:**
- Modify: `docs/core-concepts/timeout-transitions.md`
- Create: `docs/core-concepts/diagram-export.md`
- Create: `docs/diagnostics/ZSM0020.md`, `docs/diagnostics/ZSM0021.md`
- Modify: `docs/attributes.md`
- Modify: `docs/index.md`

**Step 1: Update `timeout-transitions.md`**

Remove the "Caveats" section entirely (the gap is closed). Update the body where it describes when timers arm to read "armed on construction, on every entry into the source state, and on `Reset()` / `ResetTo(state)`".

**Step 2: Create `docs/core-concepts/diagram-export.md`**

Follow the existing core-concepts template (frontmatter `id`/`title`/`sidebar_position`, intro, sections, "Related" footer at bottom). Cover:
- What `Diagram = true` does.
- Where the diagram lands (`public const string MermaidDiagram`).
- A short example with rendered Mermaid output as a fenced block.
- Notes on what's rendered (composites, history, groups, timed annotations, guards, terminals).
- Reference ZSM0020.

**Step 3: Create `docs/diagnostics/ZSM0020.md` + `ZSM0021.md`**

Follow the existing diagnostics template — see `docs/diagnostics/ZSM0019.md` for the closest reference. Each has Severity, Example (triggering source), How-to-fix (resolution source).

**Step 4: Update `docs/attributes.md`**

Add a row to `[StateMachine]`'s properties table for `Diagram` (link to `core-concepts/diagram-export.md`). Same for `[StateMachineGroup]`. Both new properties: `bool`, default `false`.

**Step 5: Update `docs/index.md`**

Add entries to the top-level table of contents:
- Core concepts → `diagram-export.md`.
- Diagnostics → `ZSM0020.md`, `ZSM0021.md`.

**Step 6: Commit**

```bash
git add docs/
git commit -m "$(cat <<'EOF'
docs: diagram export (B4) + initial-arm closes v1.4 caveat

  - Removed the Caveats section from timeout-transitions.md (no longer
    a caveat after the initial-arm fix).
  - New core-concepts/diagram-export.md page describing Diagram = true,
    the MermaidDiagram const surface, and what gets rendered.
  - New diagnostics pages for ZSM0020 + ZSM0021.
  - attributes.md adds Diagram entries on [StateMachine] + [StateMachineGroup].
  - index.md adds the new pages to the top-level TOC.
EOF
)"
```

---

## Task 21: Push branch + open PR + merge when green

**Files:** (no source — git + gh)

**Step 1: Final whole-repo build + test**

```bash
dotnet build -c Release
dotnet test -c Release
```

Expected: 0 errors, all warnings pre-existing (the same MSB3277 + MA0048 ones on `benchmarks/`). Tests: 39/39 generator + 28/28 runtime.

**Step 2: Push the branch**

```bash
git push -u origin feat/mermaid-export-and-initial-arm
```

**Step 3: Open the PR**

```bash
gh pr create --title "feat: Mermaid diagram export (B4) + initial-state arm fix" --body "$(cat <<'EOF'
## Summary

Closes out the StateMachine roadmap with two threads:

- **B4 — Mermaid diagram export.** Opt-in via `[StateMachine(Diagram = true)]` / `[StateMachineGroup(Diagram = true)]`. The generator emits a `public const string MermaidDiagram` with a full Mermaid `stateDiagram-v2` rendering: composites nested, history pseudo-states, group parts as top-level state blocks, timed edges annotated with `(after Nms)`, guards labeled `[guard]`, terminals as `--> [*]`.

- **Initial-state arm follow-up (closes the v1.4 caveat).** Timers now arm at construction, on every entry into the source state, and on `Reset()` / `ResetTo(state)`. The generator emits a `partial void HookConstructor()` + a default ctor when the user hasn't declared one; ZSM0021 fires if a user-declared ctor doesn't invoke `HookConstructor()`.

Two new diagnostics: `ZSM0020` (warning: empty diagram request), `ZSM0021` (error: missing HookConstructor invocation).

Design doc: `docs/plans/2026-05-23-mermaid-export-and-initial-arm-design.md`.
Plan: `docs/plans/2026-05-23-mermaid-export-and-initial-arm.md`.

## Test plan

- [x] Generator snapshot tests for Mermaid emit (flat / composite / group)
- [x] Diagnostic tests for ZSM0020 (positive: empty class) + ZSM0021 (positive + negative)
- [x] Runtime tests for initial-state arm (ctor + Reset)
- [x] All v1.4 tests still pass (32 → 39 generator, 26 → 28 runtime)
- [x] Existing v1.4 snapshots regenerated to include the new `ArmInitialStateTimers` + `HookConstructor` + ctor emit; visually inspected

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

**Step 4: Wait for CI**

```bash
until [ -z "$(gh pr checks --jq '.[] | select(.state == "PENDING")' 2>&1 | head -1)" ]; do sleep 30; done
gh pr checks
```

Expected: all green.

**Step 5: Merge with admin (matches PR #27's pattern; user authorized in advance)**

```bash
gh pr merge --squash --delete-branch --admin
```

**Step 6: Sync local main**

```bash
git checkout main && git fetch origin main && git reset --hard origin/main
```

---

## Notes for the implementer

- **MA0051 (60-line method limit).** Most new methods are emitter helpers; if any starts pushing 60 lines, split it. The Mermaid writer's `WriteIndented` is the most likely candidate.
- **RS1032 (CodeAnalysis message style).** Two new descriptors. Watch for interior periods + trailing period requirement.
- **MA0006 (string.Equals vs ==).** Used consistently in new code per the v1.4 Task 15 deviation.
- **PublicAPI tracking.** Four new lines for `Diagram` on the two attributes. RS0016/RS0017 will tell you if you missed any.
- **VerifyXunit snapshots.** Task 12, 13, 14 regenerate existing v1.4 snapshots because the emit shape now includes ctor/hook/arm helpers. Inspect each diff and rename `.received.cs` → `.verified.cs` only after confirming correctness.
- **Cross-class composite rendering** (Task 7) goes through `BuildModelFromSymbol` on metadata-only symbols — confirmed working for in-assembly sub-FSMs; cross-assembly should work but isn't covered by tests in this PR (defer until a real consumer asks).
- **`Combine` with CompilationProvider** (Task 10) partially invalidates incremental caching for the diagram pipeline. Acceptable trade-off; only `Diagram = true` users pay it.
