using System.Collections.Generic;

namespace SkillGraphEditor
{
    // 导出顺序仅由连接决定，画布位置和节点列表顺序不参与执行顺序。
    public static class GraphTemplateExport
    {
        public static string Generate(GraphData graph, out UnityEngine.Object[] resources)
        {
            List<GraphIssue> issues = GraphExportValidator.Validate(graph);
            if (issues.Count > 0)
            {
                throw new GraphValidationException(issues);
            }
            var actions = new List<SkillActionConfig>();
            GraphNode node = graph.nodes.Find(item => item.kind == NodeKind.Entry);
            GraphEdge edge;
            while ((edge = graph.edges.Find(item => item.from == node.id && item.fromPort == GraphPorts.FlowOutput && item.toPort == GraphPorts.FlowInput)) != null)
            {
                node = graph.Find(edge.to);
                actions.Add(GraphExportValidator.ToAction(node));
            }
            return ActionLuaGenerator.Generate(actions, out resources);
        }
    }
}
