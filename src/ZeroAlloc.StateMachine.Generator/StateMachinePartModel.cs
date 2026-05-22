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
