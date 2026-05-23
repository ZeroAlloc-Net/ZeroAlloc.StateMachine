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

    /// <summary>
    /// When greater than zero, the generator emits a <see cref="System.Threading.Timer"/>
    /// that auto-fires <see cref="On"/> after this many milliseconds in <see cref="From"/>.
    /// The timer is armed in the generated entry path for <see cref="From"/> and disarmed
    /// when the machine leaves the state. Requires <c>Concurrent = true</c> on the class
    /// (or that the transition belongs to a <c>[StateMachinePart]</c> — parts are always
    /// concurrent).
    /// Default: <c>0</c> (no timer).
    /// </summary>
    public int AfterMs { get; init; }

    /// <summary>
    /// Discriminator that scopes this transition to a named <c>[StateMachinePart]</c> when the
    /// enclosing class is a <c>[StateMachineGroup]</c>. Must match a declared part's <c>Name</c>.
    /// Leave <c>null</c> for single-machine classes (i.e. classes declared with <c>[StateMachine]</c>).
    /// Default: <c>null</c>.
    /// </summary>
    public string? Part { get; init; } = null;
}
