namespace ZeroAlloc.StateMachine;

/// <summary>
/// Marks a <c>partial</c> class or struct as a source-generated state machine.
/// The generator emits a <c>TryFire(TTrigger)</c> method, a <c>Current</c> property,
/// and <c>partial</c> method stubs for guards and entry/exit hooks.
/// </summary>
/// <example>
/// <code>
/// [StateMachine(InitialState = nameof(OrderState.Idle))]
/// [Transition&lt;OrderState, OrderTrigger&gt;(From = OrderState.Idle, On = OrderTrigger.Submit, To = OrderState.Pending)]
/// public partial class OrderStateMachine { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class StateMachineAttribute : Attribute
{
    /// <summary>
    /// The name of the initial state. Use <c>nameof(YourEnum.Value)</c>.
    /// </summary>
    public required string InitialState { get; init; }

    /// <summary>
    /// When <c>true</c>, state is stored as a <c>volatile long</c> and transitions
    /// use <c>Interlocked.CompareExchange</c> — safe for concurrent callers.
    /// Guards are not generated in concurrent mode (TOCTOU risk).
    /// Default: <c>false</c>.
    /// </summary>
    public bool Concurrent { get; init; } = false;

    /// <summary>
    /// When <c>true</c>, the generator emits a <c>public const string MermaidDiagram</c>
    /// on the partial containing a Mermaid <c>stateDiagram-v2</c> rendering of the
    /// machine's transitions. Composite sub-FSMs render as nested <c>state X { ... }</c>
    /// blocks; timed edges annotate with <c>(after Nms)</c>; guards annotate with
    /// <c>[guard]</c>; terminal states render as <c>X --> [*]</c>.
    /// Default: <c>false</c>.
    /// </summary>
    public bool Diagram { get; init; } = false;
}
