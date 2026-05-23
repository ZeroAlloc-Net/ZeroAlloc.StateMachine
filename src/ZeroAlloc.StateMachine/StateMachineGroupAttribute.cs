namespace ZeroAlloc.StateMachine;

using System;

/// <summary>
/// Declares the enclosing class as a group of concurrent state machines (B5). The class
/// must declare one or more <see cref="StateMachinePartAttribute{TState, TTrigger}"/>
/// attributes; the generator emits one independent CAS state field + <c>TryFire&lt;Name&gt;</c>
/// + per-part hooks per part.
/// </summary>
/// <remarks>
/// <para>Mutually exclusive with <see cref="StateMachineAttribute"/> on the same class
/// (see <c>ZSM0014</c>). Parts are always concurrent; <see cref="CompositeStateAttribute{TState}"/>
/// is disallowed inside a group (see <c>ZSM0018</c>).</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class StateMachineGroupAttribute : Attribute
{
    /// <summary>
    /// When <c>true</c>, the generator emits a <c>public const string MermaidDiagram</c>
    /// on the group partial. Each <see cref="StateMachinePartAttribute{TState, TTrigger}"/>
    /// renders as a top-level <c>state {Name} { ... }</c> block; transitions, terminals,
    /// timed edges, and guards render per the standard Mermaid rules.
    /// Default: <c>false</c>.
    /// </summary>
    public bool Diagram { get; init; } = false;
}
