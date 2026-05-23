namespace ZeroAlloc.StateMachine.Generator.Tests;

using System.Threading.Tasks;
using Xunit;

public class MermaidDiagramGeneratorTests
{
    [Fact]
    public Task Flat_Diagram()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
namespace MyApp;

public enum OS { Idle, Submitted, Shipped }
public enum OT { Submit, Ship, Cancel }

[StateMachine(InitialState = ""Idle"", Diagram = true)]
[Transition<OS, OT>(From = OS.Idle, On = OT.Submit, To = OS.Submitted)]
[Transition<OS, OT>(From = OS.Submitted, On = OT.Ship, To = OS.Shipped, When = true)]
[Terminal<OS>(State = OS.Shipped)]
public partial class Order { }
";
        return TestHelper.Verify<StateMachineGenerator>(source);
    }

    [Fact]
    public Task Composite_Diagram()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
namespace MyApp;

public enum LoadingState { Fetching, Parsing, Done }
public enum AppTrigger { Begin, Tick, Complete }

[StateMachine(InitialState = ""Fetching"")]
[Transition<LoadingState, AppTrigger>(From = LoadingState.Fetching, On = AppTrigger.Tick,     To = LoadingState.Parsing)]
[Transition<LoadingState, AppTrigger>(From = LoadingState.Parsing,  On = AppTrigger.Complete, To = LoadingState.Done)]
[Terminal<LoadingState>(State = LoadingState.Done)]
public partial class LoadingFsm { }

public enum AppState { Idle, Loading, Ready }

[StateMachine(InitialState = ""Idle"", Diagram = true)]
[Transition<AppState, AppTrigger>(From = AppState.Idle,    On = AppTrigger.Begin,    To = AppState.Loading)]
[Transition<AppState, AppTrigger>(From = AppState.Loading, On = AppTrigger.Complete, To = AppState.Ready)]
[CompositeState<AppState>(State = AppState.Loading, SubMachine = typeof(LoadingFsm))]
[HistoryState<AppState>(State = AppState.Loading)]
[Terminal<AppState>(State = AppState.Ready)]
public partial class App { }
";
        return TestHelper.Verify<StateMachineGenerator>(source);
    }

    [Fact]
    public Task Group_Diagram()
    {
        const string source = @"
using ZeroAlloc.StateMachine;
namespace MyApp;

public enum OpS { Idle, Running }
public enum OpT { Start, Stop }
public enum ConnS { Disconnected, Connected }
public enum ConnT { Connect, Disconnect }

[StateMachineGroup(Diagram = true)]
[StateMachinePart<OpS,   OpT>(Name = ""Op"",   InitialState = OpS.Idle)]
[StateMachinePart<ConnS, ConnT>(Name = ""Conn"", InitialState = ConnS.Disconnected)]
[Transition<OpS,   OpT>(From = OpS.Idle,    On = OpT.Start, To = OpS.Running, Part = ""Op"")]
[Transition<OpS,   OpT>(From = OpS.Running, On = OpT.Stop,  To = OpS.Idle,    Part = ""Op"")]
[Transition<ConnS, ConnT>(From = ConnS.Disconnected, On = ConnT.Connect,    To = ConnS.Connected,    Part = ""Conn"")]
[Transition<ConnS, ConnT>(From = ConnS.Connected,    On = ConnT.Disconnect, To = ConnS.Disconnected, Part = ""Conn"")]
public partial class Device { }
";
        return TestHelper.Verify<StateMachineGenerator>(source);
    }
}
