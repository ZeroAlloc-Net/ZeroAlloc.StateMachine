namespace ZeroAlloc.StateMachine;

/// <summary>
/// Marks a state as an intentional sink — a state with no outgoing transitions.
/// Silences diagnostic <c>ZSM0002</c> for that state.
/// </summary>
/// <typeparam name="TState">The state enum type.</typeparam>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public sealed class TerminalAttribute<TState> : Attribute
    where TState : struct, Enum
{
    /// <summary>The terminal state value.</summary>
    public required TState State { get; init; }
}
