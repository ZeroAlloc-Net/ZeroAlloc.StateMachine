namespace ZeroAlloc.StateMachine.Generator;

using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;

/// <summary>
/// A diagnostic found while building a model, reported when the model is emitted.
/// </summary>
/// <remarks>
/// A <see cref="Diagnostic"/> compares its message arguments by reference, so one rebuilt from
/// the same source never compares equal to the last, and a model holding it is never served from
/// the cache. Here the arguments are formatted to strings, compared by value, and the location is
/// a <see cref="LocationInfo"/>.
/// </remarks>
internal sealed record DiagnosticInfo(
    DiagnosticDescriptor Descriptor,
    LocationInfo? Location,
    EquatableArray<string> MessageArgs)
{
    public bool IsError => Descriptor.DefaultSeverity == DiagnosticSeverity.Error;

    /// <summary>
    /// Arguments are formatted with the invariant culture, as the message would format them. A
    /// null argument formats as empty, the same as <see cref="Diagnostic.Create(DiagnosticDescriptor, Microsoft.CodeAnalysis.Location, object[])"/>.
    /// </summary>
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, Location? location, params object?[] messageArgs)
    {
        var args = ImmutableArray.CreateBuilder<string>(messageArgs.Length);
        foreach (var arg in messageArgs)
            args.Add(System.Convert.ToString(arg, CultureInfo.InvariantCulture) ?? string.Empty);

        return new DiagnosticInfo(descriptor, LocationInfo.From(location), args.MoveToImmutable());
    }

    public Diagnostic ToDiagnostic()
    {
        var args = new object[MessageArgs.Length];
        for (var i = 0; i < args.Length; i++)
            args[i] = MessageArgs[i];

        return Diagnostic.Create(Descriptor, Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None, args);
    }
}
