using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkillGraphEditor
{
    public enum NodeKind { Entry, Damage, ApplyBuff }
    public enum TargetKind { CurrentTarget, Self }

    [Serializable]
    public sealed class GraphNode
    {
        public string id;
        public NodeKind kind;
        public Vector2 position;
        public TargetKind target;
        public int damage = 10;
        public string buffGuid = "";
    }

    [Serializable]
    public sealed class GraphEdge
    {
        public string from;
        public string fromPort;
        public string to;
        public string toPort;
    }

    [Serializable]
    public sealed class GraphData
    {
        public const int CurrentVersion = 2;
        public int version = CurrentVersion;
        public string luaGuid;
        public List<GraphNode> nodes = new List<GraphNode>();
        public List<GraphEdge> edges = new List<GraphEdge>();

        public static GraphData Create(string luaGuid)
        {
            var graph = new GraphData { luaGuid = luaGuid };
            graph.Add(NodeKind.Entry, new Vector2(60, 80));
            return graph;
        }

        public GraphNode Find(string id)
        {
            return nodes == null ? null : nodes.Find(node => node != null && node.id == id);
        }

        public GraphNode Add(NodeKind kind, Vector2 position)
        {
            if (!Enum.IsDefined(typeof(NodeKind), kind) || (kind == NodeKind.Entry && nodes.Exists(node => node.kind == NodeKind.Entry)))
            {
                throw new InvalidOperationException("节点类型无效或入口已存在。");
            }
            var node = new GraphNode { id = Guid.NewGuid().ToString("N"), kind = kind, position = position };
            nodes.Add(node);
            return node;
        }

        public bool Remove(string id)
        {
            GraphNode node = Find(id);
            if (node == null || node.kind == NodeKind.Entry)
            {
                return false;
            }
            edges.RemoveAll(edge => edge.from == id || edge.to == id);
            return nodes.Remove(node);
        }

        public bool CanConnect(string from, string to, out string error)
        {
            return CanConnect(from, GraphPorts.FlowOutput, to, GraphPorts.FlowInput, out error);
        }

        public bool CanConnect(string from, string fromPort, string to, string toPort, out string error)
        {
            List<GraphIssue> issues = GraphValidator.ValidateConnection(this, from, fromPort, to, toPort);
            error = issues.Count == 0 ? null : issues[0].Message;
            return issues.Count == 0;
        }

        public bool Connect(string from, string to, out string error)
        {
            return Connect(from, GraphPorts.FlowOutput, to, GraphPorts.FlowInput, out error);
        }

        public bool Connect(string from, string fromPort, string to, string toPort, out string error)
        {
            if (!CanConnect(from, fromPort, to, toPort, out error))
            {
                return false;
            }
            edges.Add(new GraphEdge { from = from, fromPort = fromPort, to = to, toPort = toPort });
            return true;
        }

        // 只校验存储结构，未连接节点和未填写的参数允许作为草稿保存。
        public void ValidateStructure()
        {
            List<GraphIssue> issues = GraphValidator.ValidateStructure(this);
            if (issues.Count > 0)
            {
                throw new GraphValidationException(issues);
            }
        }
    }
}
