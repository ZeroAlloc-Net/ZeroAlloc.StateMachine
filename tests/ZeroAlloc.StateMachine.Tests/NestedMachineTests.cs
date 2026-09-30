namespace ZeroAlloc.StateMachine.Tests;

/// <summary>
/// Machines nested in this class, some of them generic, built by the generator running as an
/// analyzer the way an application uses it. The generated members land on the nested types.
/// </summary>
public partial class NestedMachineTests
{
    [StateMachine(InitialState = nameof(SwitchState.Off))]
    [Transition<SwitchState, SwitchTrigger>(From = SwitchState.Off, On = SwitchTrigger.Flip, To = SwitchState.On)]
    [Transition<SwitchState, SwitchTrigger>(From = SwitchState.On,  On = SwitchTrigger.Flip, To = SwitchState.Off)]
    public partial class Switch<TTag>
    {
        public int Entered { get; private set; }

        partial void OnEnterOn(SwitchState from) => Entered++;
    }

    [StateMachine(InitialState = nameof(SwitchState.On))]
    [Transition<SwitchState, SwitchTrigger>(From = SwitchState.Off, On = SwitchTrigger.Flip, To = SwitchState.On)]
    [Transition<SwitchState, SwitchTrigger>(From = SwitchState.On,  On = SwitchTrigger.Flip, To = SwitchState.Off)]
    public partial struct @struct<TTag> where TTag : struct { }

    public partial class Panel
    {
        [StateMachineGroup]
        [StateMachinePart<SwitchState, SwitchTrigger>(Name = "Light", InitialState = SwitchState.Off)]
        [Transition<SwitchState, SwitchTrigger>(From = SwitchState.Off, On = SwitchTrigger.Flip, To = SwitchState.On,  Part = "Light")]
        [Transition<SwitchState, SwitchTrigger>(From = SwitchState.On,  On = SwitchTrigger.Flip, To = SwitchState.Off, Part = "Light")]
        public partial class Switches { }
    }

    [Fact]
    public void Generic_nested_class_machine_fires_and_runs_its_hooks()
    {
        var m = new Switch<string>();

        m.TryFire(SwitchTrigger.Flip).Should().BeTrue();

        m.Current.Should().Be(SwitchState.On);
        m.Entered.Should().Be(1);
    }

    [Fact]
    public void Generic_nested_struct_machine_with_a_keyword_name_starts_in_its_initial_state()
    {
        var m = new @struct<int>();
        m.Current.Should().Be(SwitchState.On);

        m.TryFire(SwitchTrigger.Flip).Should().BeTrue();
        m.Current.Should().Be(SwitchState.Off);
    }

    [Fact]
    public void Group_nested_two_levels_deep_fires_its_part()
    {
        var g = new Panel.Switches();

        g.TryFireLight(SwitchTrigger.Flip).Should().BeTrue();

        g.LightCurrent.Should().Be(SwitchState.On);
    }
}
