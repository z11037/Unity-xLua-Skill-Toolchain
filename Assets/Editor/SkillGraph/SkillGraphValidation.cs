using System;
using System.Collections.Generic;

namespace SkillGraphEditor
{
    public sealed class GraphIssue
    {
        public string Code { get; }
        public string NodeId { get; }
        public string PortId { get; }
        public string Field { get; }
        public string Message { get; }

        public GraphIssue(string code, string nodeId, string portId, string field, string message)
        {
            Code = code;
            NodeId = nodeId;
            PortId = portId;
            Field = field;
            Message = message;
        }
    }

    public sealed class GraphValidationException : InvalidOperationException
    {
        public IReadOnlyList<GraphIssue> Issues { get; }

        public GraphValidationException(IReadOnlyList<GraphIssue> issues) : base(Format(issues))
        {
            Issues = issues;
        }

        private static string Format(IReadOnlyList<GraphIssue> issues)
        {
            var messages = new List<string>();
            foreach (GraphIssue issue in issues)
            {
                messages.Add(issue.Message);
            }
            return string.Join("\n", messages);
        }
    }

    public static class GraphValidator
    {
        public static List<GraphIssue> ValidateStructure(GraphData graph)
        {
            var issues = new List<GraphIssue>();
            if (graph == null)
            {
                issues.Add(new GraphIssue("GRAPH_NULL", null, null, null, "Graph 数据为空。"));
                return issues;
            }
            if (graph.version != GraphData.CurrentVersion)
            {
                issues.Add(new GraphIssue("GRAPH_VERSION", null, null, "version", "Graph 版本不受支持。"));
            }
            if (!Guid.TryParseExact(graph.luaGuid, "N", out _))
            {
                issues.Add(new GraphIssue("LUA_GUID", null, null, "luaGuid", "Graph 的 Lua GUID 无效。"));
            }
            if (graph.nodes == null || graph.edges == null)
            {
                issues.Add(new GraphIssue("GRAPH_COLLECTION", null, null, null, "Graph 节点或连接列表为空。"));
                return issues;
            }
            var ids = new HashSet<string>();
            int entries = 0;
            foreach (GraphNode node in graph.nodes)
            {
                if (node == null)
                {
                    issues.Add(new GraphIssue("NODE_NULL", null, null, "nodes", "Graph 存在空节点。"));
                    continue;
                }
                if (!Guid.TryParseExact(node.id, "N", out _) || !ids.Add(node.id))
                {
                    issues.Add(new GraphIssue("NODE_ID", node.id, null, "id", "节点 ID 无效或重复。"));
                }
                if (!Enum.IsDefined(typeof(NodeKind), node.kind))
                {
                    issues.Add(new GraphIssue("NODE_TYPE", node.id, null, "kind", "节点类型不受支持。"));
                }
                if (!Enum.IsDefined(typeof(TargetKind), node.target))
                {
                    issues.Add(new GraphIssue("NODE_TARGET", node.id, null, "target", "节点的目标类型无效。"));
                }
                if (float.IsNaN(node.position.x) || float.IsInfinity(node.position.x)
                    || float.IsNaN(node.position.y) || float.IsInfinity(node.position.y))
                {
                    issues.Add(new GraphIssue("NODE_POSITION", node.id, null, "position", "节点坐标必须是有限数值。"));
                }
                if (node.kind == NodeKind.Entry)
                {
                    entries++;
                }
            }
            if (entries != 1)
            {
                issues.Add(new GraphIssue("ENTRY_COUNT", null, null, "nodes", "Graph 必须恰好包含一个入口。"));
            }
            var counts = new Dictionary<string, int>();
            foreach (GraphEdge edge in graph.edges)
            {
                if (edge == null)
                {
                    issues.Add(new GraphIssue("EDGE_NULL", null, null, "edges", "Graph 存在空连接。"));
                    continue;
                }
                List<GraphIssue> edgeIssues = ValidateEndpoints(graph, edge.from, edge.fromPort, edge.to, edge.toPort);
                issues.AddRange(edgeIssues);
                if (edgeIssues.Count == 0)
                {
                    CheckCount(graph, edge.from, edge.fromPort, counts, issues);
                    CheckCount(graph, edge.to, edge.toPort, counts, issues);
                }
            }
            // 端点有效后再检查 Flow 环，避免损坏数据引发后续访问异常。
            if (issues.Count == 0)
            {
                CheckFlowCycles(graph, issues);
            }
            return issues;
        }

        public static List<GraphIssue> ValidateConnection(GraphData graph, string from, string fromPort, string to, string toPort)
        {
            List<GraphIssue> issues = ValidateEndpoints(graph, from, fromPort, to, toPort);
            if (issues.Count > 0)
            {
                return issues;
            }
            GraphPortDefinition source = GraphPorts.Find(graph.Find(from).kind, fromPort);
            GraphPortDefinition destination = GraphPorts.Find(graph.Find(to).kind, toPort);
            int outputs = 0;
            int inputs = 0;
            foreach (GraphEdge edge in graph.edges)
            {
                if (edge != null && edge.from == from && edge.fromPort == fromPort)
                {
                    outputs++;
                }
                if (edge != null && edge.to == to && edge.toPort == toPort)
                {
                    inputs++;
                }
            }
            if (outputs >= source.MaxConnections)
            {
                issues.Add(new GraphIssue("PORT_OCCUPIED", from, fromPort, null, "输出端口已占用，请先断开原连接。"));
            }
            if (inputs >= destination.MaxConnections)
            {
                issues.Add(new GraphIssue("PORT_OCCUPIED", to, toPort, null, "输入端口已占用，请先断开原连接。"));
            }
            if (source.Type == PortType.Flow && ReachableFrom(graph, to).Contains(from))
            {
                issues.Add(new GraphIssue("FLOW_CYCLE", from, fromPort, null, "连接会形成执行循环。"));
            }
            return issues;
        }

