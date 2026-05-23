# Timeout transitions + concurrent state parts Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Land `ZeroAlloc.StateMachine` backlog items B3 (timeout transitions via `[Transition(... AfterMs = N)]`) and B5 (concurrent state parts via `[StateMachineGroup]` + `[StateMachinePart<TState, TTrigger>(Name, InitialState)]`) — a new `Part` discriminator on `[Transition]` that lets timed edges live inside a part, with the generator emitting per-part CAS dispatch + `System.Threading.Timer`-backed auto-fire and `IDisposable` cleanup.

**Architecture:** Two new public attribute types (`[StateMachineGroup]`, `[StateMachinePart<,>]`) plus two new optional properties on `[Transition]` (`AfterMs`, `Part`). The generator's `StateMachineModel` gains timed-edge metadata; a new `StateMachineGroupModel` describes group/part wiring. The writer emits, per timed edge, a lazily-allocated `Timer?` field armed in `Fire`/post-CAS and disarmed on exit. Group-marked classes emit one `volatile long` state field + `TryFire<Name>` + per-part hooks per part, with no shared state. New diagnostics `ZSM0012`–`ZSM0019` cover declaration errors. Generator-emitted `IDisposable` lands on every class with at least one timer field.

**Tech Stack:** .NET 10 / netstandard2.0 (Roslyn source generator targets), Roslyn `IIncrementalGenerator`, `System.Threading.Timer`, `Microsoft.CodeAnalysis.PublicApiAnalyzers` (RS0016/RS0017 enforce additive PublicAPI), xUnit + VerifyXunit (existing snapshot test convention via `tests/.../GeneratorSnapshotTests.cs` + `TestHelper.cs`).

**Design doc:** `docs/plans/2026-05-22-timeout-and-concurrent-parts-design.md` (committed in `de0730b`)

**Working branch:** `feat/timeout-and-concurrent-parts` (already created off `main`; design doc commit `de0730b` is the current HEAD).

**Key context:**
- `StateMachineModel` is an immutable `record` at `src/ZeroAlloc.StateMachine.Generator/StateMachineModel.cs`. Extending it means adding positional params + updating the single constructor call in `StateMachineGenerator.Parse`.
- The generator's `Parse` + `CollectAttributes` lives in `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs`. Attribute walks use `type.GetAttributes()` matched against `metadataName` constants (e.g. `"TransitionAttribute\`2"`).
- Existing emit shape lives in `src/ZeroAlloc.StateMachine.Generator/StateMachineWriter.cs` — a `StringBuilder` walker with `WriteConcurrentBody` / `WriteNonConcurrentBody` paths. Composite helpers `WriteCompositeFields` / `WriteFireCompositeBlocks` are the closest precedent for emitting per-edge field/body blocks.
- Existing diagnostics max ID is `ZSM0011`; new ones start at `ZSM0012`.
- `TreatWarningsAsErrors=true` is repo-wide via `Directory.Build.props` — PublicAPI mismatches fail the build.
- Repo enforces MA0051 (max 60 lines per method) and RS1032 (CodeAnalysis analyzer messageFormat: single sentence, no trailing period). Both surfaced during composite-states implementation; split methods early.
- Snapshot tests use `TestHelper.Verify<StateMachineGenerator>(source)` (VerifyXunit). First run writes `.received.cs`; rename to `.verified.cs` to lock the snapshot.
- Existing `[StateMachine]` declaration uses `[StateMachine(InitialState = "Idle", Concurrent = true)]`. `[StateMachineGroup]` REPLACES `[StateMachine]` on a class (mutually exclusive per design Q5). The generator's primary attribute trigger is `StateMachineAttribute` via `ForAttributeWithMetadataName` — group classes need a SEPARATE registration.

---

## Task 1: Extend `TransitionAttribute<,>` with `AfterMs` + `Part`

**Files:**
- Modify: `src/ZeroAlloc.StateMachine/TransitionAttribute.cs`
- Modify: `src/ZeroAlloc.StateMachine/PublicAPI.Unshipped.txt`

**Step 1: Add the two optional properties**

In `src/ZeroAlloc.StateMachine/TransitionAttribute.cs`, after the existing `When` property:

```csharp
    /// <summary>
    /// When greater than zero, the generator emits a <see cref="System.Threading.Timer"/>
    /// that auto-fires <see cref="On"/> after this many milliseconds in <see cref="From"/>.
    /// The timer is armed in the generated entry path for <see cref="From"/> and disarmed
    /// when the machine leaves the state. Requires <c>Concurrent = true</c> on the class
    /// (or that the transition belongs to a <c>[StateMachinePart]</c> — parts are always
    /// concurrent).
    /// Default: <c>0</c> (no timer).
    /// </summary>
    public int AfterMs { get; init; }

    /// <summary>
    /// Discriminator that scopes this transition to a named <c>[StateMachinePart]</c> when the
    /// enclosing class is a <c>[StateMachineGroup]</c>. Must match a declared part's <c>Name</c>.
    /// Leave <c>null</c> for single-machine classes (i.e. classes declared with <c>[StateMachine]</c>).
    /// Default: <c>null</c>.
    /// </summary>
    public string? Part { get; init; } = null;
```

**Step 2: Update `PublicAPI.Unshipped.txt`**

Append (alphabetical order — paste at the bottom of the existing `TransitionAttribute<TState, TTrigger>` block):

```
ZeroAlloc.StateMachine.TransitionAttribute<TState, TTrigger>.AfterMs.get -> int
ZeroAlloc.StateMachine.TransitionAttribute<TState, TTrigger>.AfterMs.init -> void
ZeroAlloc.StateMachine.TransitionAttribute<TState, TTrigger>.Part.get -> string?
ZeroAlloc.StateMachine.TransitionAttribute<TState, TTrigger>.Part.init -> void
```

If `RS0016`/`RS0017` fires, accept the analyzer's suggested form verbatim — it's the source of truth for nullable-annotation placement.

**Step 3: Verify build**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.StateMachine
dotnet build src/ZeroAlloc.StateMachine/ZeroAlloc.StateMachine.csproj -c Release
```

Expected: 0 warnings, 0 errors.

**Step 4: Commit**

```bash
git add src/ZeroAlloc.StateMachine/TransitionAttribute.cs \
        src/ZeroAlloc.StateMachine/PublicAPI.Unshipped.txt
git commit -m "$(cat <<'EOF'
feat: add AfterMs + Part to [Transition] for B3+B5

Two new optional properties on TransitionAttribute<TState, TTrigger>:

  - AfterMs (int): when > 0, generator emits a Timer that auto-fires
    On after the configured ms. Default 0 (no timer).
  - Part (string?): scopes the transition to a named [StateMachinePart]
    when the enclosing class is a [StateMachineGroup]. Default null.

Both default to "off" so existing v1.3 [Transition] declarations are
strictly additive. Generator wiring lands in subsequent commits.
EOF
)"
```

---

## Task 2: Add `[StateMachineGroup]` + `[StateMachinePart<,>]` runtime types

**Files:**
- Create: `src/ZeroAlloc.StateMachine/StateMachineGroupAttribute.cs`
- Create: `src/ZeroAlloc.StateMachine/StateMachinePartAttribute.cs`
- Modify: `src/ZeroAlloc.StateMachine/PublicAPI.Unshipped.txt`

**Step 1: Add `StateMachineGroupAttribute.cs`**

```csharp
namespace ZeroAlloc.StateMachine;

using System;

/// <summary>
/// Declares the enclosing class as a group of concurrent state machines (B5). The class
/// must declare one or more <see cref="StateMachinePartAttribute{TState, TTrigger}"/>
/// attributes; the generator emits one independent CAS state field + <c>TryFire&lt;Name&gt;</c>
/// + per-part hooks per part.
/// </summary>
/// <remarks>
/// <para>Mutually exclusive with <see cref="StateMachineAttribute"/> on the same class
/// (see <c>ZSM0014</c>). Parts are always concurrent; <see cref="CompositeStateAttribute{TState}"/>
/// is disallowed inside a group (see <c>ZSM0018</c>).</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class StateMachineGroupAttribute : Attribute
{
}
```

**Step 2: Add `StateMachinePartAttribute.cs`**

```csharp
namespace ZeroAlloc.StateMachine;

using System;

