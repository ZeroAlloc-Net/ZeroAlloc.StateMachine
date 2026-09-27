namespace ZeroAlloc.StateMachine.Tests;

/// <summary>A parent machine whose Loading state is composite, with shallow history.</summary>
[StateMachine(InitialState = nameof(HOuterState.Idle))]
[Transition<HOuterState, HTrigger>(From = HOuterState.Idle,    On = HTrigger.Start,    To = HOuterState.Loading)]
[Transition<HOuterState, HTrigger>(From = HOuterState.Loading, On = HTrigger.Suspend,  To = HOuterState.Idle)]
[Transition<HOuterState, HTrigger>(From = HOuterState.Idle,    On = HTrigger.Resume,   To = HOuterState.Loading)]
[Transition<HOuterState, HTrigger>(From = HOuterState.Loading, On = HTrigger.Complete, To = HOuterState.Done)]
[CompositeState<HOuterState>(State = HOuterState.Loading, SubMachine = typeof(TestLoadingFsm))]
[HistoryState<HOuterState>(State = HOuterState.Loading)]
[Terminal<HOuterState>(State = HOuterState.Done)]
public partial class TestHierMachine { }
