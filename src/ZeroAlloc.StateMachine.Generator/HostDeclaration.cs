namespace ZeroAlloc.StateMachine.Generator;

using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// The partial declarations a host's generated members are written into: those of its
/// containing types, outermost first, then the host's own. Kept as strings, so the model
/// holding it stays cacheable.
/// </summary>
/// <param name="ContainingTypes">
/// One header per containing type, outermost first, such as <c>partial record struct Outer&lt;T&gt;</c>.
/// Empty for a host at the top of a namespace.
/// </param>
/// <param name="Keyword">The host's own header keywords: <c>partial class</c> or <c>partial struct</c>.</param>
/// <param name="Name">The host's name, escaped where it is a keyword, as in <c>@class</c>. Names its constructors.</param>
/// <param name="TypeParameters">The host's type parameter list, as in <c>&lt;TX, TY&gt;</c>, or empty.</param>
internal sealed record HostDeclaration(
    EquatableArray<string> ContainingTypes,
    string Keyword,
    string Name,
    string TypeParameters)
{
    /// <summary>The host as it refers to itself, as in <c>M&lt;TX&gt;</c>.</summary>
    public string SelfType => Name + TypeParameters;

    public static HostDeclaration For(INamedTypeSymbol host)
    {
        var containing = ImmutableArray.CreateBuilder<string>();
        for (var t = host.ContainingType; t is not null; t = t.ContainingType)
            containing.Insert(0, HeaderKeyword(t, withRef: true) + " " + Identifier(t.Name) + TypeParameterList(t));

        return new HostDeclaration(
            containing.ToImmutable(), HeaderKeyword(host, withRef: false), Identifier(host.Name), TypeParameterList(host));
    }

    /// <summary>
    /// Opens the containing types' declarations, outermost first. The caller writes the host's
    /// own declaration and body, then calls <see cref="Close"/>.
    /// </summary>
    public void Open(StringBuilder sb)
    {
        foreach (var header in ContainingTypes)
        {
            sb.AppendLine(header);
            sb.AppendLine("{");
        }
    }

    public void Close(StringBuilder sb)
    {
        for (var i = 0; i < ContainingTypes.Length; i++)
            sb.AppendLine("}");
    }

    /// <summary>
    /// The outermost containing type of <paramref name="host"/> that is not declared
    /// <c>partial</c>, or null when all of them are. The generated code has to reopen every
    /// containing type, which only a partial type allows.
    /// </summary>
    public static INamedTypeSymbol? FirstNonPartialContainingType(INamedTypeSymbol host)
    {
        INamedTypeSymbol? outermost = null;
        for (var t = host.ContainingType; t is not null; t = t.ContainingType)
        {
            if (!IsPartial(t)) outermost = t;
        }
        return outermost;
    }

    /// <summary>
    /// The host itself or the containing type that is file-local, or null. Only a top-level type
    /// can be declared <c>file</c>, and a generated file can never reopen it.
    /// </summary>
    public static INamedTypeSymbol? FileLocalType(INamedTypeSymbol host)
    {
        for (INamedTypeSymbol? t = host; t is not null; t = t.ContainingType)
        {
            if (t.IsFileLocal) return t;
        }
        return null;
    }

    private static bool IsPartial(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences.Any(static r =>
            r.GetSyntax() is TypeDeclarationSyntax declaration &&
            declaration.Modifiers.Any(static m => m.IsKind(SyntaxKind.PartialKeyword)));

    // Accessibility, `static`, `readonly`, `ref` and constraints can all be left out of a
    // partial part. The host's own header leaves `ref` out, as it always has, so a top-level
    // host's output is unchanged; a containing type keeps it, as in ZeroAlloc.Mapping. `ref` has
    // to come before `partial`.
    private static string HeaderKeyword(INamedTypeSymbol type, bool withRef)
    {
        var kind = type switch
        {
            { IsRecord: true, TypeKind: TypeKind.Struct } => "record struct",
            { IsRecord: true } => "record",
            { TypeKind: TypeKind.Struct } => "struct",
            { TypeKind: TypeKind.Interface } => "interface",
            _ => "class",
        };
        return (withRef && type.IsRefLikeType ? "ref partial " : "partial ") + kind;
    }

    /// <summary>
    /// Type parameter names only. Variance never appears here: an interface with a variant
    /// type parameter cannot contain types.
    /// </summary>
    private static string TypeParameterList(INamedTypeSymbol type) =>
        type.TypeParameters.IsEmpty
            ? string.Empty
            : "<" + string.Join(", ", type.TypeParameters.Select(static p => Identifier(p.Name))) + ">";

    private static string Identifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
}
