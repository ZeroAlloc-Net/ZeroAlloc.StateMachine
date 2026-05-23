namespace ZeroAlloc.StateMachine.Generator;

using Microsoft.CodeAnalysis;

internal static class StateMachineDiagnostics
{
    public static readonly DiagnosticDescriptor UnreachableState = new(
        id:                 "ZSM0001",
        title:              "Unreachable state",
        messageFormat:      "State '{0}' on '{1}' is unreachable: no transition leads to it and it is not the initial state",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:        "Add a transition that leads to this state, or remove it from the enum.");

    public static readonly DiagnosticDescriptor SinkState = new(
        id:                 "ZSM0002",
        title:              "Unintentional sink state",
        messageFormat:      "State '{0}' on '{1}' has no outgoing transitions. Annotate with [Terminal<{2}>(State = {2}.{0})] if intentional.",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:        "Add a [Terminal] annotation or add outgoing transitions.");

    public static readonly DiagnosticDescriptor SingleUseTrigger = new(
        id:                 "ZSM0003",
        title:              "Single-use trigger",
        messageFormat:      "Trigger '{0}' on '{1}' appears in only one transition. Verify this is not a typo.",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:        "If intentional, suppress with #pragma warning disable ZSM0003.");

    public static readonly DiagnosticDescriptor StructConcurrentNotSupported = new(
        id:                 "ZSM0004",
        title:              "Concurrent mode not supported on structs",
        messageFormat:      "'{0}' is a struct with Concurrent = true. Concurrent mode requires a class. Change to a class or remove Concurrent = true.",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "Change the type to a class, or set Concurrent = false.");

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
        messageFormat:      "[CompositeState(State = {0}.{1})] on '{2}': SubMachine type '{3}' is not a [StateMachine] partial class",
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
        messageFormat:      "[CompositeState(State = {0})] on '{1}': '{0}' is not a member of the parent's state enum '{2}'",
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
        messageFormat:      "'{0}' declares both [StateMachine] and [StateMachineGroup]. Pick one: single-machine classes use [StateMachine]; multi-part classes use [StateMachineGroup] + [StateMachinePart].",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "A class is either a single state machine or a group of named parts, not both.");

    public static readonly DiagnosticDescriptor DuplicateStateMachinePartName = new(
        id:                 "ZSM0015",
        title:              "Duplicate [StateMachinePart] name",
        messageFormat:      "[StateMachinePart] on '{0}': two parts share Name = \"{1}\". Each part must have a unique name within the class.",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "The Name doubles as the suffix for TryFire<Name>, <Name>Current and per-part hooks, so it must be unique.");

    public static readonly DiagnosticDescriptor TransitionPartUnknown = new(
        id:                 "ZSM0016",
        title:              "Transition references unknown part",
        messageFormat:      "[Transition(... Part = \"{0}\")] on '{1}': no [StateMachinePart] with Name = \"{0}\" declared on this class. In a [StateMachineGroup] class every transition must carry Part = the name of a declared part.",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "Transitions inside a [StateMachineGroup] are routed by the Part discriminator; an unknown or missing Part cannot be dispatched.");

    public static readonly DiagnosticDescriptor EmptyStateMachineGroup = new(
        id:                 "ZSM0017",
        title:              "[StateMachineGroup] declares no parts",
        messageFormat:      "[StateMachineGroup] on '{0}': no [StateMachinePart] declared. A group must contain at least one part.",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "Empty groups generate no usable code; declare at least one [StateMachinePart] or use [StateMachine] for a single-machine class.");

    public static readonly DiagnosticDescriptor CompositeStateInGroup = new(
        id:                 "ZSM0018",
        title:              "[CompositeState] is not supported inside [StateMachineGroup]",
        messageFormat:      "[CompositeState] on '{0}': composites are not supported inside a [StateMachineGroup] (parts are always concurrent; composites require sequential dispatch)",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "Parts are concurrent by definition; composite states require sequential dispatch and cannot live inside a group.");

    public static readonly DiagnosticDescriptor DisposeSignatureConflict = new(
        id:                 "ZSM0019",
        title:              "User-declared Dispose conflicts with generated signature",
        messageFormat:      "'{0}' declares its own Dispose method with a signature incompatible with the generator's emit. Remove the user-declared Dispose or change the signature to public void Dispose().",
        category:           "ZeroAlloc.StateMachine",
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "When timed transitions exist, the generator emits public void Dispose() implementing IDisposable; a user method with a different signature collides with it.");
}