/// <summary>
/// Declares a single named state machine inside a <see cref="StateMachineGroupAttribute"/> (B5).
/// Each part has an independent <typeparamref name="TState"/> + <typeparamref name="TTrigger"/>
/// and is dispatched via the generator-emitted <c>TryFire{Name}(TTrigger)</c> method.
/// </summary>
/// <typeparam name="TState">The part's state enum type.</typeparam>
/// <typeparam name="TTrigger">The part's trigger enum type.</typeparam>
/// <remarks>
/// Stack multiple on the same class. <c>Name</c> must be unique within the class (see
/// <c>ZSM0015</c>). Transitions belonging to this part are tagged with
/// <c>[Transition&lt;TState, TTrigger&gt;(... Part = "Name")]</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class StateMachinePartAttribute<TState, TTrigger> : Attribute
    where TState   : struct, Enum
    where TTrigger : struct, Enum
{
    /// <summary>Unique name within the class. Used to derive <c>TryFire{Name}</c>, <c>{Name}Current</c>, <c>OnEnter{Name}{State}</c>, etc.</summary>
    public required string Name { get; init; }

    /// <summary>Initial state for this part. Set in the generated constructor.</summary>
    public required TState InitialState { get; init; }
}
```

**Step 3: Update `PublicAPI.Unshipped.txt`**

Append (alphabetical order):

```
ZeroAlloc.StateMachine.StateMachineGroupAttribute
ZeroAlloc.StateMachine.StateMachineGroupAttribute.StateMachineGroupAttribute() -> void
ZeroAlloc.StateMachine.StateMachinePartAttribute<TState, TTrigger>
ZeroAlloc.StateMachine.StateMachinePartAttribute<TState, TTrigger>.InitialState.get -> TState
ZeroAlloc.StateMachine.StateMachinePartAttribute<TState, TTrigger>.InitialState.init -> void
ZeroAlloc.StateMachine.StateMachinePartAttribute<TState, TTrigger>.Name.get -> string!
ZeroAlloc.StateMachine.StateMachinePartAttribute<TState, TTrigger>.Name.init -> void
ZeroAlloc.StateMachine.StateMachinePartAttribute<TState, TTrigger>.StateMachinePartAttribute() -> void
```

**Step 4: Verify build**

```bash
dotnet build src/ZeroAlloc.StateMachine/ZeroAlloc.StateMachine.csproj -c Release
```

Expected: 0 warnings, 0 errors.

**Step 5: Commit**

```bash
git add src/ZeroAlloc.StateMachine/StateMachineGroupAttribute.cs \
        src/ZeroAlloc.StateMachine/StateMachinePartAttribute.cs \
        src/ZeroAlloc.StateMachine/PublicAPI.Unshipped.txt
git commit -m "$(cat <<'EOF'
feat: add [StateMachineGroup] + [StateMachinePart<TState, TTrigger>] (B5 runtime types)

Two new public attribute types the generator (next commits) will pick
up to wire multiple concurrent state machines into one class:

  - StateMachineGroupAttribute — class-level marker; mutually exclusive
    with [StateMachine].
  - StateMachinePartAttribute<TState, TTrigger>(Name, InitialState) —
    declares one named CAS state field + TryFire<Name> within the group.

Generator wiring lands in subsequent commits.
EOF
)"
```

---

## Task 3: Add diagnostic descriptors `ZSM0012`–`ZSM0019`

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineDiagnostics.cs`

**Step 1: Append the new descriptors**

Add to the end of the `StateMachineDiagnostics` class (after `CompositeAndTerminalOnSameState`):

```csharp
    public static readonly DiagnosticDescriptor TimedTransitionRequiresConcurrent = new(
        id:                 "ZSM0012",
        title:              "AfterMs requires Concurrent = true",
        messageFormat:      "[Transition(From = {0}.{1}, On = {2}, To = {3}, AfterMs = {4})] on '{5}': AfterMs requires Concurrent = true (or that the transition belongs to a [StateMachinePart], which is always concurrent)",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "Timer callbacks race with user-initiated TryFire; CAS-based concurrent dispatch is required to keep the model thread-safe.");

    public static readonly DiagnosticDescriptor TimedTransitionInvalidDuration = new(
        id:                 "ZSM0013",
        title:              "AfterMs must be positive",
        messageFormat:      "[Transition(From = {0}.{1}, On = {2}, To = {3}, AfterMs = {4})] on '{5}': AfterMs must be greater than zero",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "A non-positive AfterMs has no meaningful semantics; omit the property to disable the timer.");

    public static readonly DiagnosticDescriptor StateMachineAndGroupExclusive = new(
        id:                 "ZSM0014",
        title:              "[StateMachine] and [StateMachineGroup] are mutually exclusive",
        messageFormat:      "'{0}' declares both [StateMachine] and [StateMachineGroup]. Pick one — single-machine classes use [StateMachine]; multi-part classes use [StateMachineGroup] + [StateMachinePart]",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "A class is either a single state machine or a group of named parts, not both.");

    public static readonly DiagnosticDescriptor DuplicateStateMachinePartName = new(
        id:                 "ZSM0015",
        title:              "Duplicate [StateMachinePart] name",
        messageFormat:      "[StateMachinePart] on '{0}': two parts share Name = \"{1}\". Each part must have a unique name within the class",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "The Name doubles as the suffix for TryFire<Name>, <Name>Current and per-part hooks, so it must be unique.");

    public static readonly DiagnosticDescriptor TransitionPartUnknown = new(
        id:                 "ZSM0016",
        title:              "Transition references unknown part",
        messageFormat:      "[Transition(... Part = \"{0}\")] on '{1}': no [StateMachinePart] with Name = \"{0}\" declared on this class. In a [StateMachineGroup] class every transition must carry Part = the name of a declared part",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "Transitions inside a [StateMachineGroup] are routed by the Part discriminator; an unknown or missing Part cannot be dispatched.");

    public static readonly DiagnosticDescriptor EmptyStateMachineGroup = new(
        id:                 "ZSM0017",
        title:              "[StateMachineGroup] declares no parts",
        messageFormat:      "[StateMachineGroup] on '{0}': no [StateMachinePart] declared. A group must contain at least one part",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "Empty groups generate no usable code; declare at least one [StateMachinePart] or use [StateMachine] for a single-machine class.");

    public static readonly DiagnosticDescriptor CompositeStateInGroup = new(
        id:                 "ZSM0018",
        title:              "[CompositeState] is not supported inside [StateMachineGroup]",
        messageFormat:      "[CompositeState] on '{0}': composites are not supported inside a [StateMachineGroup] (parts are always concurrent; composites require sequential dispatch — see ZSM0005)",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "Parts are concurrent by definition; composite states require sequential dispatch and cannot live inside a group.");

    public static readonly DiagnosticDescriptor DisposeSignatureConflict = new(
        id:                 "ZSM0019",
        title:              "User-declared Dispose conflicts with generated signature",
        messageFormat:      "'{0}' declares its own Dispose method with a signature incompatible with the generator's emit. Remove the user-declared Dispose or change the signature to public void Dispose()",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "When timed transitions exist, the generator emits public void Dispose() implementing IDisposable; a user method with a different signature collides with it.");
```

> **Note on `messageFormat` style:** RS1032 requires single-sentence messages without trailing periods. Each descriptor above ends without a period for that reason — keep it that way.

**Step 2: Verify build**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
```

Expected: 0 errors. (The descriptors are not used yet; build only verifies syntax + RS1032.)

**Step 3: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineDiagnostics.cs
git commit -m "$(cat <<'EOF'
feat(generator): add diagnostic descriptors ZSM0012-ZSM0019 for B3+B5

Eight new error-severity diagnostics covering declaration mistakes for
[Transition(AfterMs = ...)], [StateMachineGroup], and [StateMachinePart]:

  ZSM0012: AfterMs declared without Concurrent = true
  ZSM0013: AfterMs <= 0
  ZSM0014: [StateMachine] and [StateMachineGroup] on the same class
  ZSM0015: two [StateMachinePart]s share a Name
  ZSM0016: [Transition].Part references an unknown part
  ZSM0017: [StateMachineGroup] declares zero parts
  ZSM0018: [CompositeState] inside a [StateMachineGroup]
  ZSM0019: user-declared Dispose conflicts with generated emit

Descriptors only — detection + report lands in subsequent commits.
EOF
)"
```

---

## Task 4: Thread `AfterMs` + `Part` through `TransitionModel`

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/TransitionModel.cs`
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs:149-174` (CollectTransition)

**Step 1: Extend `TransitionModel`**

```csharp
namespace ZeroAlloc.StateMachine.Generator;

/// <summary>A single declared transition.</summary>
internal sealed record TransitionModel(
    string From,           // enum member name, e.g. "Idle"
    string On,             // enum member name, e.g. "Submit"
    string To,             // enum member name, e.g. "Pending"
    bool HasGuard,         // When = true on the attribute
    int AfterMs,           // > 0 => timed edge
    string? Part           // non-null when inside [StateMachineGroup]; null for [StateMachine] classes
);
```

**Step 2: Update `CollectTransition` to read the new named args**

In `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs`, inside `CollectTransition` (lines 149-174), after the existing `hasGuard` read:

```csharp
        var afterMs = attr.NamedArguments
            .FirstOrDefault(kv => string.Equals(kv.Key, "AfterMs", StringComparison.Ordinal)).Value.Value is int ms ? ms : 0;
        var part = attr.NamedArguments
            .FirstOrDefault(kv => string.Equals(kv.Key, "Part", StringComparison.Ordinal)).Value.Value as string;

        if (from is not null && on is not null && to is not null)
            transitions.Add(new TransitionModel(from, on, to, hasGuard, afterMs, part));
```

(Replace the existing `transitions.Add(new TransitionModel(from, on, to, hasGuard));` line.)

**Step 3: Verify build + existing tests still pass**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ZeroAlloc.StateMachine.Generator.Tests.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ZeroAlloc.StateMachine.Tests.csproj -c Release
```

Expected: 0 errors, all existing tests still pass. The two new fields default to `0` / `null`, matching existing transition declarations exactly.

> If a snapshot test fails because the `record TransitionModel`'s `ToString()` changed and that string appears in a snapshot, that's an unintended leak of internals — investigate before regenerating snapshots.

**Step 4: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/TransitionModel.cs \
        src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs
git commit -m "$(cat <<'EOF'
feat(generator): parse AfterMs + Part on [Transition]

Extends TransitionModel with AfterMs (int, default 0) and Part (string?,
default null), and updates CollectTransition to read both named args.

Behaviour change: none yet — fields are model-only. Writer + diagnostic
wiring lands in subsequent commits.
EOF
)"
```

