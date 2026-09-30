; Shipped analyzer releases.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.0.0

### New Rules

Rule ID | Category               | Severity | Notes
--------|------------------------|----------|-----------------------------------------
ZSM0001 | ZeroAlloc.StateMachine | Warning  | Unreachable state
ZSM0002 | ZeroAlloc.StateMachine | Warning  | Unintentional sink state
ZSM0003 | ZeroAlloc.StateMachine | Warning  | Single-use trigger
ZSM0004 | ZeroAlloc.StateMachine | Error    | Concurrent mode not supported on structs

## Release 1.4.0

### New Rules

Rule ID | Category               | Severity | Notes
--------|------------------------|----------|--------------------------------------------------------------
ZSM0005 | ZeroAlloc.StateMachine | Error    | Composite state on a concurrent machine
ZSM0006 | ZeroAlloc.StateMachine | Error    | Sub-machine is not a [StateMachine]
ZSM0007 | ZeroAlloc.StateMachine | Error    | Sub-machine trigger type mismatch
ZSM0008 | ZeroAlloc.StateMachine | Error    | Composite state value not declared in TState
ZSM0009 | ZeroAlloc.StateMachine | Error    | Duplicate composite state
ZSM0010 | ZeroAlloc.StateMachine | Error    | History state without composite
ZSM0011 | ZeroAlloc.StateMachine | Error    | Composite state cannot also be terminal
ZSM0012 | ZeroAlloc.StateMachine | Error    | AfterMs requires Concurrent = true
ZSM0013 | ZeroAlloc.StateMachine | Error    | AfterMs must be positive
ZSM0014 | ZeroAlloc.StateMachine | Error    | [StateMachine] and [StateMachineGroup] are mutually exclusive
ZSM0015 | ZeroAlloc.StateMachine | Error    | Duplicate [StateMachinePart] name
ZSM0016 | ZeroAlloc.StateMachine | Error    | Transition references unknown part
ZSM0017 | ZeroAlloc.StateMachine | Error    | [StateMachineGroup] declares no parts
ZSM0018 | ZeroAlloc.StateMachine | Error    | [CompositeState] is not supported inside [StateMachineGroup]
ZSM0019 | ZeroAlloc.StateMachine | Error    | User-declared Dispose conflicts with generated signature

## Release 1.5.0

### New Rules

Rule ID | Category               | Severity | Notes
--------|------------------------|----------|----------------------------------------------------------------
ZSM0020 | ZeroAlloc.StateMachine | Warning  | [StateMachine(Diagram = true)] on a class with zero transitions
ZSM0021 | ZeroAlloc.StateMachine | Error    | User-declared constructor must call HookConstructor()

## Release 1.6.0

### New Rules

Rule ID | Category               | Severity | Notes
--------|------------------------|----------|-----------------------------------------------
ZSM0022 | ZeroAlloc.StateMachine | Warning  | When = true is ignored on a concurrent machine

## Release 1.6.2

### New Rules

Rule ID | Category               | Severity | Notes
--------|------------------------|----------|------------------------------------------------------------------
ZSM0023 | ZeroAlloc.StateMachine | Warning  | State machine nested in a containing type that is not partial
ZSM0024 | ZeroAlloc.StateMachine | Error    | File-local state machine
ZSM0025 | ZeroAlloc.StateMachine | Error    | State machine name differs only in case from another state machine
ZSM0026 | ZeroAlloc.StateMachine | Warning  | Copies of a timed record share its timers
