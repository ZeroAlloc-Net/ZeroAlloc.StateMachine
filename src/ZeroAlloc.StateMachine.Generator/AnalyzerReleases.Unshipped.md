; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### Changed Rules

Rule ID | New Category           | New Severity | Old Category           | Old Severity | Notes
--------|------------------------|--------------|------------------------|--------------|---------------------------------------------------------------
ZSM0022 | ZeroAlloc.StateMachine | Error        | ZeroAlloc.StateMachine | Warning      | When = true is ignored on a concurrent machine
ZSM0023 | ZeroAlloc.StateMachine | Error        | ZeroAlloc.StateMachine | Warning      | State machine nested in a containing type that is not partial