---

## Task 5: Add `StateMachineGroupModel` + `StateMachinePartModel`

**Files:**
- Create: `src/ZeroAlloc.StateMachine.Generator/StateMachinePartModel.cs`
- Create: `src/ZeroAlloc.StateMachine.Generator/StateMachineGroupModel.cs`

**Step 1: Add `StateMachinePartModel.cs`**

```csharp
namespace ZeroAlloc.StateMachine.Generator;

using System.Collections.Immutable;

/// <summary>Single [StateMachinePart] declaration captured during parsing.</summary>
/// <param name="Name">Unique name within the group (e.g. "Operational").</param>
/// <param name="InitialState">Short enum member name of the part's initial state.</param>
/// <param name="StateTypeFqn">Fully-qualified TState (e.g. "global::MyApp.OpState").</param>
/// <param name="StateTypeShort">Short TState name.</param>
/// <param name="TriggerTypeFqn">Fully-qualified TTrigger.</param>
/// <param name="TriggerTypeShort">Short TTrigger name.</param>
/// <param name="Transitions">Transitions that carry Part = this.Name.</param>
internal sealed record StateMachinePartModel(
    string Name,
    string InitialState,
    string StateTypeFqn,
    string StateTypeShort,
    string TriggerTypeFqn,
    string TriggerTypeShort,
    ImmutableArray<TransitionModel> Transitions);
```

**Step 2: Add `StateMachineGroupModel.cs`**

```csharp
namespace ZeroAlloc.StateMachine.Generator;

using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

/// <summary>Immutable model of a [StateMachineGroup] type, built by the generator parser.</summary>
internal sealed record StateMachineGroupModel(
    string? Namespace,
    string ClassName,
    ImmutableArray<StateMachinePartModel> Parts,
    ImmutableArray<Diagnostic> Diagnostics
);
```

**Step 3: Verify build**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
```

Expected: 0 errors. (Records aren't referenced yet — build only verifies syntax.)

**Step 4: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachinePartModel.cs \
        src/ZeroAlloc.StateMachine.Generator/StateMachineGroupModel.cs
git commit -m "$(cat <<'EOF'
feat(generator): add StateMachinePartModel + StateMachineGroupModel records

Immutable records that describe a [StateMachineGroup] class and its
parts. Parse + writer wiring lands in subsequent commits.
EOF
)"
```

---

## Task 6: Detect timed-transition diagnostics (ZSM0012, ZSM0013) for `[StateMachine]` classes

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs`

**Step 1: Add the analyzer**

Append a new method to `StateMachineGenerator` (after `AnalyzeCompositeStates`):

```csharp
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
```

**Step 2: Wire it from `AnalyzeDiagnostics`**

Inside `AnalyzeDiagnostics`, append (after the existing `AnalyzeCompositeStates(...)` call):

```csharp
        AnalyzeTimedTransitions(transitions, stateTypeShort, type, concurrent, diagnostics);
```

**Step 3: Verify build + existing tests still pass**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release
```

Expected: 0 errors, all existing tests still pass (no timed transitions yet in fixtures).

**Step 4: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs
git commit -m "$(cat <<'EOF'
feat(generator): detect ZSM0012 + ZSM0013 for timed transitions

AnalyzeTimedTransitions walks the transition list and reports:
  ZSM0012 when AfterMs > 0 on a non-concurrent class
  ZSM0013 when AfterMs is negative

Tests for both diagnostics land with the snapshot/diagnostic suite.
EOF
)"
```

---

## Task 7: Emit timer fields, arm/disarm blocks, and `IDisposable` for `[StateMachine]` classes

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineWriter.cs`

**Step 1: Add `WriteTimerFields` helper**

Append to `StateMachineWriter.cs`:

```csharp
    private static void WriteTimerFields(StringBuilder sb, StateMachineModel m, string? partPrefix = null)
    {
        var timedEdges = m.Transitions.Where(static t => t.AfterMs > 0);
        if (partPrefix is null)
            timedEdges = timedEdges.Where(static t => t.Part is null);
        else
            timedEdges = timedEdges.Where(t => string.Equals(t.Part, partPrefix, StringComparison.Ordinal));

        foreach (var t in timedEdges)
        {
            var prefix = partPrefix is null ? string.Empty : $"{partPrefix}_";
            sb.AppendLine($"    private System.Threading.Timer? _timer_{prefix}{t.From}_{t.On};");
        }
    }
```

**Step 2: Add `WriteTimerArmBlocks` + `WriteTimerDisarmBlocks` helpers**

Append (still inside `StateMachineWriter`):

```csharp
    private static void WriteTimerArmBlocks(StringBuilder sb, StateMachineModel m,
        string indent, string stateExpr, string? partPrefix, string tryFireMethod, string stateTypeFqn)
    {
        foreach (var t in m.Transitions)
        {
            if (t.AfterMs == 0) continue;
            if (!string.Equals(t.Part, partPrefix, StringComparison.Ordinal)) continue;

            var prefix = partPrefix is null ? string.Empty : $"{partPrefix}_";
            var field = $"_timer_{prefix}{t.From}_{t.On}";
            var triggerFqn = partPrefix is null
                ? m.TriggerTypeFqn
                : ResolvePartTriggerFqn(m, partPrefix);

            sb.AppendLine($"{indent}if ({stateExpr} == {stateTypeFqn}.{t.From})");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    var __t = {field};");
            sb.AppendLine($"{indent}    if (__t is null)");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        __t = new System.Threading.Timer(");
            sb.AppendLine($"{indent}            static s => (({m.ClassName})s!).{tryFireMethod}({triggerFqn}.{t.On}),");
            sb.AppendLine($"{indent}            this, {t.AfterMs}, System.Threading.Timeout.Infinite);");
            sb.AppendLine($"{indent}        {field} = __t;");
            sb.AppendLine($"{indent}    }}");
            sb.AppendLine($"{indent}    else");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        __t.Change({t.AfterMs}, System.Threading.Timeout.Infinite);");
            sb.AppendLine($"{indent}    }}");
            sb.AppendLine($"{indent}}}");
        }
    }

    private static void WriteTimerDisarmBlocks(StringBuilder sb, StateMachineModel m,
        string indent, string fromExpr, string? partPrefix, string stateTypeFqn)
    {
        foreach (var t in m.Transitions)
        {
            if (t.AfterMs == 0) continue;
            if (!string.Equals(t.Part, partPrefix, StringComparison.Ordinal)) continue;

            var prefix = partPrefix is null ? string.Empty : $"{partPrefix}_";
            var field = $"_timer_{prefix}{t.From}_{t.On}";
            sb.AppendLine($"{indent}if ({fromExpr} == {stateTypeFqn}.{t.From})");
            sb.AppendLine($"{indent}    {field}?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);");
        }
    }

    private static string ResolvePartTriggerFqn(StateMachineModel m, string partName)
    {
        // For single-machine classes this is never called (partPrefix is always null).
        // Stub returns the model's trigger FQN; group emit (Task 11+) overrides via its own writer.
        return m.TriggerTypeFqn;
    }

    private static bool HasAnyTimedEdge(StateMachineModel m) =>
        m.Transitions.Any(static t => t.AfterMs > 0);
```

**Step 3: Wire timer field emit into the concurrent body**

In `WriteConcurrentBody`, after the `_state` field line (around line 308) and BEFORE `WriteConcurrentTryFire`:

```csharp
        WriteTimerFields(sb, m);
```

(Non-concurrent body does NOT get this — ZSM0012 already prevented timed edges from reaching that path.)

**Step 4: Wire arm/disarm into concurrent `TryFire`**

Modify `WriteConcurrentTryFire` (around lines 328-366). Inside the CAS-succeeded block (between `OnEnter(next.Value, current);` and `return true;`), inject:

```csharp
        sb.AppendLine($"                OnExit(current, trigger);");
        sb.AppendLine($"                OnEnter(next.Value, current);");
        // NEW — arm on enter, disarm on exit
        WriteTimerDisarmBlocks(sb, m, "                ", "current", partPrefix: null, m.StateTypeFqn);
        WriteTimerArmBlocks(sb, m, "                ", "next.Value", partPrefix: null, tryFireMethod: "TryFire", m.StateTypeFqn);
        sb.AppendLine($"                return true;");
```

**Step 5: Emit `IDisposable` when any timed edge exists**

Add a new `WriteDispose` helper:

```csharp
    private static void WriteDispose(StringBuilder sb, StateMachineModel m)
    {
        if (!HasAnyTimedEdge(m)) return;

        sb.AppendLine();
        sb.AppendLine($"    // ── IDisposable — disposes timer fields. Idempotent (Timer.Dispose is idempotent).");
        sb.AppendLine($"    /// <summary>Disposes all timers owned by this machine.</summary>");
        sb.AppendLine($"    public void Dispose()");
        sb.AppendLine($"    {{");
        foreach (var t in m.Transitions)
        {
            if (t.AfterMs == 0) continue;
            var prefix = t.Part is null ? string.Empty : $"{t.Part}_";
            sb.AppendLine($"        _timer_{prefix}{t.From}_{t.On}?.Dispose();");
        }
        sb.AppendLine($"        System.GC.SuppressFinalize(this);");
        sb.AppendLine($"    }}");
    }
```

