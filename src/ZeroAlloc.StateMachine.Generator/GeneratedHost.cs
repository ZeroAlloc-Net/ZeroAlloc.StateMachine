namespace ZeroAlloc.StateMachine.Generator;

/// <summary>A host a source file is generated for: its file name, name and declaration.</summary>
internal sealed record GeneratedHost(string HintName, string DisplayName, LocationInfo? Location)
{
    /// <summary>
    /// Whether the machine gets a file: it can be generated into, has no error, and has
    /// transitions. A machine without any is only built so that ZSM0020 can be reported.
    /// </summary>
    public static bool IsGenerated(StateMachineModel model) =>
        model.Declaration is not null && !model.Diagnostics.Any(static d => d.IsError) && !model.Transitions.IsEmpty;

    /// <summary>Whether the group gets a file: it can be generated into, and has no error.</summary>
    public static bool IsGenerated(StateMachineGroupModel model) =>
        model.Declaration is not null && !model.Diagnostics.Any(static d => d.IsError);

    public static GeneratedHost? For(StateMachineModel model) =>
        IsGenerated(model) ? new GeneratedHost(model.HintName, model.DisplayName, model.HostLocation) : null;

    public static GeneratedHost? For(StateMachineGroupModel model) =>
        IsGenerated(model) ? new GeneratedHost(model.HintName, model.DisplayName, model.HostLocation) : null;
}
