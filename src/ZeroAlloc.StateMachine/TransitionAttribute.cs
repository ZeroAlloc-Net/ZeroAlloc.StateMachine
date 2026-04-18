namespace ZeroAlloc.StateMachine;

/// <summary>
/// Declares a single state machine transition. Stack multiple on the same type.
/// </summary>
/// <typeparam name="TState">The state enum type.</typeparam>
/// <typeparam name="TTrigger">The trigger enum type.</typeparam>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public sealed class TransitionAttribute<TState, TTrigger> : Attribute
    where TState   : struct, Enum
    where TTrigger : struct, Enum
{
    /// <summary>Source state for this transition.</summary>
    public required TState From { get; init; }

    /// <summary>Trigger that fires the transition.</summary>
    public required TTrigger On { get; init; }

    /// <summary>Destination state after the transition fires.</summary>
    public required TState To { get; init; }

    /// <summary>
    /// When <c>true</c>, the generator emits a <c>Guard{TriggerName}(TState, TTrigger)</c>
    /// partial method stub and adds a <c>when</c> clause to the switch arm.
    /// Ignored in concurrent mode.
    /// Default: <c>false</c>.
    /// </summary>
    public bool When { get; init; } = false;
}
