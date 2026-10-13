using System.Collections.Generic;

namespace SkillGraphEditor
{
    // 仅供两个生成器使用，不保存文件，也不承担运行时执行。
    public abstract class GraphGenerationStep { }

    public sealed class GraphActionStep : GraphGenerationStep
    {
        public SkillActionConfig Action { get; }

        public GraphActionStep(SkillActionConfig action)
        {
            Action = action;
        }
    }

    public sealed class GraphIfStep : GraphGenerationStep
    {
        public TargetKind Target { get; }
        public float HealthPercent { get; }
        public IReadOnlyList<GraphGenerationStep> Satisfied { get; }
        public IReadOnlyList<GraphGenerationStep> Unsatisfied { get; }

        public GraphIfStep(TargetKind target, float healthPercent, IReadOnlyList<GraphGenerationStep> satisfied, IReadOnlyList<GraphGenerationStep> unsatisfied)
        {
            Target = target;
            HealthPercent = healthPercent;
            Satisfied = satisfied;
            Unsatisfied = unsatisfied;
        }
    }

    public sealed class GraphGenerationPlan
    {
        public IReadOnlyList<GraphGenerationStep> Steps { get; }

        private GraphGenerationPlan(List<GraphGenerationStep> steps)
        {
            Steps = steps.AsReadOnly();
        }

        public static GraphGenerationPlan Build(GraphData graph)
        {
            List<GraphIssue> issues = GraphExportValidator.Validate(graph);
            if (issues.Count > 0)
            {
                throw new GraphValidationException(issues);
            }
            GraphNode entry = graph.nodes.Find(node => node.kind == NodeKind.Entry);
            return new GraphGenerationPlan(BuildSequence(graph, GraphValidator.Outgoing(graph, entry.id, GraphPorts.FlowOutput)));
        }

        private static List<GraphGenerationStep> BuildSequence(GraphData graph, GraphEdge edge)
        {
            var steps = new List<GraphGenerationStep>();
            while (edge != null)
            {
                GraphNode node = graph.Find(edge.to);
                if (node.kind == NodeKind.If)
                {
                    GraphEdge conditionEdge = graph.edges.Find(item => item.to == node.id && item.toPort == GraphPorts.ConditionInput);
                    GraphNode condition = graph.Find(conditionEdge.from);
                    List<GraphGenerationStep> satisfied = BuildSequence(graph, GraphValidator.Outgoing(graph, node.id, GraphPorts.FlowTrue));
                    List<GraphGenerationStep> unsatisfied = BuildSequence(graph, GraphValidator.Outgoing(graph, node.id, GraphPorts.FlowFalse));
                    steps.Add(new GraphIfStep(condition.target, condition.healthPercent, satisfied.AsReadOnly(), unsatisfied.AsReadOnly()));
                }
                else
                {
                    steps.Add(new GraphActionStep(GraphExportValidator.ToAction(node)));
                }
                // If 的后续与顺序动作使用同一个 Flow 输出；不把两个互斥分支展开为列表。
                edge = GraphValidator.Outgoing(graph, node.id, GraphPorts.FlowOutput);
            }
            return steps;
        }
    }
}
