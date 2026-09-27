namespace ZeroAlloc.StateMachine.Tests;

/// <summary>A group of two independent parts, one of which has a timed transition.</summary>
[StateMachineGroup]
[StateMachinePart<OpS,   OpT>(Name = "Op",   InitialState = OpS.Idle)]
[StateMachinePart<ConnS, ConnT>(Name = "Conn", InitialState = ConnS.Disconnected)]
[Transition<OpS,   OpT>(From = OpS.Idle,    On = OpT.Start, To = OpS.Running, Part = "Op")]
[Transition<OpS,   OpT>(From = OpS.Running, On = OpT.Stop,  To = OpS.Idle,    Part = "Op")]
[Transition<OpS,   OpT>(From = OpS.Running, On = OpT.Fault, To = OpS.Faulted, Part = "Op", AfterMs = 100)]
[Transition<ConnS, ConnT>(From = ConnS.Disconnected, On = ConnT.Connect,    To = ConnS.Connected,    Part = "Conn")]
[Transition<ConnS, ConnT>(From = ConnS.Connected,    On = ConnT.Disconnect, To = ConnS.Disconnected, Part = "Conn")]
public partial class Device { }
