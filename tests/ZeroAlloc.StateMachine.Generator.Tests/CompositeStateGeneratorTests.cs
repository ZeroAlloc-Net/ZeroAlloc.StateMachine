using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.StateMachine.Generator.Tests;

public class CompositeStateGeneratorTests
{
    [Fact]
    public Task Basic_OneComposite_NoHistory()
    {
        var source = """
            using ZeroAlloc.StateMachine;

            namespace MyApp;

            public enum ParentState  { Idle, Loading, Done }
            public enum LoadingState { Fetching, Parsing }
            public enum Trigger      { Start, DataReceived, Cancel, Complete }

            [StateMachine(InitialState = nameof(LoadingState.Fetching))]
            [Transition<LoadingState, Trigger>(From = LoadingState.Fetching, On = Trigger.DataReceived, To = LoadingState.Parsing)]
            public partial class LoadingFsm { }

            [StateMachine(InitialState = nameof(ParentState.Idle))]
            [Transition<ParentState, Trigger>(From = ParentState.Idle,    On = Trigger.Start,    To = ParentState.Loading)]
            [Transition<ParentState, Trigger>(From = ParentState.Loading, On = Trigger.Cancel,   To = ParentState.Idle)]
            [Transition<ParentState, Trigger>(From = ParentState.Loading, On = Trigger.Complete, To = ParentState.Done)]
            [CompositeState<ParentState>(State = ParentState.Loading, SubMachine = typeof(LoadingFsm))]
            [Terminal<ParentState>(State = ParentState.Done)]
            public partial class ParentMachine { }
            """;

        return TestHelper.Verify<StateMachineGenerator>(source);
    }

    [Fact]
    public Task WithHistory_RestoresLeaf()
    {
        var source = """
            using ZeroAlloc.StateMachine;

            namespace MyApp;

            public enum ParentState  { Idle, Loading }
            public enum LoadingState { Fetching, Parsing }
            public enum Trigger      { Start, DataReceived, Suspend, Resume }

            [StateMachine(InitialState = nameof(LoadingState.Fetching))]
            [Transition<LoadingState, Trigger>(From = LoadingState.Fetching, On = Trigger.DataReceived, To = LoadingState.Parsing)]
            public partial class LoadingFsm { }

            [StateMachine(InitialState = nameof(ParentState.Idle))]
            [Transition<ParentState, Trigger>(From = ParentState.Idle,    On = Trigger.Start,   To = ParentState.Loading)]
            [Transition<ParentState, Trigger>(From = ParentState.Loading, On = Trigger.Suspend, To = ParentState.Idle)]
            [Transition<ParentState, Trigger>(From = ParentState.Idle,    On = Trigger.Resume,  To = ParentState.Loading)]
            [CompositeState<ParentState>(State = ParentState.Loading, SubMachine = typeof(LoadingFsm))]
            [HistoryState<ParentState>(State = ParentState.Loading)]
            public partial class ParentMachine { }
            """;

        return TestHelper.Verify<StateMachineGenerator>(source);
    }

    [Fact]
    public Task Nested_TwoLevelsDeep()
    {
        var source = """
            using ZeroAlloc.StateMachine;

            namespace MyApp;

            public enum L1 { A, B }
            public enum L2 { X, Y }
            public enum L3 { P, Q }
            public enum Trigger { Tick, Reset }

            [StateMachine(InitialState = nameof(L3.P))]
            [Transition<L3, Trigger>(From = L3.P, On = Trigger.Tick, To = L3.Q)]
            public partial class LeafFsm { }

            [StateMachine(InitialState = nameof(L2.X))]
            [Transition<L2, Trigger>(From = L2.X, On = Trigger.Tick, To = L2.Y)]
            [CompositeState<L2>(State = L2.X, SubMachine = typeof(LeafFsm))]
            public partial class MidFsm { }

            [StateMachine(InitialState = nameof(L1.A))]
            [Transition<L1, Trigger>(From = L1.A, On = Trigger.Reset, To = L1.B)]
            [CompositeState<L1>(State = L1.A, SubMachine = typeof(MidFsm))]
            public partial class TopFsm { }
            """;

        return TestHelper.Verify<StateMachineGenerator>(source);
    }

    [Fact]
    public Task MultipleComposites_OneParent()
    {
        var source = """
            using ZeroAlloc.StateMachine;

            namespace MyApp;

            public enum Outer { Idle, Connecting, Authenticated }
            public enum Conn  { Resolving, Handshaking }
            public enum Auth  { ValidatingToken, Renewing }
            public enum T     { Begin, Done, Restart }

            [StateMachine(InitialState = nameof(Conn.Resolving))]
            [Transition<Conn, T>(From = Conn.Resolving, On = T.Done, To = Conn.Handshaking)]
            public partial class ConnFsm { }

            [StateMachine(InitialState = nameof(Auth.ValidatingToken))]
            [Transition<Auth, T>(From = Auth.ValidatingToken, On = T.Done, To = Auth.Renewing)]
            public partial class AuthFsm { }

            [StateMachine(InitialState = nameof(Outer.Idle))]
            [Transition<Outer, T>(From = Outer.Idle,          On = T.Begin,   To = Outer.Connecting)]
            [Transition<Outer, T>(From = Outer.Connecting,    On = T.Done,    To = Outer.Authenticated)]
            [Transition<Outer, T>(From = Outer.Authenticated, On = T.Restart, To = Outer.Idle)]
            [CompositeState<Outer>(State = Outer.Connecting,    SubMachine = typeof(ConnFsm))]
            [CompositeState<Outer>(State = Outer.Authenticated, SubMachine = typeof(AuthFsm))]
            public partial class Machine { }
            """;

        return TestHelper.Verify<StateMachineGenerator>(source);
    }
}
