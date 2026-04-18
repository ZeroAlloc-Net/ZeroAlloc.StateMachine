// Polyfill required for C# records / init-only setters when targeting netstandard2.0.
// The compiler emits a reference to this type for 'init' accessors; it is absent from
// the netstandard2.0 BCL, so we provide the definition ourselves.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit { }
