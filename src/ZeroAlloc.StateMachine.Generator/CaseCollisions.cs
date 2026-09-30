namespace ZeroAlloc.StateMachine.Generator;

using System.Collections.Generic;
using System.Collections.Immutable;

/// <summary>
/// The hosts whose hint names differ only in case from an earlier host's, which Roslyn would
/// reject as duplicates, and the ZSM0025 errors about them.
/// </summary>
internal sealed record CaseCollisions(
    EquatableArray<string> SkippedHintNames,
    EquatableArray<DiagnosticInfo> Diagnostics)
{
    /// <summary>
    /// Groups the generated hosts by hint name as Roslyn compares them, ignoring case. In each
    /// group the host declared first, by file path and then position, keeps its file; every
    /// later host is skipped and reported. The order does not depend on the order of the
    /// compilation's files, so the same host is generated on every run.
    /// </summary>
    public static CaseCollisions Find(ImmutableArray<GeneratedHost> hosts)
    {
        var ordered = hosts.ToArray();
        System.Array.Sort(ordered, CompareDeclarationOrder);

        var first = new Dictionary<string, GeneratedHost>(System.StringComparer.OrdinalIgnoreCase);
        var skipped = ImmutableArray.CreateBuilder<string>();
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        foreach (var host in ordered)
        {
            if (!first.TryGetValue(host.HintName, out var earlier))
            {
                first.Add(host.HintName, host);
                continue;
            }

            skipped.Add(host.HintName);
            diagnostics.Add(new DiagnosticInfo(
                StateMachineDiagnostics.HostNameDiffersOnlyInCase,
                host.Location,
                ImmutableArray.Create(host.DisplayName, host.HintName, earlier.DisplayName)));
        }

        return new CaseCollisions(skipped.ToImmutable(), diagnostics.ToImmutable());
    }

    private static int CompareDeclarationOrder(GeneratedHost x, GeneratedHost y)
    {
        var byPath = string.CompareOrdinal(x.Location?.Tree.FilePath, y.Location?.Tree.FilePath);
        if (byPath != 0) return byPath;
        var byPosition = (x.Location?.Span.Start ?? 0).CompareTo(y.Location?.Span.Start ?? 0);
        return byPosition != 0 ? byPosition : string.CompareOrdinal(x.HintName, y.HintName);
    }
}
