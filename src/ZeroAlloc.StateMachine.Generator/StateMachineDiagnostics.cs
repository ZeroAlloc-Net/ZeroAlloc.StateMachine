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
}
