using System.Collections.Generic;

namespace SkillGraphEditor
{
    // 仅整理本次生成的顺序和分支；直接引用节点，不复制参数或跨编辑缓存。
    public abstract class GraphGenerationStep { }

    public sealed class GraphActionStep : GraphGenerationStep
    {
        public GraphNode Node { get; }

        public GraphActionStep(GraphNode node)
        {
            Node = node;
        }
    }

    public sealed class GraphIfStep : GraphGenerationStep
    {
        public GraphNode Condition { get; }
        public IReadOnlyList<GraphGenerationStep> Satisfied { get; }
        public IReadOnlyList<GraphGenerationStep> Unsatisfied { get; }

        public GraphIfStep(GraphNode condition, IReadOnlyList<GraphGenerationStep> satisfied, IReadOnlyList<GraphGenerationStep> unsatisfied)
        {
            Condition = condition;
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
                    steps.Add(new GraphIfStep(condition, satisfied.AsReadOnly(), unsatisfied.AsReadOnly()));
                }
                else
                {
                    steps.Add(new GraphActionStep(node));
                }
                // If 的后续与顺序动作使用同一个 Flow 输出；不把两个互斥分支展开为列表。
                edge = GraphValidator.Outgoing(graph, node.id, GraphPorts.FlowOutput);
            }
            return steps;
        }
    }
}
