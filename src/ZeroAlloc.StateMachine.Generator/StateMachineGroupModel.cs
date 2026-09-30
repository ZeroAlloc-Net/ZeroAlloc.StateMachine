namespace ZeroAlloc.StateMachine.Generator;

/// <summary>Immutable model of a [StateMachineGroup] type, built by the generator parser.</summary>
internal sealed record StateMachineGroupModel(
    string? Namespace,
    string ClassName,
    string HintName,
    EquatableArray<StateMachinePartModel> Parts,
    bool HasUserCtor,
    bool Diagram,
    EquatableArray<DiagnosticInfo> Diagnostics
);
