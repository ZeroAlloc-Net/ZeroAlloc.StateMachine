namespace ZeroAlloc.StateMachine.Generator;

/// <summary>Immutable model of a [StateMachineGroup] type, built by the generator parser.</summary>
internal sealed record StateMachineGroupModel(
    string? Namespace,
    // Null when the host cannot be generated into, as ZSM0023 and ZSM0024 report: then nothing
    // is emitted for it, whatever its other diagnostics.
    HostDeclaration? Declaration,
    string DisplayName,       // e.g. "MyApp.Outer.OrderMachine", for diagnostics
    LocationInfo? HostLocation, // the host's name, where diagnostics about the host itself go
    string HintName,
    EquatableArray<StateMachinePartModel> Parts,
    bool HasUserCtor,
    bool Diagram,
    EquatableArray<DiagnosticInfo> Diagnostics
);
