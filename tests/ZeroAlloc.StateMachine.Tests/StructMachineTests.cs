using System.Reflection;
using System.Runtime.CompilerServices;

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

    [Fact]
    public void Struct_sub_machine_keeps_its_state_change_inside_a_class_parent()
    {
        var m = new ClassParentOfStructFsm();
        Assert.True(m.TryFire(StructTrigger.Start));

        Assert.True(m.TryFire(StructTrigger.DataReceived));    // sub: Fetching -> Parsing
        Assert.False(m.TryFire(StructTrigger.DataReceived));   // sub is in Parsing; no edge left
    }

    [Fact]
    public void Struct_sub_machine_history_is_restored_inside_a_class_parent()
    {
        var m = new ClassParentOfStructFsm();
        m.TryFire(StructTrigger.Start);
        m.TryFire(StructTrigger.DataReceived);                 // sub: Fetching -> Parsing

        Assert.True(m.TryFire(StructTrigger.Suspend));         // history captures Parsing
        Assert.True(m.TryFire(StructTrigger.Start));           // history restores Parsing
        Assert.False(m.TryFire(StructTrigger.DataReceived));
    }

    [Fact]
    public void Struct_sub_machine_keeps_its_state_change_inside_a_struct_parent()
    {
        var m = new StructParentOfStructFsm();
        Assert.True(m.TryFire(StructTrigger.Start));
        Assert.Equal(StructOuterState.Loading, m.Current);

        Assert.True(m.TryFire(StructTrigger.DataReceived));    // sub: Fetching -> Parsing
        Assert.False(m.TryFire(StructTrigger.DataReceived));   // sub is in Parsing; no edge left

        Assert.True(m.TryFire(StructTrigger.Suspend));
        Assert.True(m.TryFire(StructTrigger.Start));           // history restores Parsing
        Assert.False(m.TryFire(StructTrigger.DataReceived));
    }

    [Fact]
    public void Struct_machine_Current_is_a_readonly_member()
    {
        // A readonly getter lets a machine held in a readonly field or passed by `in` read
        // Current without a defensive copy.
        var getter = typeof(StructLightSwitch).GetProperty(nameof(StructLightSwitch.Current))!.GetMethod!;
        Assert.NotNull(getter.GetCustomAttribute<IsReadOnlyAttribute>());
    }
}
