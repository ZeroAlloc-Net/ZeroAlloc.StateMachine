---
id: concurrent-parts
title: Concurrent Parts
sidebar_position: 6
---

# Concurrent Parts

A **state machine group** declares two or more independent concurrent
state machines inside one class. Each *part* has its own state enum, its
own trigger enum, its own `TryFire<Name>`, and its own per-part
`OnEnter` / `OnExit` hooks. Parts share nothing but the host instance.

---

## Why

Some objects model multiple, orthogonal lifecycles. An IoT device has an
`Operational` state (`Idle`, `Running`, `Faulted`) AND a `Connection`
state (`Offline`, `Connecting`, `Online`) — they evolve on different
triggers and don't constrain each other. Modelling them as a single
flat enum yields a Cartesian product of irrelevant states
(`Idle_Offline`, `Idle_Connecting`, ...).

Concurrent parts let you keep the two machines side-by-side in one class
without losing thread-safety or zero-allocation dispatch.

---

## How

Replace `[StateMachine]` with `[StateMachineGroup]` and declare one
`[StateMachinePart<TState, TTrigger>]` per machine. Tag each
`[Transition]` with its `Part = "..."` name:

```csharp
using ZeroAlloc.StateMachine;

public enum OpState   { Idle, Running, Faulted }
public enum OpTrigger { Start, Stop, Fault }

public enum ConnState   { Offline, Connecting, Online }
public enum ConnTrigger { Connect, Established, Drop }

[StateMachineGroup]
[StateMachinePart<OpState,   OpTrigger>  (Name = "Operational", InitialState = OpState.Idle)]
[StateMachinePart<ConnState, ConnTrigger>(Name = "Connection",  InitialState = ConnState.Offline)]

[Transition<OpState, OpTrigger>(Part = "Operational", From = OpState.Idle,    On = OpTrigger.Start, To = OpState.Running)]
[Transition<OpState, OpTrigger>(Part = "Operational", From = OpState.Running, On = OpTrigger.Stop,  To = OpState.Idle)]
[Transition<OpState, OpTrigger>(Part = "Operational", From = OpState.Running, On = OpTrigger.Fault, To = OpState.Faulted)]

[Transition<ConnState, ConnTrigger>(Part = "Connection", From = ConnState.Offline,    On = ConnTrigger.Connect,     To = ConnState.Connecting)]
[Transition<ConnState, ConnTrigger>(Part = "Connection", From = ConnState.Connecting, On = ConnTrigger.Established, To = ConnState.Online)]
[Transition<ConnState, ConnTrigger>(Part = "Connection", From = ConnState.Online,     On = ConnTrigger.Drop,        To = ConnState.Offline)]
public partial class Device { }
```

Usage:

```csharp
var d = new Device();

d.TryFireOperational(OpTrigger.Start);       // OperationalCurrent: Idle → Running
d.TryFireConnection(ConnTrigger.Connect);    // ConnectionCurrent:  Offline → Connecting

d.OperationalCurrent;   // OpState.Running
d.ConnectionCurrent;    // ConnState.Connecting
```

The two parts run independently. `OperationalCurrent` and
`ConnectionCurrent` each have their own CAS-protected `volatile long`
field; calls don't serialise against each other.

---

## Constraints

- `[StateMachine]` and `[StateMachineGroup]` are mutually exclusive
  ([ZSM0014](../diagnostics/ZSM0014.md)).
- Each part `Name` must be unique within the class
  ([ZSM0015](../diagnostics/ZSM0015.md)).
- Every `[Transition]` inside a group must set `Part = "..."` to a name
  declared by some `[StateMachinePart]` on the class
  ([ZSM0016](../diagnostics/ZSM0016.md)).
- A `[StateMachineGroup]` class must declare at least one
  `[StateMachinePart]` ([ZSM0017](../diagnostics/ZSM0017.md)).
- `[CompositeState]` is not supported inside a group
  ([ZSM0018](../diagnostics/ZSM0018.md)) — parts are always concurrent,
  and concurrent mode already disallows composites.
- All parts are concurrent by construction — there is no opt-out per
  part. If you need a single-threaded part, model it as a separate
  non-group `[StateMachine]` class.

---

## Generator emit

For each `[StateMachinePart(Name = "X", InitialState = ...)]`, the
generator emits:

```csharp
// Per-part state field (CAS-protected)
private volatile long _state_X = (long)/* InitialState */;

// Per-part current accessor
public TState XCurrent
    => (TState)System.Threading.Volatile.Read(ref _state_X);

// Per-part public dispatch
public bool TryFireX(TTrigger trigger) { /* CAS loop over (XCurrent, trigger) */ }

// Per-part hooks (user-implemented as partial)
partial void OnEnterX(TState to, TState from);
partial void OnExitX (TState from, TTrigger trigger);
```

Each part is a complete copy of the v1 concurrent emit, name-mangled by
the part name. There is no shared state field, no shared trigger enum,
and no shared hook between parts.

Timed edges work inside parts: an `AfterMs` transition tagged with
`Part = "X"` generates a timer field scoped to that part, and the
callback dispatches to `TryFireX`. See
[Timeout Transitions](timeout-transitions.md).

---

## Per-part hooks

Implement the partial methods to react to state crossings on a specific
part:

```csharp
public partial class Device
{
    partial void OnEnterOperational(OpState to, OpState from)
    {
        if (to == OpState.Faulted) Log("device entered Faulted");
    }

    partial void OnExitConnection(ConnState from, ConnTrigger trigger)
    {
        if (from == ConnState.Online && trigger == ConnTrigger.Drop)
            FlushPendingSends();
    }
}
```

The hooks are independent — `OnEnterOperational` never observes a
`Connection` transition and vice versa.

---

## Related

- [Concurrent Mode](concurrent-mode.md) — the per-part CAS model is identical to single-machine concurrent mode
- [Timeout Transitions](timeout-transitions.md) — `AfterMs` inside a part
- [Attribute Reference — `[StateMachineGroup]`](../attributes.md#statemachinegroup)
- [Attribute Reference — `[StateMachinePart]`](../attributes.md#statemachinepart-tstate-ttrigger)
- [ZSM0014](../diagnostics/ZSM0014.md), [ZSM0015](../diagnostics/ZSM0015.md), [ZSM0016](../diagnostics/ZSM0016.md), [ZSM0017](../diagnostics/ZSM0017.md), [ZSM0018](../diagnostics/ZSM0018.md)
