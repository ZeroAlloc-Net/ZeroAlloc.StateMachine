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
    /// <param name="m">The state-machine model to render.</param>
    /// <param name="resolveSubMachine">
    /// Resolver invoked for each composite state's <see cref="CompositeStateModel.SubMachineFqn"/>.
    /// Returns a <see cref="StateMachineModel"/> for the sub-FSM, or <c>null</c> to skip that
    /// composite. May be <c>null</c> to render the parent without expanding any composites.
    /// </param>
    public static string Write(StateMachineModel m, System.Func<string, StateMachineModel?>? resolveSubMachine)
    {
        var sb = new StringBuilder();
        sb.AppendLine("stateDiagram-v2");

        WriteIndented(sb, m, indent: "    ", resolveSubMachine);

        return sb.ToString().TrimEnd();
    }

    private static void WriteIndented(StringBuilder sb, StateMachineModel m, string indent, System.Func<string, StateMachineModel?>? resolveSubMachine)
    {
        // Initial-state marker.
        sb.Append(indent).Append("[*] --> ").AppendLine(m.InitialState);

        WriteTransitions(sb, m, indent);
        WriteTerminals(sb, m, indent);
        WriteComposites(sb, m, indent, resolveSubMachine);
    }

    private static void WriteTransitions(StringBuilder sb, StateMachineModel m, string indent)
    {
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
    }

    private static void WriteTerminals(StringBuilder sb, StateMachineModel m, string indent)
    {
        foreach (var s in m.TerminalStates)
        {
            sb.Append(indent).Append(s).AppendLine(" --> [*]");
        }
    }

    private static void WriteComposites(StringBuilder sb, StateMachineModel m, string indent, System.Func<string, StateMachineModel?>? resolveSubMachine)
    {
        if (resolveSubMachine is null) return;

        foreach (var c in m.CompositeStates)
        {
            var subModel = resolveSubMachine(c.SubMachineFqn);
            if (subModel is null) continue;

            sb.Append(indent).Append("state ").Append(c.State).AppendLine(" {");
            WriteIndented(sb, subModel, indent + "    ", resolveSubMachine);
            sb.Append(indent).AppendLine("}");
        }
    }
}
