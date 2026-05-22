namespace ZeroAlloc.StateMachine;

using System;

/// <summary>
/// Declares a single named state machine inside a <see cref="StateMachineGroupAttribute"/> (B5).
/// Each part has an independent <typeparamref name="TState"/> + <typeparamref name="TTrigger"/>
/// and is dispatched via the generator-emitted <c>TryFire{Name}(TTrigger)</c> method.
/// </summary>
/// <typeparam name="TState">The part's state enum type.</typeparam>
/// <typeparam name="TTrigger">The part's trigger enum type.</typeparam>
/// <remarks>
/// Stack multiple on the same class. <c>Name</c> must be unique within the class (see
/// <c>ZSM0015</c>). Transitions belonging to this part are tagged with
/// <c>[Transition&lt;TState, TTrigger&gt;(... Part = "Name")]</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class StateMachinePartAttribute<TState, TTrigger> : Attribute
    where TState   : struct, Enum
    where TTrigger : struct, Enum
{
    /// <summary>Unique name within the class. Used to derive <c>TryFire{Name}</c>, <c>{Name}Current</c>, <c>OnEnter{Name}{State}</c>, etc.</summary>
    public required string Name { get; init; }

    /// <summary>Initial state for this part. Set in the generated constructor.</summary>
    public required TState InitialState { get; init; }
}
