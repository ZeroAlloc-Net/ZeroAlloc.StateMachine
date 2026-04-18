namespace ZeroAlloc.StateMachine.Generator;

/// <summary>A single declared transition.</summary>
internal sealed record TransitionModel(
    string From,      // enum member name, e.g. "Idle"
    string On,        // enum member name, e.g. "Submit"
    string To,        // enum member name, e.g. "Pending"
    bool HasGuard     // When = true on the attribute
);
