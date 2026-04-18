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
}
