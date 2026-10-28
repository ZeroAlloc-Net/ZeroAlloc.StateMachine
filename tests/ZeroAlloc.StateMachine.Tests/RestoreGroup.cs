namespace ZeroAlloc.StateMachine.Tests;

/// <summary>
/// A group for the Restore tests. Link's initial state has a timed edge, and Op's Running state
/// has one, so a test can tell which timers a restore armed.
/// </summary>
[StateMachineGroup]
[StateMachinePart<RgOpState,   OpT>(Name = "Op",   InitialState = RgOpState.Idle)]
[StateMachinePart<RgLinkState, ConnT>(Name = "Link", InitialState = RgLinkState.Down)]
[Transition<RgOpState,   OpT>(From = RgOpState.Idle,    On = OpT.Start, To = RgOpState.Running, Part = "Op")]
[Transition<RgOpState,   OpT>(From = RgOpState.Running, On = OpT.Stop,  To = RgOpState.Idle,    Part = "Op")]
[Transition<RgOpState,   OpT>(From = RgOpState.Running, On = OpT.Fault, To = RgOpState.Faulted, Part = "Op", AfterMs = 200)]
[Transition<RgLinkState, ConnT>(From = RgLinkState.Down, On = ConnT.Connect,    To = RgLinkState.Up,   Part = "Link", AfterMs = 300)]
[Transition<RgLinkState, ConnT>(From = RgLinkState.Up,   On = ConnT.Disconnect, To = RgLinkState.Down, Part = "Link")]
public partial class RestoreGroup
{
    private readonly List<string> _log = new();

    public IReadOnlyList<string> Log => _log;

    partial void OnEnterOpRunning(RgOpState from) => _log.Add("enter:Op.Running");
    partial void OnExitOpRunning(OpT on) => _log.Add("exit:Op.Running");
    partial void OnEnterLinkUp(RgLinkState from) => _log.Add("enter:Link.Up");
}