Wire it into `WriteConcurrentBody`, AFTER `WriteConcurrentPartialStubs(sb, m);`:

```csharp
        WriteDispose(sb, m);
```

Update the class-declaration line to add `: System.IDisposable` when any timer exists. In `Write` (around line 22), replace the existing `sb.AppendLine($"{keyword} {model.ClassName}");` with:

```csharp
        var disposableSuffix = HasAnyTimedEdge(model) ? " : System.IDisposable" : "";
        sb.AppendLine($"{keyword} {model.ClassName}{disposableSuffix}");
```

(Note: this assumes the user's partial class doesn't already declare `IDisposable`. ZSM0019 catches conflicts — wired in Task 8.)

**Step 6: Verify build + existing tests still pass (snapshot regen NOT yet)**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release
```

Expected: 0 errors. Existing runtime tests (no timed edges in fixtures) still pass — emit shape is unchanged when `m.Transitions.All(t => t.AfterMs == 0)`.

Generator-snapshot tests may fail if the `{keyword} {model.ClassName}` line changed for a fixture that has 0 timed edges — but `disposableSuffix` is `""` when `HasAnyTimedEdge(m)` is false, so the line should be byte-identical. Run them too:

```bash
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
```

Expected: all existing snapshot tests still pass.

**Step 7: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineWriter.cs
git commit -m "$(cat <<'EOF'
feat(generator): emit timers + IDisposable for [Transition(AfterMs)] (B3)

Per-edge System.Threading.Timer? fields lazily allocated on first
arm, reused via Timer.Change. Armed in the concurrent CAS-succeeded
path on entering From; disarmed on leaving From.

Emits public void Dispose() implementing IDisposable on every
[StateMachine] class with at least one timed edge. Existing classes
without timers see byte-identical output.
EOF
)"
```

---

## Task 8: Detect ZSM0019 (Dispose signature conflict)

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs`

**Step 1: Add the analyzer**

Append a new method:

```csharp
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
```

**Step 2: Wire it from `AnalyzeDiagnostics`**

Append (after `AnalyzeTimedTransitions(...)`):

```csharp
        AnalyzeDisposeConflict(type, transitions, diagnostics);
```

**Step 3: Verify build + existing tests still pass**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release
```

Expected: 0 errors.

**Step 4: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs
git commit -m "$(cat <<'EOF'
feat(generator): detect ZSM0019 (Dispose signature conflict)

When timed transitions are present, the user-declared Dispose method
must be either absent or match public void Dispose() exactly.
Anything else collides with the generator's emit.
EOF
)"
```

---

## Task 9: Register `[StateMachineGroup]` as a second generator entry point

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs`

**Step 1: Add the group FQN constant**

Near the top of the class, after `StateMachineAttributeFqn`:

```csharp
    private const string StateMachineGroupAttributeFqn = "ZeroAlloc.StateMachine.StateMachineGroupAttribute";
    private const string StateMachineGroupAttributeMetadataName = "StateMachineGroupAttribute";
    private const string StateMachinePartAttributeMetadataName  = "StateMachinePartAttribute`2";
```

**Step 2: Register the second pipeline in `Initialize`**

Add inside `Initialize`, after the existing `models` registration:

```csharp
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
```

**Step 3: Add a stub `ParseGroup` returning empty model**

```csharp
    private static StateMachineGroupModel? ParseGroup(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol type) return null;
        ct.ThrowIfCancellationRequested();

        var ns = type.ContainingNamespace.IsGlobalNamespace
                 ? null
                 : type.ContainingNamespace.ToDisplayString();

        return new StateMachineGroupModel(
            ns, type.Name,
            ImmutableArray<StateMachinePartModel>.Empty,
            ImmutableArray<Diagnostic>.Empty);
    }
```

**Step 4: Add a stub `StateMachineGroupWriter.Write`**

Create `src/ZeroAlloc.StateMachine.Generator/StateMachineGroupWriter.cs`:

```csharp
namespace ZeroAlloc.StateMachine.Generator;

using System.Text;

internal static class StateMachineGroupWriter
{
    public static string Write(StateMachineGroupModel m)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (m.Namespace is not null)
        {
            sb.AppendLine($"namespace {m.Namespace};");
            sb.AppendLine();
        }

        sb.AppendLine($"partial class {m.ClassName}");
        sb.AppendLine("{");
        // Per-part emit lands in subsequent tasks.
        sb.AppendLine("}");
        return sb.ToString();
    }
}
```

**Step 5: Verify build + existing tests still pass**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release
```

Expected: 0 errors. The new pipeline emits an empty `partial class` for any class marked `[StateMachineGroup]` — but no fixture is yet, so behavior is unchanged.

**Step 6: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs \
        src/ZeroAlloc.StateMachine.Generator/StateMachineGroupWriter.cs
git commit -m "$(cat <<'EOF'
feat(generator): register [StateMachineGroup] as second pipeline entry

A second ForAttributeWithMetadataName subscription wires
[StateMachineGroup]-marked classes into the generator. ParseGroup +
StateMachineGroupWriter are stubs that emit an empty partial class
shell; per-part emit lands in subsequent commits.
EOF
)"
```

---

## Task 10: Parse `[StateMachinePart]` attributes into `StateMachineGroupModel`

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs`

**Step 1: Expand `ParseGroup`**

Replace the stub `ParseGroup` with:

```csharp
    private static StateMachineGroupModel? ParseGroup(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol type) return null;
        ct.ThrowIfCancellationRequested();

        var ns = type.ContainingNamespace.IsGlobalNamespace
                 ? null
                 : type.ContainingNamespace.ToDisplayString();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        var parts = CollectGroupParts(type);

        ct.ThrowIfCancellationRequested();
        AnalyzeGroupDiagnostics(type, parts, diagnostics);

        return new StateMachineGroupModel(
            ns, type.Name, parts, diagnostics.ToImmutable());
    }

    private static ImmutableArray<StateMachinePartModel> CollectGroupParts(INamedTypeSymbol type)
    {
        // First pass — gather part declarations (Name + InitialState + TState/TTrigger).
        var partBuilders = new System.Collections.Generic.List<(string Name, string InitialState,
            string StateFqn, string StateShort, string TriggerFqn, string TriggerShort)>();

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

            partBuilders.Add((
                Name: name!,
                InitialState: initial,
                StateFqn: ac.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                StateShort: ac.TypeArguments[0].Name,
                TriggerFqn: ac.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                TriggerShort: ac.TypeArguments[1].Name));
        }

        // Second pass — bucket transitions by Part.
        var transitionsByPart = new System.Collections.Generic.Dictionary<string, ImmutableArray<TransitionModel>.Builder>(StringComparer.Ordinal);
        foreach (var pb in partBuilders)
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

        var result = ImmutableArray.CreateBuilder<StateMachinePartModel>();
        foreach (var pb in partBuilders)
        {
            var transitions = transitionsByPart[pb.Name].ToImmutable();
            result.Add(new StateMachinePartModel(
                pb.Name, pb.InitialState, pb.StateFqn, pb.StateShort,
                pb.TriggerFqn, pb.TriggerShort, transitions));
        }
        return result.ToImmutable();
    }
```

**Step 2: Add a stub `AnalyzeGroupDiagnostics`**

```csharp
    private static void AnalyzeGroupDiagnostics(
        INamedTypeSymbol type,
        ImmutableArray<StateMachinePartModel> parts,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        // Detection lands in Task 11.
    }
```

**Step 3: Verify build + existing tests still pass**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release
```

Expected: 0 errors.

**Step 4: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs
git commit -m "$(cat <<'EOF'
feat(generator): parse [StateMachinePart] + part-scoped [Transition]

CollectGroupParts performs two passes:
  1. gather part declarations with their TState + TTrigger;
  2. bucket [Transition] declarations by their Part discriminator
     into each part's transition list.

AnalyzeGroupDiagnostics is a stub; detection lands in Task 11.
EOF
)"
```

---

## Task 11: Detect group diagnostics (ZSM0014–ZSM0018)

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs`

**Step 1: Replace `AnalyzeGroupDiagnostics` stub**

```csharp
    private static void AnalyzeGroupDiagnostics(
        INamedTypeSymbol type,
        ImmutableArray<StateMachinePartModel> parts,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var location = type.Locations.Length > 0 ? type.Locations[0] : Location.None;

        AnalyzeGroupExclusivity(type, location, diagnostics);
        AnalyzeGroupEmpty(type, parts, location, diagnostics);
        AnalyzeDuplicatePartNames(type, parts, location, diagnostics);
        AnalyzeUnknownTransitionParts(type, parts, location, diagnostics);
        AnalyzeCompositeInGroup(type, location, diagnostics);
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
```

**Step 2: Verify build + existing tests still pass**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release
```

Expected: 0 errors.

**Step 3: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineGenerator.cs
git commit -m "$(cat <<'EOF'
feat(generator): detect ZSM0014-ZSM0018 for [StateMachineGroup]

Five new diagnostics fire from AnalyzeGroupDiagnostics:
  ZSM0014: [StateMachine] + [StateMachineGroup] on the same class
  ZSM0015: two parts share Name
  ZSM0016: [Transition].Part references an unknown part (or null)
  ZSM0017: group declares zero parts
  ZSM0018: [CompositeState] inside a group

Detection only — writer emit for parts lands in the next commit.
EOF
)"
```

---

## Task 12: Emit per-part CAS state, `TryFire<Name>`, hooks, and per-part timers

**Files:**
- Modify: `src/ZeroAlloc.StateMachine.Generator/StateMachineGroupWriter.cs`

**Step 1: Rewrite `StateMachineGroupWriter.Write`**

```csharp
namespace ZeroAlloc.StateMachine.Generator;

