namespace ZeroAlloc.StateMachine.Generator.Tests;

public class StateMachineGroupGeneratorTests
{
    [Fact]
    public Task TwoParts()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
namespace MyApp;

public enum OpState { Idle, Running }
public enum OpTrigger { Start, Stop }
public enum ConnState { Disconnected, Connected }
public enum ConnTrigger { Connect, Disconnect }

[StateMachineGroup]
[StateMachinePart<OpState,   OpTrigger>(Name = ""Operational"", InitialState = OpState.Idle)]
[StateMachinePart<ConnState, ConnTrigger>(Name = ""Connection"", InitialState = ConnState.Disconnected)]
[Transition<OpState,   OpTrigger>(From = OpState.Idle,    On = OpTrigger.Start,      To = OpState.Running,      Part = ""Operational"")]
[Transition<OpState,   OpTrigger>(From = OpState.Running, On = OpTrigger.Stop,       To = OpState.Idle,         Part = ""Operational"")]
[Transition<ConnState, ConnTrigger>(From = ConnState.Disconnected, On = ConnTrigger.Connect,    To = ConnState.Connected,    Part = ""Connection"")]
[Transition<ConnState, ConnTrigger>(From = ConnState.Connected,    On = ConnTrigger.Disconnect, To = ConnState.Disconnected, Part = ""Connection"")]
public partial class Device { }
";
        return TestHelper.Verify<StateMachineGenerator>(source);
    }

    [Fact]
    public Task TwoPartsOneTimedEdge()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
namespace MyApp;

public enum OpState { Idle, Running, Faulted }
public enum OpTrigger { Start, Fault }
public enum ConnState { Disconnected, Connected }
public enum ConnTrigger { Connect, Disconnect }

[StateMachineGroup]
[StateMachinePart<OpState,   OpTrigger>(Name = ""Operational"", InitialState = OpState.Idle)]
[StateMachinePart<ConnState, ConnTrigger>(Name = ""Connection"", InitialState = ConnState.Disconnected)]
[Transition<OpState,   OpTrigger>(From = OpState.Idle,    On = OpTrigger.Start, To = OpState.Running,  Part = ""Operational"")]
[Transition<OpState,   OpTrigger>(From = OpState.Running, On = OpTrigger.Fault, To = OpState.Faulted,  Part = ""Operational"", AfterMs = 10000)]
[Transition<ConnState, ConnTrigger>(From = ConnState.Disconnected, On = ConnTrigger.Connect,    To = ConnState.Connected,    Part = ""Connection"")]
[Transition<ConnState, ConnTrigger>(From = ConnState.Connected,    On = ConnTrigger.Disconnect, To = ConnState.Disconnected, Part = ""Connection"")]
public partial class DeviceTimed { }
";
        return TestHelper.Verify<StateMachineGenerator>(source);
    }
}
