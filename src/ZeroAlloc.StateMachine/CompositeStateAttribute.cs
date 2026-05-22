namespace ZeroAlloc.StateMachine;

using System;

/// <summary>
/// Declares that a state of the enclosing state machine is a composite — when the machine
/// is in <see cref="State"/>, triggers are first dispatched to the sub-machine instance
/// (a <c>[StateMachine]</c> partial class identified by <see cref="SubMachine"/>) before
/// falling through to the parent's own transition table.
/// </summary>
/// <typeparam name="TState">The state enum type of the enclosing state machine.</typeparam>
/// <remarks>
/// <para>The sub-machine must:</para>
/// <list type="bullet">
///   <item>be a <c>partial</c> class with its own <c>[StateMachine]</c> attribute;</item>
///   <item>declare transitions using the SAME <c>TTrigger</c> as the parent (its own <c>TState</c> is independent);</item>
///   <item>NOT itself be in concurrent mode (composite states are sequential-only — see <c>ZSM0005</c>).</item>
/// </list>
/// <para>Composite states are mutually exclusive with <c>[Terminal]</c> on the same state (see <c>ZSM0011</c>).</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public sealed class CompositeStateAttribute<TState> : Attribute
    where TState : struct, Enum
{
    /// <summary>Parent state whose dispatch is delegated to the sub-machine.</summary>
    public required TState State { get; init; }

    /// <summary>Type of the sub-machine — must be a <c>[StateMachine]</c> partial class with the same <c>TTrigger</c>.</summary>
    public required Type SubMachine { get; init; }
}