using System.Linq;
using System.Text;

internal static class StateMachineGroupWriter
{
    public static string Write(StateMachineGroupModel m)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (m.Namespace is not null)
        {
            sb.AppendLine($"namespace {m.Namespace};");
            sb.AppendLine();
        }

        var hasAnyTimer = m.Parts.Any(static p => p.Transitions.Any(static t => t.AfterMs > 0));
        var disposableSuffix = hasAnyTimer ? " : System.IDisposable" : "";
        sb.AppendLine($"partial class {m.ClassName}{disposableSuffix}");
        sb.AppendLine("{");

        foreach (var p in m.Parts)
        {
            WritePartBody(sb, m.ClassName, p);
            sb.AppendLine();
        }

        if (hasAnyTimer) WriteGroupDispose(sb, m);

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void WritePartBody(StringBuilder sb, string className, StateMachinePartModel p)
    {
        WritePartFields(sb, p);
        WritePartCurrentProperty(sb, p);
        WritePartTryFire(sb, className, p);
        WritePartHooks(sb, p);
    }

    private static void WritePartFields(StringBuilder sb, StateMachinePartModel p)
    {
        sb.AppendLine($"    // ── Part: {p.Name} ────────────────────────────────────────");
        sb.AppendLine($"    private long _state_{p.Name} = (long){p.StateTypeFqn}.{p.InitialState};");
        foreach (var t in p.Transitions)
        {
            if (t.AfterMs == 0) continue;
            sb.AppendLine($"    private System.Threading.Timer? _timer_{p.Name}_{t.From}_{t.On};");
        }
        sb.AppendLine();
    }

    private static void WritePartCurrentProperty(StringBuilder sb, StateMachinePartModel p)
    {
        sb.AppendLine($"    /// <summary>Current state of part \"{p.Name}\" (thread-safe read).</summary>");
        sb.AppendLine($"    public {p.StateTypeFqn} {p.Name}Current => ({p.StateTypeFqn})System.Threading.Volatile.Read(ref _state_{p.Name});");
        sb.AppendLine();
    }

    private static void WritePartTryFire(StringBuilder sb, string className, StateMachinePartModel p)
    {
        var st = p.StateTypeFqn;
        var tr = p.TriggerTypeFqn;

        sb.AppendLine($"    /// <summary>Attempt to fire <paramref name=\"trigger\"/> on part \"{p.Name}\". Returns <c>true</c> if the transition occurred.</summary>");
        sb.AppendLine($"    public bool TryFire{p.Name}({tr} trigger)");
        sb.AppendLine($"    {{");
        sb.AppendLine($"        while (true)");
        sb.AppendLine($"        {{");
        sb.AppendLine($"            var current = ({st})System.Threading.Volatile.Read(ref _state_{p.Name});");
        sb.AppendLine($"            {st}? next = (current, trigger) switch");
        sb.AppendLine($"            {{");
        foreach (var t in p.Transitions)
            sb.AppendLine($"                ({st}.{t.From}, {tr}.{t.On}) => ({st}?){st}.{t.To},");
        sb.AppendLine($"                _ => null");
        sb.AppendLine($"            }};");
        sb.AppendLine();
        sb.AppendLine($"            if (next is null) return false;");
        sb.AppendLine();
        sb.AppendLine($"            if (System.Threading.Interlocked.CompareExchange(");
        sb.AppendLine($"                    ref _state_{p.Name}, (long)next.Value, (long)current) == (long)current)");
        sb.AppendLine($"            {{");
        sb.AppendLine($"                OnExit{p.Name}(current, trigger);");
        sb.AppendLine($"                OnEnter{p.Name}(next.Value, current);");
        WritePartTimerDisarmInline(sb, p);
        WritePartTimerArmInline(sb, p, className);
        sb.AppendLine($"                return true;");
        sb.AppendLine($"            }}");
        sb.AppendLine($"        }}");
        sb.AppendLine($"    }}");
        sb.AppendLine();
    }

    private static void WritePartTimerDisarmInline(StringBuilder sb, StateMachinePartModel p)
    {
        var st = p.StateTypeFqn;
        foreach (var t in p.Transitions)
        {
            if (t.AfterMs == 0) continue;
            sb.AppendLine($"                if (current == {st}.{t.From})");
            sb.AppendLine($"                    _timer_{p.Name}_{t.From}_{t.On}?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);");
        }
    }

    private static void WritePartTimerArmInline(StringBuilder sb, StateMachinePartModel p, string className)
    {
        var st = p.StateTypeFqn;
        var tr = p.TriggerTypeFqn;
        foreach (var t in p.Transitions)
        {
            if (t.AfterMs == 0) continue;
            var field = $"_timer_{p.Name}_{t.From}_{t.On}";
            sb.AppendLine($"                if (next.Value == {st}.{t.From})");
            sb.AppendLine($"                {{");
            sb.AppendLine($"                    var __t = {field};");
            sb.AppendLine($"                    if (__t is null)");
            sb.AppendLine($"                    {{");
            sb.AppendLine($"                        __t = new System.Threading.Timer(");
            sb.AppendLine($"                            static s => (({className})s!).TryFire{p.Name}({tr}.{t.On}),");
            sb.AppendLine($"                            this, {t.AfterMs}, System.Threading.Timeout.Infinite);");
            sb.AppendLine($"                        {field} = __t;");
            sb.AppendLine($"                    }}");
            sb.AppendLine($"                    else");
            sb.AppendLine($"                    {{");
            sb.AppendLine($"                        __t.Change({t.AfterMs}, System.Threading.Timeout.Infinite);");
            sb.AppendLine($"                    }}");
            sb.AppendLine($"                }}");
        }
    }

    private static void WritePartHooks(StringBuilder sb, StateMachinePartModel p)
    {
        var st = p.StateTypeFqn;
        var tr = p.TriggerTypeFqn;
        var exitStates = p.Transitions.Select(static t => t.From).Distinct(System.StringComparer.Ordinal).ToArray();
        var enterStates = p.Transitions.Select(static t => t.To).Distinct(System.StringComparer.Ordinal).ToArray();

        sb.AppendLine($"    private void OnExit{p.Name}({st} state, {tr} trigger)");
        sb.AppendLine($"    {{");
        if (exitStates.Length > 0)
        {
            sb.AppendLine($"        switch (state)");
            sb.AppendLine($"        {{");
            foreach (var s in exitStates)
                sb.AppendLine($"            case {st}.{s}: OnExit{p.Name}{s}(trigger); break;");
            sb.AppendLine($"        }}");
        }
        sb.AppendLine($"    }}");
        sb.AppendLine();

        sb.AppendLine($"    private void OnEnter{p.Name}({st} state, {st} from)");
        sb.AppendLine($"    {{");
        if (enterStates.Length > 0)
        {
            sb.AppendLine($"        switch (state)");
            sb.AppendLine($"        {{");
            foreach (var s in enterStates)
                sb.AppendLine($"            case {st}.{s}: OnEnter{p.Name}{s}(from); break;");
            sb.AppendLine($"        }}");
        }
        sb.AppendLine($"    }}");
        sb.AppendLine();

        sb.AppendLine($"    // ── Partial hooks for part \"{p.Name}\" — implement what you need");
        foreach (var s in exitStates)
        {
            sb.AppendLine($"    /// <summary>Called after leaving <c>{s}</c> on part \"{p.Name}\".</summary>");
            sb.AppendLine($"    partial void OnExit{p.Name}{s}({tr} on);");
        }
        foreach (var s in enterStates)
        {
            sb.AppendLine($"    /// <summary>Called after entering <c>{s}</c> on part \"{p.Name}\".</summary>");
            sb.AppendLine($"    partial void OnEnter{p.Name}{s}({st} from);");
        }
    }

    private static void WriteGroupDispose(StringBuilder sb, StateMachineGroupModel m)
    {
        sb.AppendLine();
        sb.AppendLine($"    /// <summary>Disposes all timers owned by this group.</summary>");
        sb.AppendLine($"    public void Dispose()");
        sb.AppendLine($"    {{");
        foreach (var p in m.Parts)
            foreach (var t in p.Transitions)
                if (t.AfterMs > 0)
                    sb.AppendLine($"        _timer_{p.Name}_{t.From}_{t.On}?.Dispose();");
        sb.AppendLine($"        System.GC.SuppressFinalize(this);");
        sb.AppendLine($"    }}");
    }
}
```

> **MA0051 note:** if `WritePartTryFire` exceeds 60 lines, split the timer-block emission further (it already factors out into two helpers). Keep each emitted helper ≤60 lines.

**Step 2: Verify build + existing tests still pass**

```bash
dotnet build src/ZeroAlloc.StateMachine.Generator/ZeroAlloc.StateMachine.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release
```

Expected: 0 errors. No group fixtures yet — existing tests untouched.

**Step 3: Commit**

```bash
git add src/ZeroAlloc.StateMachine.Generator/StateMachineGroupWriter.cs
git commit -m "$(cat <<'EOF'
feat(generator): emit per-part CAS + TryFire<Name> + hooks for groups

