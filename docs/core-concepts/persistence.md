---
id: persistence
title: Persistence
sidebar_position: 8
---

# Persistence

A `[StateMachine]` always starts in its `InitialState`. To rebuild a machine from a state you
stored earlier, such as an aggregate's lifecycle state loaded from a database, call the
generated static `Restore` method:

```csharp
OrderState stored = row.State;               // read from your store
var order = OrderMachine.Restore(stored);    // Current == stored
order.TryFire(OrderTrigger.Ship);            // carries on from there
```

Store `machine.Current`, which is a plain enum, and pass it back to `Restore`. There is no
trigger history to keep or replay.

## What Restore does

```csharp
public static OrderMachine Restore(OrderState state);
```

- **Fires no hooks.** Neither `OnExit` nor `OnEnter` runs, and no guard is evaluated. The
  state was reached earlier, and its side effects already happened.
- **Validates the state.** It throws `ArgumentOutOfRangeException`, with `ParamName` set to
  `state`, for a value that is not a declared state, such as `(OrderState)42`, and for a
  declared state that cannot be reached from `InitialState` through the transitions.
  The generator works out the set of valid states at build time and emits it as a `switch`,
  so the check allocates nothing and uses no reflection or `Enum.IsDefined`.
- **Arms timers.** On a concurrent machine with `AfterMs` transitions, timers for the
  restored state are armed, and timers armed for any other state are disarmed. The timer
  starts over: a persisted state carries no record of how long the machine was in it.
- **Works for every variant.** Class and struct machines, concurrent or not.
  For a struct it returns a copy.

`Reset` and `ResetTo` stay `internal`. `Restore` is the supported entry point because it
validates its argument.

## Composite states

Restoring a composite state resets its sub-machine to the sub-machine's initial state:

```csharp
var m = AppMachine.Restore(AppState.Loading);   // sub-machine at its initial state
```

To restore both levels, persist the sub-machine's state as well and pass it to the overload.
The generator emits one for each distinct sub-state type:

```csharp
public static AppMachine Restore(AppState state, LoadingState subState);

var m = AppMachine.Restore(AppState.Loading, LoadingState.Parsing);
```

It throws `ArgumentOutOfRangeException` with `ParamName` `subState` when the sub-state is not
a restorable state of that composite's sub-machine, and `ArgumentException` when `state` is not
a composite state with a sub-machine over that sub-state type. Shallow history is not part of
the restore: history is captured again the next time the composite is left.

## When Restore is not generated

`Restore` creates the machine with `new T()` and then sets its state, so the machine needs a
parameterless constructor. It is left out, with an informational diagnostic, and the machine
is still generated, when:

- a class declares constructors but none without parameters, or has a primary constructor
  and no parameterless constructor, or is abstract, or a struct has a primary constructor
  and no parameterless constructor that chains to it: [ZSM0027](../diagnostics/ZSM0027.md);
- the type already declares a member named `Restore` that would conflict with the generated
  one: [ZSM0028](../diagnostics/ZSM0028.md).

Both are informational so that a type that compiled before `Restore` existed keeps compiling.

`[StateMachineGroup]` types do not get `Restore`.
