namespace ZeroAlloc.StateMachine.Tests;

/// <summary>
/// A flat struct machine with no user constructor. Its initial state is not the enum default,
/// so a test can tell whether the field initializers ran.
/// </summary>
[StateMachine(InitialState = nameof(SwitchState.On))]
[Transition<SwitchState, SwitchTrigger>(From = SwitchState.Off, On = SwitchTrigger.Flip, To = SwitchState.On)]
[Transition<SwitchState, SwitchTrigger>(From = SwitchState.On,  On = SwitchTrigger.Flip, To = SwitchState.Off)]
public partial struct StructLightSwitch { }