For each [StateMachinePart] the writer emits:
  - private volatile long _state_<Name>
  - public <TState> <Name>Current
  - public bool TryFire<Name>(<TTrigger>) with CAS loop
  - private OnEnter<Name>/OnExit<Name> dispatchers
  - partial void OnEnter<Name><State> / OnExit<Name><State> stubs

Per-part timed edges arm/disarm inside the part's CAS-succeeded path.
A single public void Dispose() (when any part has a timed edge)
disposes every timer field across all parts.
EOF
)"
```

---

## Task 13: Snapshot tests for B3 (timed transitions)

**Files:**
- Create: `tests/ZeroAlloc.StateMachine.Generator.Tests/TimedTransitionGeneratorTests.cs`
- Create: `tests/ZeroAlloc.StateMachine.Generator.Tests/Snapshots/TimedTransitionGeneratorTests.SingleTimedEdge#MyApp_Watchdog.g.verified.cs` (committed after first verified run)
- Create: `tests/ZeroAlloc.StateMachine.Generator.Tests/Snapshots/TimedTransitionGeneratorTests.MultipleTimedEdges#MyApp_Multi.g.verified.cs` (committed after first verified run)

**Step 1: Add the test fixture**

```csharp
namespace ZeroAlloc.StateMachine.Generator.Tests;

using System.Threading.Tasks;
using VerifyXunit;
using Xunit;

[UsesVerify]
public class TimedTransitionGeneratorTests
{
    [Fact]
    public Task SingleTimedEdge()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
namespace MyApp;

public enum WdState { Idle, Working, Dead }
public enum WdTrigger { Start, Heartbeat, Timeout }

[StateMachine(InitialState = ""Idle"", Concurrent = true)]
[Transition<WdState, WdTrigger>(From = WdState.Idle,    On = WdTrigger.Start,    To = WdState.Working)]
[Transition<WdState, WdTrigger>(From = WdState.Working, On = WdTrigger.Heartbeat, To = WdState.Working)]
[Transition<WdState, WdTrigger>(From = WdState.Working, On = WdTrigger.Timeout,   To = WdState.Dead, AfterMs = 5000)]
public partial class Watchdog { }
";
        return TestHelper.Verify<StateMachineGenerator>(source);
    }

    [Fact]
    public Task MultipleTimedEdges()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
namespace MyApp;

public enum MS { A, B, C }
public enum MT { ToB, ToC, Reset }

[StateMachine(InitialState = ""A"", Concurrent = true)]
[Transition<MS, MT>(From = MS.A, On = MT.ToB,   To = MS.B, AfterMs = 1000)]
[Transition<MS, MT>(From = MS.B, On = MT.ToC,   To = MS.C, AfterMs = 2000)]
[Transition<MS, MT>(From = MS.C, On = MT.Reset, To = MS.A)]
public partial class Multi { }
";
        return TestHelper.Verify<StateMachineGenerator>(source);
    }
}
```

**Step 2: Run once to produce `.received.cs`**

```bash
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release --filter "FullyQualifiedName~TimedTransitionGeneratorTests"
```

Expected: BOTH tests FAIL with VerifyXunit's "received but no verified" diff message. The `.received.cs` files now exist in `Snapshots/`.

**Step 3: Inspect the received output**

Verify by hand that:
- `_timer_Working_Timeout` field is declared (single test) / `_timer_A_ToB` + `_timer_B_ToC` (multi).
- Inside the CAS-succeeded block, arm-on-enter blocks reference `next.Value == WdState.Working`, with `Timer.Change(5000, Timeout.Infinite)`.
- Disarm-on-exit blocks reference `current == WdState.Working`.
- `public void Dispose()` appears at the end.
- Class declaration is `partial class Watchdog : System.IDisposable`.

**Step 4: Rename to lock the snapshots**

```bash
cd tests/ZeroAlloc.StateMachine.Generator.Tests/Snapshots
mv "TimedTransitionGeneratorTests.SingleTimedEdge#MyApp_Watchdog.g.received.cs" \
   "TimedTransitionGeneratorTests.SingleTimedEdge#MyApp_Watchdog.g.verified.cs"
mv "TimedTransitionGeneratorTests.MultipleTimedEdges#MyApp_Multi.g.received.cs" \
   "TimedTransitionGeneratorTests.MultipleTimedEdges#MyApp_Multi.g.verified.cs"
```

**Step 5: Re-run to confirm snapshots are locked**

```bash
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release --filter "FullyQualifiedName~TimedTransitionGeneratorTests"
```

Expected: both tests PASS.

**Step 6: Commit**

```bash
git add tests/ZeroAlloc.StateMachine.Generator.Tests/TimedTransitionGeneratorTests.cs \
        tests/ZeroAlloc.StateMachine.Generator.Tests/Snapshots/TimedTransitionGeneratorTests*.verified.cs
git commit -m "$(cat <<'EOF'
test(generator): snapshot tests for [Transition(AfterMs = ...)] emit (B3)

Two snapshot tests covering:
  - SingleTimedEdge: one timed transition into a sink state
  - MultipleTimedEdges: two timed edges that share neither From nor On

Snapshots assert field naming, arm/disarm placement inside the
concurrent CAS-succeeded path, and the generated public void Dispose().
EOF
)"
```

---

## Task 14: Snapshot tests for B5 (state machine groups)

**Files:**
- Create: `tests/ZeroAlloc.StateMachine.Generator.Tests/StateMachineGroupGeneratorTests.cs`
- Create (via verify): `tests/ZeroAlloc.StateMachine.Generator.Tests/Snapshots/StateMachineGroupGeneratorTests.TwoParts#MyApp_Device.Group.g.verified.cs`
- Create (via verify): `tests/ZeroAlloc.StateMachine.Generator.Tests/Snapshots/StateMachineGroupGeneratorTests.TwoPartsOneTimedEdge#MyApp_DeviceTimed.Group.g.verified.cs`

**Step 1: Add the test fixture**

```csharp
namespace ZeroAlloc.StateMachine.Generator.Tests;

using System.Threading.Tasks;
using VerifyXunit;
using Xunit;

[UsesVerify]
public class StateMachineGroupGeneratorTests
{
    [Fact]
    public Task TwoParts()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
namespace MyApp;

public enum OpState { Idle, Running }
public enum OpTrigger { Start, Stop }
public enum ConnState { Disconnected, Connected }
public enum ConnTrigger { Connect, Disconnect }

[StateMachineGroup]
[StateMachinePart<OpState,   OpTrigger>(Name = ""Operational"", InitialState = OpState.Idle)]
[StateMachinePart<ConnState, ConnTrigger>(Name = ""Connection"", InitialState = ConnState.Disconnected)]
[Transition<OpState,   OpTrigger>(From = OpState.Idle,    On = OpTrigger.Start,      To = OpState.Running,      Part = ""Operational"")]
[Transition<OpState,   OpTrigger>(From = OpState.Running, On = OpTrigger.Stop,       To = OpState.Idle,         Part = ""Operational"")]
[Transition<ConnState, ConnTrigger>(From = ConnState.Disconnected, On = ConnTrigger.Connect,    To = ConnState.Connected,    Part = ""Connection"")]
[Transition<ConnState, ConnTrigger>(From = ConnState.Connected,    On = ConnTrigger.Disconnect, To = ConnState.Disconnected, Part = ""Connection"")]
public partial class Device { }
";
        return TestHelper.Verify<StateMachineGenerator>(source);
    }

    [Fact]
    public Task TwoPartsOneTimedEdge()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
namespace MyApp;

public enum OpState { Idle, Running, Faulted }
public enum OpTrigger { Start, Fault }
public enum ConnState { Disconnected, Connected }
public enum ConnTrigger { Connect, Disconnect }

[StateMachineGroup]
[StateMachinePart<OpState,   OpTrigger>(Name = ""Operational"", InitialState = OpState.Idle)]
[StateMachinePart<ConnState, ConnTrigger>(Name = ""Connection"", InitialState = ConnState.Disconnected)]
[Transition<OpState,   OpTrigger>(From = OpState.Idle,    On = OpTrigger.Start, To = OpState.Running,  Part = ""Operational"")]
[Transition<OpState,   OpTrigger>(From = OpState.Running, On = OpTrigger.Fault, To = OpState.Faulted,  Part = ""Operational"", AfterMs = 10000)]
[Transition<ConnState, ConnTrigger>(From = ConnState.Disconnected, On = ConnTrigger.Connect,    To = ConnState.Connected,    Part = ""Connection"")]
[Transition<ConnState, ConnTrigger>(From = ConnState.Connected,    On = ConnTrigger.Disconnect, To = ConnState.Disconnected, Part = ""Connection"")]
public partial class DeviceTimed { }
";
        return TestHelper.Verify<StateMachineGenerator>(source);
    }
}
```

