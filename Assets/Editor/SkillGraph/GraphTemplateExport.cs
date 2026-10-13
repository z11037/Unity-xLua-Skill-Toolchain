using System.Collections.Generic;

namespace SkillGraphEditor
{
    // 导出顺序仅由连接决定，画布位置和节点列表顺序不参与执行顺序。
    public static class GraphTemplateExport
    {
        public static string Generate(GraphData graph, out UnityEngine.Object[] resources)
        {
            return GraphLuaGenerator.Generate(GraphGenerationPlan.Build(graph), out resources);
        }

        public static string GenerateDescription(GraphData graph)
        {
            return GraphDescriptionGenerator.Generate(GraphGenerationPlan.Build(graph));
        }

        // 保留旧顺序图的动作转换入口；分支图由结构化生成数据输出。
        public static List<SkillActionConfig> BuildActions(GraphData graph)
        {
            var actions = new List<SkillActionConfig>();
            foreach (GraphNode node in BuildNodes(graph))
            {
                actions.Add(GraphExportValidator.ToAction(node));
            }
            return actions;
        }

        // 保留旧顺序图的转换接口；分支图不能静默展开成线性动作列表。
        public static List<GraphNode> BuildNodes(GraphData graph)
        {
            List<GraphIssue> issues = GraphExportValidator.Validate(graph);
            if (issues.Count > 0)
            {
                throw new GraphValidationException(issues);
            }
            HashSet<string> reachable = GraphValidator.ReachableFlow(graph);
            if (graph.nodes.Exists(item => item.kind == NodeKind.If && reachable.Contains(item.id)))
            {
                throw new System.InvalidOperationException("包含分支的 Graph 不能转换为线性动作列表，请使用 Graph 的 Lua 或描述生成入口。");
            }
            var nodes = new List<GraphNode>();
            GraphNode node = graph.nodes.Find(item => item.kind == NodeKind.Entry);
            GraphEdge edge;
            while ((edge = graph.edges.Find(item => item.from == node.id && item.fromPort == GraphPorts.FlowOutput && item.toPort == GraphPorts.FlowInput)) != null)
            {
                node = graph.Find(edge.to);
                nodes.Add(node);
            }
            return nodes;
        }
    }
}
