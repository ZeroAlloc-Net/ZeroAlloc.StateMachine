; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category               | Severity | Notes
--------|------------------------|----------|-----------------------------------------------------------------
ZSM0027 | ZeroAlloc.StateMachine | Info     | Restore is not generated: no parameterless constructor
ZSM0028 | ZeroAlloc.StateMachine | Info     | Restore is not generated: the type declares a conflicting member