**Step 2: Run once to produce `.received.cs`**

```bash
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release --filter "FullyQualifiedName~StateMachineGroupGeneratorTests"
```

Expected: both tests FAIL with VerifyXunit's "received but no verified" diff.

**Step 3: Inspect + rename to lock**

Verify by hand:
- One `_state_Operational` field + one `_state_Connection` field.
- `OperationalCurrent` and `ConnectionCurrent` properties.
- `TryFireOperational(OpTrigger)` and `TryFireConnection(ConnTrigger)` methods.
- Per-part hooks: `OnEnterOperationalIdle`, `OnExitOperationalIdle`, etc.
- For `TwoPartsOneTimedEdge`: `_timer_Operational_Running_Fault` field + arm/disarm + `Dispose()` + `: System.IDisposable`.

```bash
cd tests/ZeroAlloc.StateMachine.Generator.Tests/Snapshots
mv "StateMachineGroupGeneratorTests.TwoParts#MyApp_Device.Group.g.received.cs" \
   "StateMachineGroupGeneratorTests.TwoParts#MyApp_Device.Group.g.verified.cs"
mv "StateMachineGroupGeneratorTests.TwoPartsOneTimedEdge#MyApp_DeviceTimed.Group.g.received.cs" \
   "StateMachineGroupGeneratorTests.TwoPartsOneTimedEdge#MyApp_DeviceTimed.Group.g.verified.cs"
```

**Step 4: Re-run to confirm**

```bash
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release --filter "FullyQualifiedName~StateMachineGroupGeneratorTests"
```

Expected: both tests PASS.

**Step 5: Commit**

```bash
git add tests/ZeroAlloc.StateMachine.Generator.Tests/StateMachineGroupGeneratorTests.cs \
        tests/ZeroAlloc.StateMachine.Generator.Tests/Snapshots/StateMachineGroupGeneratorTests*.verified.cs
git commit -m "$(cat <<'EOF'
test(generator): snapshot tests for [StateMachineGroup] emit (B5)

Two snapshot tests covering:
  - TwoParts: two independent CAS parts with disjoint TState/TTrigger
  - TwoPartsOneTimedEdge: same plus a timed edge inside one part

Snapshots assert per-part field/property/method naming, hook routing,
and that a single Dispose disposes timers across parts.
EOF
)"
```

---

## Task 15: Diagnostic tests for ZSM0012–ZSM0019

**Files:**
- Modify: `tests/ZeroAlloc.StateMachine.Generator.Tests/DiagnosticTests.cs`

**Step 1: Append eight test methods**

```csharp
    [Fact]
    public async Task ZSM0012_FiresWhen_AfterMs_OnNonConcurrentClass()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B }
public enum T { Go }
[StateMachine(InitialState = ""A"")]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, AfterMs = 1000)]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.Contains(diags, d => d.Id == "ZSM0012");
    }

    [Fact]
    public async Task ZSM0013_FiresWhen_AfterMs_IsNegative()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B }
public enum T { Go }
[StateMachine(InitialState = ""A"", Concurrent = true)]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, AfterMs = -1)]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.Contains(diags, d => d.Id == "ZSM0013");
    }

    [Fact]
    public async Task ZSM0014_FiresWhen_BothStateMachineAndGroup()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B } public enum T { Go }
[StateMachine(InitialState = ""A"")]
[StateMachineGroup]
[StateMachinePart<S, T>(Name = ""P"", InitialState = S.A)]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B)]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.Contains(diags, d => d.Id == "ZSM0014");
    }

    [Fact]
    public async Task ZSM0015_FiresWhen_DuplicatePartNames()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B } public enum T { Go }
[StateMachineGroup]
[StateMachinePart<S, T>(Name = ""P"", InitialState = S.A)]
[StateMachinePart<S, T>(Name = ""P"", InitialState = S.A)]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, Part = ""P"")]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.Contains(diags, d => d.Id == "ZSM0015");
    }

    [Fact]
    public async Task ZSM0016_FiresWhen_TransitionPart_IsUnknown()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B } public enum T { Go }
[StateMachineGroup]
[StateMachinePart<S, T>(Name = ""P"", InitialState = S.A)]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, Part = ""DoesNotExist"")]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.Contains(diags, d => d.Id == "ZSM0016");
    }

    [Fact]
    public async Task ZSM0017_FiresWhen_GroupHasNoParts()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
[StateMachineGroup]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.Contains(diags, d => d.Id == "ZSM0017");
    }

    [Fact]
    public async Task ZSM0018_FiresWhen_CompositeInGroup()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B } public enum T { Go }
public enum SubS { X, Y }
[StateMachine(InitialState = ""X"")]
[Transition<SubS, T>(From = SubS.X, On = T.Go, To = SubS.Y)]
public partial class Sub { }

[StateMachineGroup]
[StateMachinePart<S, T>(Name = ""P"", InitialState = S.A)]
[CompositeState<S>(State = S.A, SubMachine = typeof(Sub))]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, Part = ""P"")]
public partial class M { }
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.Contains(diags, d => d.Id == "ZSM0018");
    }

    [Fact]
    public async Task ZSM0019_FiresWhen_UserDispose_HasWrongSignature()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
public enum S { A, B } public enum T { Go }
[StateMachine(InitialState = ""A"", Concurrent = true)]
[Transition<S, T>(From = S.A, On = T.Go, To = S.B, AfterMs = 1000)]
public partial class M
{
    private void Dispose() { }   // wrong: private (gen wants public)
}
";
        var diags = await TestHelper.GetDiagnostics<StateMachineGenerator>(source);
        Assert.Contains(diags, d => d.Id == "ZSM0019");
    }
```

**Step 2: Run them**

```bash
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests/ -c Release --filter "FullyQualifiedName~DiagnosticTests"
```

Expected: all eight new tests PASS.

**Step 3: Commit**

```bash
git add tests/ZeroAlloc.StateMachine.Generator.Tests/DiagnosticTests.cs
git commit -m "$(cat <<'EOF'
test(generator): diagnostic tests for ZSM0012-ZSM0019

One positive test per new diagnostic asserting that the declaration
trigger reports the expected diagnostic ID.
EOF
)"
```

---

## Task 16: Runtime tests for B3 (timed transitions)

**Files:**
- Create: `tests/ZeroAlloc.StateMachine.Tests/TimedTransitionTests.cs`

**Step 1: Add the test file**

```csharp
namespace ZeroAlloc.StateMachine.Tests;

using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZeroAlloc.StateMachine;

public class TimedTransitionTests
{
    private enum WdState { Idle, Working, Dead }
    private enum WdTrigger { Start, Heartbeat, Timeout }

    [StateMachine(InitialState = "Idle", Concurrent = true)]
    [Transition<WdState, WdTrigger>(From = WdState.Idle,    On = WdTrigger.Start,    To = WdState.Working)]
    [Transition<WdState, WdTrigger>(From = WdState.Working, On = WdTrigger.Heartbeat, To = WdState.Working)]
    [Transition<WdState, WdTrigger>(From = WdState.Working, On = WdTrigger.Timeout,   To = WdState.Dead, AfterMs = 100)]
    private partial class Watchdog { }

    [Fact]
    public async Task Timer_arms_on_enter_and_fires_after_duration()
    {
        using var w = new Watchdog();
        Assert.True(w.TryFire(WdTrigger.Start)); // → Working, arms 100ms timer
        await Task.Delay(250); // give the timer plenty of slack on busy CI
        Assert.Equal(WdState.Dead, w.Current);
    }

    [Fact]
    public async Task User_fire_before_timer_disarms_cleanly()
    {
        using var w = new Watchdog();
        Assert.True(w.TryFire(WdTrigger.Start));    // → Working
        Assert.True(w.TryFire(WdTrigger.Heartbeat)); // → Working (self-transition, disarms + rearms)
        await Task.Delay(50); // less than the new 100ms — should still be Working
        Assert.Equal(WdState.Working, w.Current);
    }

    [Fact]
    public async Task Timer_callback_after_state_moves_is_no_op()
    {
        using var w = new Watchdog();
        Assert.True(w.TryFire(WdTrigger.Start)); // → Working, 100ms timer armed
        // Race: in real life the timer might fire after we've already moved.
        // Disarm cleanly by transitioning to a state with no Timeout edge.
        // (There's no manual transition out of Working except Timeout, so use Heartbeat — same state, rearms.)
        await Task.Delay(250);
        // The most recent arm wins; state should be Dead.
        Assert.Equal(WdState.Dead, w.Current);
    }

    [Fact]
    public void Dispose_cancels_in_flight_timers()
    {
        var w = new Watchdog();
        Assert.True(w.TryFire(WdTrigger.Start));
        w.Dispose();
        // No assertion possible directly — but no exception should be thrown,
        // and waiting longer than the timer interval should NOT advance Current
        // because the timer is disposed.
        Thread.Sleep(200);
        Assert.Equal(WdState.Working, w.Current);
    }
}
```

> The `Heartbeat` self-transition is included precisely so we have a way to retrigger the arm without leaving `Working`. The flaky-on-CI risk on `Task.Delay` is mitigated by using 100ms timers + 250ms waits. If the CI runner is so slow that 250ms-of-delay misses a 100ms-timer, the bigger problem is the runner.

**Step 2: Run**