        public static HashSet<string> ReachableFlow(GraphData graph)
        {
            GraphNode entry = graph.nodes.Find(node => node != null && node.kind == NodeKind.Entry);
            return entry == null ? new HashSet<string>() : ReachableFrom(graph, entry.id);
        }

        private static List<GraphIssue> ValidateEndpoints(GraphData graph, string from, string fromPort, string to, string toPort)
        {
            var issues = new List<GraphIssue>();
            if (graph == null || graph.nodes == null || graph.edges == null)
            {
                issues.Add(new GraphIssue("GRAPH_COLLECTION", null, null, null, "Graph 节点或连接列表无效。"));
                return issues;
            }
            GraphNode source = graph.Find(from);
            GraphNode destination = graph.Find(to);
            if (source == null)
            {
                issues.Add(new GraphIssue("EDGE_NODE", from, fromPort, null, "连接的来源节点不存在。"));
            }
            if (destination == null)
            {
                issues.Add(new GraphIssue("EDGE_NODE", to, toPort, null, "连接的目标节点不存在。"));
            }
            if (issues.Count > 0)
            {
                return issues;
            }
            if (from == to)
            {
                issues.Add(new GraphIssue("EDGE_SELF", from, fromPort, null, "节点不能连接自身。"));
            }
            GraphPortDefinition output = GraphPorts.Find(source.kind, fromPort);
            GraphPortDefinition input = GraphPorts.Find(destination.kind, toPort);
            if (output == null)
            {
                issues.Add(new GraphIssue("PORT_MISSING", from, fromPort, null, "来源端口缺失或不存在。"));
            }
            if (input == null)
            {
                issues.Add(new GraphIssue("PORT_MISSING", to, toPort, null, "目标端口缺失或不存在。"));
            }
            if (output == null || input == null)
            {
                return issues;
            }
            if (output.Direction != PortDirection.Output || input.Direction != PortDirection.Input)
            {
                issues.Add(new GraphIssue("PORT_DIRECTION", to, toPort, null, "连接必须从输出端口指向输入端口。"));
            }
            else if (!GraphPorts.AreCompatible(output, input))
            {
                issues.Add(new GraphIssue("PORT_TYPE", to, toPort, null, "连接两端的端口类型不匹配。"));
            }
            return issues;
        }

        private static void CheckCount(GraphData graph, string nodeId, string portId, Dictionary<string, int> counts, List<GraphIssue> issues)
        {
            string key = nodeId + ":" + portId;
            counts.TryGetValue(key, out int count);
            counts[key] = ++count;
            if (count > GraphPorts.Find(graph.Find(nodeId).kind, portId).MaxConnections)
            {
                issues.Add(new GraphIssue("PORT_OCCUPIED", nodeId, portId, null, "端口连接数量超过限制。"));
            }
        }

        private static HashSet<string> ReachableFrom(GraphData graph, string start)
        {
            var reached = new HashSet<string>();
            var pending = new Queue<string>();
            var adjacency = FlowAdjacency(graph);
            pending.Enqueue(start);
            while (pending.Count > 0)
            {
                string id = pending.Dequeue();
                if (!reached.Add(id) || !adjacency.TryGetValue(id, out List<string> targets))
                {
                    continue;
                }
                foreach (string target in targets)
                {
                    pending.Enqueue(target);
                }
            }
            return reached;
        }

        private static Dictionary<string, List<string>> FlowAdjacency(GraphData graph)
        {
            var adjacency = new Dictionary<string, List<string>>();
            foreach (GraphEdge edge in graph.edges)
            {
                GraphNode node = edge == null ? null : graph.Find(edge.from);
                GraphPortDefinition port = node == null ? null : GraphPorts.Find(node.kind, edge.fromPort);
                if (port == null || port.Type != PortType.Flow || edge.to == null)
                {
                    continue;
                }
                if (!adjacency.TryGetValue(edge.from, out List<string> targets))
                {
                    targets = new List<string>();
                    adjacency.Add(edge.from, targets);
                }
                targets.Add(edge.to);
            }
            return adjacency;
        }

        private static void CheckFlowCycles(GraphData graph, List<GraphIssue> issues)
        {
            var adjacency = FlowAdjacency(graph);
            var degrees = new Dictionary<string, int>();
            foreach (GraphNode node in graph.nodes)
            {
                degrees.Add(node.id, 0);
            }
            foreach (List<string> targets in adjacency.Values)
            {
                foreach (string target in targets)
                {
                    degrees[target]++;
                }
            }
            var pending = new Queue<string>();
            foreach (KeyValuePair<string, int> item in degrees)
            {
                if (item.Value == 0)
                {
                    pending.Enqueue(item.Key);
                }
            }
            while (pending.Count > 0)
            {
                string id = pending.Dequeue();
                if (!adjacency.TryGetValue(id, out List<string> targets))
                {
                    continue;
                }
                foreach (string target in targets)
                {
                    if (--degrees[target] == 0)
                    {
                        pending.Enqueue(target);
                    }
                }
            }
            foreach (KeyValuePair<string, int> item in degrees)
            {
                if (item.Value > 0)
                {
                    issues.Add(new GraphIssue("FLOW_CYCLE", item.Key, GraphPorts.FlowInput, null, "节点属于执行循环，无法确定执行顺序。"));
                }
            }
        }
    }
}
