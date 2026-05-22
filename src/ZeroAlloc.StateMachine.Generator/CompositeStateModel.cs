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