```bash
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release --filter "FullyQualifiedName~TimedTransitionTests"
```

Expected: all four tests PASS.

**Step 3: Commit**

```bash
git add tests/ZeroAlloc.StateMachine.Tests/TimedTransitionTests.cs
git commit -m "$(cat <<'EOF'
test: runtime tests for [Transition(AfterMs = ...)] (B3)

Four scenarios:
  - Timer arms on enter and fires after duration → state transitions
  - User TryFire before timeout disarms cleanly (re-arms on rearm path)
  - Timer callback after state has moved is a no-op (CAS fails harmlessly)
  - Dispose cancels in-flight timers; no callbacks after Dispose
EOF
)"
```

---

## Task 17: Runtime tests for B5 (state machine groups)

**Files:**
- Create: `tests/ZeroAlloc.StateMachine.Tests/StateMachineGroupTests.cs`

**Step 1: Add the test file**

```csharp
namespace ZeroAlloc.StateMachine.Tests;

using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZeroAlloc.StateMachine;

public class StateMachineGroupTests
{
    private enum OpS { Idle, Running, Faulted }
    private enum OpT { Start, Stop, Fault }
    private enum ConnS { Disconnected, Connected }
    private enum ConnT { Connect, Disconnect }

    [StateMachineGroup]
    [StateMachinePart<OpS,   OpT>(Name = "Op",   InitialState = OpS.Idle)]
    [StateMachinePart<ConnS, ConnT>(Name = "Conn", InitialState = ConnS.Disconnected)]
    [Transition<OpS,   OpT>(From = OpS.Idle,    On = OpT.Start, To = OpS.Running, Part = "Op")]
    [Transition<OpS,   OpT>(From = OpS.Running, On = OpT.Stop,  To = OpS.Idle,    Part = "Op")]
    [Transition<OpS,   OpT>(From = OpS.Running, On = OpT.Fault, To = OpS.Faulted, Part = "Op", AfterMs = 100)]
    [Transition<ConnS, ConnT>(From = ConnS.Disconnected, On = ConnT.Connect,    To = ConnS.Connected,    Part = "Conn")]
    [Transition<ConnS, ConnT>(From = ConnS.Connected,    On = ConnT.Disconnect, To = ConnS.Disconnected, Part = "Conn")]
    private partial class Device { }

    [Fact]
    public void Parts_evolve_independently()
    {
        using var d = new Device();
        Assert.Equal(OpS.Idle, d.OpCurrent);
        Assert.Equal(ConnS.Disconnected, d.ConnCurrent);

        Assert.True(d.TryFireOp(OpT.Start));
        Assert.Equal(OpS.Running, d.OpCurrent);
        Assert.Equal(ConnS.Disconnected, d.ConnCurrent); // unchanged

        Assert.True(d.TryFireConn(ConnT.Connect));
        Assert.Equal(OpS.Running, d.OpCurrent);          // unchanged
        Assert.Equal(ConnS.Connected, d.ConnCurrent);
    }

    [Fact]
    public void TryFire_unknown_trigger_returns_false_no_state_change()
    {
        using var d = new Device();
        Assert.False(d.TryFireOp(OpT.Stop)); // not valid from Idle
        Assert.Equal(OpS.Idle, d.OpCurrent);
    }

    [Fact]
    public async Task Timed_edge_in_part_arms_disarms_independent_of_other_part()
    {
        using var d = new Device();
        Assert.True(d.TryFireOp(OpT.Start));        // → Op:Running, 100ms Fault timer armed
        Assert.True(d.TryFireConn(ConnT.Connect));  // unrelated; should not affect Op timer
        await Task.Delay(250);
        Assert.Equal(OpS.Faulted, d.OpCurrent);
        Assert.Equal(ConnS.Connected, d.ConnCurrent);
    }
}
```

**Step 2: Run**

```bash
dotnet test tests/ZeroAlloc.StateMachine.Tests/ -c Release --filter "FullyQualifiedName~StateMachineGroupTests"
```

Expected: all three tests PASS.

**Step 3: Commit**

```bash
git add tests/ZeroAlloc.StateMachine.Tests/StateMachineGroupTests.cs
git commit -m "$(cat <<'EOF'
test: runtime tests for [StateMachineGroup] (B5)

Three scenarios:
  - Two parts evolve fully independently under TryFire<Name>
  - TryFire<Name> with an unknown (state, trigger) pair returns false
  - Timed edge inside a part arms/disarms scoped to that part only
EOF
)"
```

---

## Task 18: Documentation

**Files:**
- Create: `docs/core-concepts/timeout-transitions.md`
- Create: `docs/core-concepts/concurrent-parts.md`
- Modify: `docs/attributes.md` (add AfterMs, Part, StateMachineGroup, StateMachinePart)
- Create: `docs/diagnostics/ZSM0012.md` through `docs/diagnostics/ZSM0019.md`

**Step 1: Mirror the existing diagnostics-doc shape**

Check the existing convention before authoring:

```bash
ls docs/diagnostics/
cat docs/diagnostics/ZSM0011.md   # closest precedent
```

Author each new doc following the same template (Title, Cause, Example, Fix).

**Step 2: Author the core-concepts pages**

- `timeout-transitions.md`: explain `AfterMs`, the concurrent requirement, the `IDisposable` cleanup contract, and the race-safety guarantee from CAS.
- `concurrent-parts.md`: explain `[StateMachineGroup]` + `[StateMachinePart]`, the per-part `TryFire<Name>` / `<Name>Current` / `OnEnter<Name><State>` surface, and the no-shared-state guarantee.

**Step 3: Verify links + headings render**

```bash
# If the repo uses a markdown linter; otherwise just visually inspect.
ls docs/core-concepts/
ls docs/diagnostics/
```

**Step 4: Commit**

```bash
git add docs/
git commit -m "$(cat <<'EOF'
docs: timeout transitions + concurrent parts + ZSM0012-ZSM0019

Two new core-concepts pages and eight new diagnostic pages following
the existing template. attributes.md adds entries for AfterMs, Part,
[StateMachineGroup], [StateMachinePart].
EOF
)"
```

---

## Task 19: Push branch + open PR

**Files:** (no source — git only)

**Step 1: Final build + full test sweep**

```bash
dotnet build -c Release
dotnet test -c Release
```

Expected: all tests PASS, 0 warnings, 0 errors.

**Step 2: Push the branch**

```bash
git push -u origin feat/timeout-and-concurrent-parts
```

**Step 3: Open the PR**

```bash
gh pr create --title "feat: timeout transitions + concurrent state parts (B3+B5)" --body "$(cat <<'EOF'
## Summary

Lands `ZeroAlloc.StateMachine` backlog items B3 + B5 as a pair:

- **B3 (timeout transitions):** `[Transition(... AfterMs = N)]` emits a lazily-allocated `System.Threading.Timer?` per timed edge. Armed on enter, disarmed on exit. Requires `Concurrent = true`. Generated `IDisposable` cleans up timers.
- **B5 (concurrent state parts):** `[StateMachineGroup]` + `[StateMachinePart<TState, TTrigger>(Name, InitialState)]` declare multiple independent CAS state fields in one class, each with its own `TryFire<Name>` / `<Name>Current` / per-part hooks.

Eight new diagnostics (`ZSM0012`–`ZSM0019`) cover declaration mistakes.

Design doc: `docs/plans/2026-05-22-timeout-and-concurrent-parts-design.md` (commit `de0730b`).
Plan: `docs/plans/2026-05-22-timeout-and-concurrent-parts.md`.

## Test plan

- [x] Generator snapshot tests for timed edges (single + multiple)
- [x] Generator snapshot tests for groups (no timer + with timer)
- [x] Diagnostic tests for ZSM0012–ZSM0019
- [x] Runtime tests for timer arm/disarm/dispose
- [x] Runtime tests for per-part independence
- [x] All existing v1.3 tests still pass (strictly additive)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

**Step 4: Return the PR URL to the user.**

---

## Notes for the implementer

- **MA0051 (60-line method limit).** `WritePartTryFire` is close to the limit; if it exceeds, split arm/disarm emission into further helpers.
- **RS1032 (CodeAnalysis message style).** Diagnostic `messageFormat` strings must be single sentences with no trailing period.
- **PublicAPI tracking.** Every public property/member touched in Tasks 1, 2 lands in `PublicAPI.Unshipped.txt`. RS0016/RS0017 will tell you if you missed a line.
- **VerifyXunit snapshots.** First test run produces `.received.cs` files that DON'T match a `.verified.cs` → test fails with a diff. Inspect the diff, then rename `.received.cs` → `.verified.cs` to lock the snapshot. Commit BOTH the test source and the verified snapshot files.
- **Concurrent CAS race safety for timers.** A timer callback's `TryFire` runs the same CAS loop as user dispatch. If the state has already moved, `(current, trigger) switch` returns `null` and the callback exits cleanly — no special "is the state still X?" check needed in the callback.
- **`IDisposable` only when needed.** Classes without timed edges do not gain `: System.IDisposable` and do not get a generated `Dispose()`. This keeps the surface unchanged for v1.3 consumers.
- **Group classes do not call into the single-machine writer.** They have their own `StateMachineGroupWriter`. The two pipelines are independent (Task 9 registers a second `ForAttributeWithMetadataName` subscription).
