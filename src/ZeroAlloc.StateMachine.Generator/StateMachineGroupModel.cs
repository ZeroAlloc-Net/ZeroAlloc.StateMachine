namespace ZeroAlloc.StateMachine.Generator;

using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

/// <summary>Immutable model of a [StateMachineGroup] type, built by the generator parser.</summary>
internal sealed record StateMachineGroupModel(
    string? Namespace,
    string ClassName,
    ImmutableArray<StateMachinePartModel> Parts,
    bool Diagram,
    ImmutableArray<Diagnostic> Diagnostics
);
