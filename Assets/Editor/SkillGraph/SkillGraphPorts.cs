using System;
using System.Collections.Generic;

namespace SkillGraphEditor
{
    public enum PortType { Flow, Value, Condition }
    public enum PortDirection { Input, Output }

    // 端口由节点类型定义，图文件只保存稳定的端口 ID。
    public sealed class GraphPortDefinition
    {
        public string Id { get; }
        public PortDirection Direction { get; }
        public PortType Type { get; }
        public int MaxConnections { get; }

        public GraphPortDefinition(string id, PortDirection direction, PortType type, int maxConnections)
        {
            if (string.IsNullOrWhiteSpace(id) || !Enum.IsDefined(typeof(PortDirection), direction)
                || !Enum.IsDefined(typeof(PortType), type) || maxConnections < 1)
            {
                throw new ArgumentException("端口定义的 ID、方向、类型或连接数量无效。");
            }
            Id = id;
            Direction = direction;
            Type = type;
            MaxConnections = maxConnections;
        }
    }

    public static class GraphPorts
    {
        public const string FlowInput = "flow-in";
        public const string FlowOutput = "flow-out";
        private static readonly GraphPortDefinition input = new GraphPortDefinition(FlowInput, PortDirection.Input, PortType.Flow, 1);
        private static readonly GraphPortDefinition output = new GraphPortDefinition(FlowOutput, PortDirection.Output, PortType.Flow, 1);
        private static readonly IReadOnlyList<GraphPortDefinition> entry = Array.AsReadOnly(new[] { output });
        private static readonly IReadOnlyList<GraphPortDefinition> action = Array.AsReadOnly(new[] { input, output });
        private static readonly IReadOnlyList<GraphPortDefinition> empty = Array.AsReadOnly(new GraphPortDefinition[0]);

        public static IReadOnlyList<GraphPortDefinition> For(NodeKind kind)
        {
            switch (kind)
            {
                case NodeKind.Entry:
                    return entry;
                case NodeKind.Damage:
                case NodeKind.ApplyBuff:
                    return action;
                default:
                    return empty;
            }
        }

        public static GraphPortDefinition Find(NodeKind kind, string id)
        {
            foreach (GraphPortDefinition port in For(kind))
            {
                if (port.Id == id)
                {
                    return port;
                }
            }
            return null;
        }

        public static bool AreCompatible(GraphPortDefinition from, GraphPortDefinition to)
        {
            return from != null && to != null && from.Direction == PortDirection.Output
                && to.Direction == PortDirection.Input && from.Type == to.Type;
        }
    }
}
