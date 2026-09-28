namespace ZeroAlloc.StateMachine.Generator;

/// <summary>
/// A sub-machine a composite state's diagram expands, resolved while parsing so emitting needs
/// no compilation.
/// </summary>
internal sealed record SubMachineModel(string Fqn, StateMachineModel Model);
