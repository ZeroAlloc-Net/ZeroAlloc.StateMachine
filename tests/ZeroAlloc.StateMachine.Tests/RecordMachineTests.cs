namespace ZeroAlloc.StateMachine.Tests;

/// <summary>
/// Record and record struct machines, built by the generator running as an analyzer the way an
/// application uses it (#161).
/// </summary>
public partial class RecordMachineTests
{
    [StateMachine(InitialState = nameof(SwitchState.On))]
    [Transition<SwitchState, SwitchTrigger>(From = SwitchState.Off, On = SwitchTrigger.Flip, To = SwitchState.On)]
    [Transition<SwitchState, SwitchTrigger>(From = SwitchState.On,  On = SwitchTrigger.Flip, To = SwitchState.Off)]
    public partial record RecordSwitch
    {
        public int Entered { get; private set; }

        partial void OnEnterOff(SwitchState from) => Entered++;
    }

    [StateMachine(InitialState = nameof(SwitchState.On))]
    [Transition<SwitchState, SwitchTrigger>(From = SwitchState.Off, On = SwitchTrigger.Flip, To = SwitchState.On)]
    [Transition<SwitchState, SwitchTrigger>(From = SwitchState.On,  On = SwitchTrigger.Flip, To = SwitchState.Off)]
    public partial record struct RecordStructSwitch;

    [StateMachine(InitialState = nameof(SwitchState.On))]
    [Transition<SwitchState, SwitchTrigger>(From = SwitchState.Off, On = SwitchTrigger.Flip, To = SwitchState.On)]
    [Transition<SwitchState, SwitchTrigger>(From = SwitchState.On,  On = SwitchTrigger.Flip, To = SwitchState.Off)]
    public partial record struct PositionalSwitch(string Name);

    public partial record Panel(int Id)
    {
        [StateMachine(InitialState = nameof(SwitchState.On))]
        [Transition<SwitchState, SwitchTrigger>(From = SwitchState.Off, On = SwitchTrigger.Flip, To = SwitchState.On)]
        [Transition<SwitchState, SwitchTrigger>(From = SwitchState.On,  On = SwitchTrigger.Flip, To = SwitchState.Off)]
        public partial record struct Light;
    }

    [Fact]
    public void Record_machine_fires_and_runs_its_hooks()
    {
        var m = new RecordSwitch();
        m.Current.Should().Be(SwitchState.On);

        m.TryFire(SwitchTrigger.Flip).Should().BeTrue();

        m.Current.Should().Be(SwitchState.Off);
        m.Entered.Should().Be(1);
    }

    [Fact]
    public void Record_machine_copied_with_with_keeps_the_state_and_then_moves_on_its_own()
    {
        var original = new RecordSwitch();
        original.TryFire(SwitchTrigger.Flip);

        var copy = original with { };
        copy.Current.Should().Be(SwitchState.Off);
        copy.TryFire(SwitchTrigger.Flip).Should().BeTrue();

        copy.Current.Should().Be(SwitchState.On);
        original.Current.Should().Be(SwitchState.Off);
    }

    [Fact]
    public void Record_struct_machine_starts_in_its_initial_state()
    {
        var m = new RecordStructSwitch();
        m.Current.Should().Be(SwitchState.On);

        m.TryFire(SwitchTrigger.Flip).Should().BeTrue();
        m.Current.Should().Be(SwitchState.Off);
    }

    [Fact]
    public void Positional_record_struct_machine_starts_in_its_initial_state_from_its_primary_constructor()
    {
        var m = new PositionalSwitch("hall");
        m.Current.Should().Be(SwitchState.On);

        m.TryFire(SwitchTrigger.Flip).Should().BeTrue();
        m.Current.Should().Be(SwitchState.Off);
        m.Name.Should().Be("hall");
    }

    [Fact]
    public void Record_struct_machine_nested_in_a_record_fires()
    {
        var m = new Panel.Light();

        m.TryFire(SwitchTrigger.Flip).Should().BeTrue();

        m.Current.Should().Be(SwitchState.Off);
    }
}
