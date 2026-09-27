namespace ZeroAlloc.StateMachine.Tests;

/// <summary>A struct machine whose composite state is backed by a struct sub-machine.</summary>
[StateMachine(InitialState = nameof(StructOuterState.Idle))]
[Transition<StructOuterState, StructTrigger>(From = StructOuterState.Idle,    On = StructTrigger.Start,   To = StructOuterState.Loading)]
[Transition<StructOuterState, StructTrigger>(From = StructOuterState.Loading, On = StructTrigger.Suspend, To = StructOuterState.Idle)]
[CompositeState<StructOuterState>(State = StructOuterState.Loading, SubMachine = typeof(StructLoadingFsm))]
[HistoryState<StructOuterState>(State = StructOuterState.Loading)]
public partial struct StructParentOfStructFsm { }
