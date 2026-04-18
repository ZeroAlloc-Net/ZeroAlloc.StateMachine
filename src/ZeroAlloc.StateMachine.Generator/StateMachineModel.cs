namespace ZeroAlloc.StateMachine.Generator;

using Microsoft.CodeAnalysis;
using System.Collections.Generic;

/// <summary>Immutable model of a [StateMachine] type, built by the generator parser.</summary>
internal sealed record StateMachineModel(
    string? Namespace,
    string ClassName,
    bool IsStruct,
    string InitialState,
    bool Concurrent,
    string StateTypeFqn,       // e.g. "global::MyApp.OrderState"
    string StateTypeShort,     // e.g. "OrderState"
    string TriggerTypeFqn,     // e.g. "global::MyApp.OrderTrigger"
    string TriggerTypeShort,   // e.g. "OrderTrigger"
    IReadOnlyList<TransitionModel> Transitions,
    IReadOnlyList<string> TerminalStates,    // short enum member names
    IReadOnlyList<Diagnostic> Diagnostics
);

