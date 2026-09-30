namespace ZeroAlloc.StateMachine.Generator;

/// <summary>Immutable model of a [StateMachine] type, built by the generator parser.</summary>
internal sealed record StateMachineModel(
    string? Namespace,
    // Null when the host cannot be generated into, as ZSM0023 and ZSM0024 report: then nothing
    // is emitted for it, whatever its other diagnostics.
    HostDeclaration? Declaration,
    string DisplayName,       // e.g. "MyApp.Outer.OrderMachine", for diagnostics
    LocationInfo? HostLocation, // the host's name, where diagnostics about the host itself go
    string HintName,          // the generated file's name, see HintNames.ForHost
    bool IsStruct,
    string InitialState,
    bool Concurrent,
    string StateTypeFqn,       // e.g. "global::MyApp.OrderState"
    string StateTypeShort,     // e.g. "OrderState"
    string TriggerTypeFqn,     // e.g. "global::MyApp.OrderTrigger"
    string TriggerTypeShort,   // e.g. "OrderTrigger"
    EquatableArray<TransitionModel> Transitions,
    EquatableArray<string> TerminalStates,    // short enum member names
    EquatableArray<CompositeStateModel> CompositeStates,
    EquatableArray<HistoryStateModel> HistoryStates,
    bool HasUserCtor,
    bool HasUserParameterlessCtor,
    bool HasPrimaryCtor,       // declared with a parameter list, as a positional record or `struct M(int x)`
    bool Diagram,
    EquatableArray<SubMachineModel> SubMachines,  // every sub-machine the diagram expands, transitively
    EquatableArray<DiagnosticInfo> Diagnostics
);

