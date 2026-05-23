namespace ZeroAlloc.StateMachine.Generator;

using System.Linq;
using System.Text;

/// <summary>
/// Emits Mermaid stateDiagram-v2 body content from a <see cref="StateMachineModel"/> or
/// <see cref="StateMachineGroupModel"/>. The output is the diagram body only — callers wrap
/// it in a <c>public const string MermaidDiagram = "..."</c> literal in the generated partial.
/// </summary>
internal static class MermaidDiagramWriter
{
    /// <summary>Emit a Mermaid stateDiagram-v2 body for a single-machine model.</summary>
    public static string Write(StateMachineModel m)
    {
        var sb = new StringBuilder();
        sb.AppendLine("stateDiagram-v2");

        WriteIndented(sb, m, indent: "    ");

        return sb.ToString().TrimEnd();
    }

    private static void WriteIndented(StringBuilder sb, StateMachineModel m, string indent)
    {
        // Initial-state marker.
        sb.Append(indent).Append("[*] --> ").AppendLine(m.InitialState);

        // Transitions.
        foreach (var t in m.Transitions)
        {
            sb.Append(indent);
            sb.Append(t.From).Append(" --> ").Append(t.To).Append(": ").Append(t.On);
            if (t.AfterMs > 0)
                sb.Append(" (after ").Append(t.AfterMs).Append("ms)");
            if (t.HasGuard)
                sb.Append(" [guard]");
            sb.AppendLine();
        }

        // Terminal states.
        foreach (var s in m.TerminalStates)
        {
            sb.Append(indent).Append(s).AppendLine(" --> [*]");
        }
    }
}
