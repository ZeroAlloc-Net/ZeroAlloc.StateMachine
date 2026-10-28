namespace ZeroAlloc.StateMachine.Tests;

/// <summary>Class machine whose entry hooks log, to show that Restore fires none.</summary>
[StateMachine(InitialState = nameof(RState.Idle))]
[Transition<RState, RTrigger>(From = RState.Idle,    On = RTrigger.Go, To = RState.Working)]
[Transition<RState, RTrigger>(From = RState.Working, On = RTrigger.Go, To = RState.Done)]
[Terminal<RState>(State = RState.Done)]
public partial class RestoreClassMachine
{
    private readonly List<string> _log = new();

    public IReadOnlyList<string> Log => _log;

    partial void OnEnterWorking(RState from) => _log.Add("enter:Working");
    partial void OnExitWorking(RTrigger on) => _log.Add("exit:Working");
    partial void OnEnterDone(RState from) => _log.Add("enter:Done");
}
