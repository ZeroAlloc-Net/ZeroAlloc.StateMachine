namespace ZeroAlloc.StateMachine;

using System;

/// <summary>
/// Declares shallow history on a composite state. When the composite is re-entered after
/// having been previously exited, the sub-machine resumes at its last leaf state (the state
/// it was in at the moment of exit) instead of resetting to its declared initial state.
/// </summary>
/// <typeparam name="TState">The state enum type of the enclosing state machine.</typeparam>
/// <remarks>
/// Must accompany a <c>[CompositeState(State = X)]</c> on the same class — declaring
/// <c>[HistoryState(State = X)]</c> alone emits <c>ZSM0010</c>. History is shallow only:
/// nested sub-machines are always reset to their initial state when their containing
/// sub-machine is restored.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public sealed class HistoryStateAttribute<TState> : Attribute
    where TState : struct, Enum
{
    /// <summary>Composite state that should remember its sub-machine's last leaf state across exits and re-entries.</summary>
    public required TState State { get; init; }
}
