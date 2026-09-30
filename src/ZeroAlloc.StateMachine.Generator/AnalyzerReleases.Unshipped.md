; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category               | Severity | Notes
--------|------------------------|----------|------------------------------------------------------------------
ZSM0023 | ZeroAlloc.StateMachine | Warning  | State machine nested in a containing type that is not partial
ZSM0024 | ZeroAlloc.StateMachine | Error    | File-local state machine
ZSM0025 | ZeroAlloc.StateMachine | Error    | State machine name differs only in case from another state machine
ZSM0026 | ZeroAlloc.StateMachine | Warning  | Copies of a timed record share its timers
