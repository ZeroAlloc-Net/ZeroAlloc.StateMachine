namespace ZeroAlloc.StateMachine.Tests;

public class StructMachineTests
{
    [Fact]
    public void Struct_machine_without_a_user_constructor_starts_in_its_initial_state()
    {
        var m = new StructLightSwitch();
        Assert.Equal(SwitchState.On, m.Current);

        Assert.True(m.TryFire(SwitchTrigger.Flip));
        Assert.Equal(SwitchState.Off, m.Current);
    }
}
