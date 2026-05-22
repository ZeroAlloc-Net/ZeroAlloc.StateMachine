namespace ZeroAlloc.StateMachine.Generator;

/// <summary>Single [HistoryState] declaration captured during parsing.</summary>
/// <param name="State">Short enum member name of the composite that gets shallow history.</param>
internal sealed record HistoryStateModel(string State);
