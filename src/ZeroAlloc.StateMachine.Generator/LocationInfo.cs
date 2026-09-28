namespace ZeroAlloc.StateMachine.Generator;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// A diagnostic location the pipeline can cache: the syntax tree and the span within it.
/// </summary>
/// <remarks>
/// <para>
/// The tree is kept, not just its file path, because the rebuilt diagnostic must be a source
/// location. <c>Location.Create(filePath, span, lineSpan)</c> gives an external-file location
/// with no <see cref="Location.SourceTree"/>, and the compiler then ignores
/// <c>#pragma warning disable</c> for it.
/// </para>
/// <para>
/// Keeping the tree does not defeat caching. <see cref="SyntaxTree"/> compares by reference, and
/// a compilation reuses the tree instance of every file that did not change, so the location
/// compares equal across runs until its own file is edited, when the model is rebuilt anyway. A
/// tree belongs to no one compilation, and only the tree of the latest run is held.
/// </para>
/// </remarks>
internal sealed record LocationInfo(SyntaxTree Tree, TextSpan Span)
{
    public Location ToLocation() => Location.Create(Tree, Span);

    /// <summary>
    /// Null for <see cref="Location.None"/>, a missing location, or one outside source, which
    /// report as <see cref="Location.None"/>. Every location the generator reports is in source.
    /// </summary>
    public static LocationInfo? From(Location? location) =>
        location?.SourceTree is { } tree ? new LocationInfo(tree, location.SourceSpan) : null;
}
