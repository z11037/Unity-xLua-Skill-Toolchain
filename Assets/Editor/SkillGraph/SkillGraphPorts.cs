using System;
using System.Collections.Generic;

namespace SkillGraphEditor
{
    public enum PortType { Flow, Value, Condition }
    public enum PortDirection { Input, Output }
    public enum PortSide { Left, Right, Top, Bottom }

    // 端口由节点类型定义，图文件只保存稳定的端口 ID。
    public sealed class GraphPortDefinition
    {
        public string Id { get; }
        public PortDirection Direction { get; }
        public PortType Type { get; }
        public int MaxConnections { get; }
        public PortSide Side { get; }

        public GraphPortDefinition(string id, PortDirection direction, PortType type, int maxConnections, PortSide? side = null)
        {
            if (string.IsNullOrWhiteSpace(id) || !Enum.IsDefined(typeof(PortDirection), direction)
                || !Enum.IsDefined(typeof(PortType), type) || maxConnections < 1
                || (side.HasValue && !Enum.IsDefined(typeof(PortSide), side.Value)))
            {
                throw new ArgumentException("端口定义的 ID、方向、类型或连接数量无效。");
            }
            Id = id;
            Direction = direction;
            Type = type;
            MaxConnections = maxConnections;
            Side = side ?? (direction == PortDirection.Input ? PortSide.Left : PortSide.Right);
        }
    }

    public static class GraphPorts
    {
        public const string FlowInput = "flow-in";
        public const string FlowOutput = "flow-out";
        public const string FlowTrue = "flow-true";
        public const string FlowFalse = "flow-false";
        public const string ConditionInput = "condition-in";
        public const string ConditionOutput = "condition-out";
        private static readonly GraphPortDefinition input = new GraphPortDefinition(FlowInput, PortDirection.Input, PortType.Flow, 1);
        private static readonly GraphPortDefinition output = new GraphPortDefinition(FlowOutput, PortDirection.Output, PortType.Flow, 1);
        private static readonly IReadOnlyList<GraphPortDefinition> entry = Array.AsReadOnly(new[] { output });
        private static readonly IReadOnlyList<GraphPortDefinition> action = Array.AsReadOnly(new[] { input, output });
        private static readonly IReadOnlyList<GraphPortDefinition> branch = Array.AsReadOnly(new[]
        {
            input,
            new GraphPortDefinition(ConditionInput, PortDirection.Input, PortType.Condition, 1, PortSide.Top),
            new GraphPortDefinition(FlowTrue, PortDirection.Output, PortType.Flow, 1, PortSide.Bottom),
            new GraphPortDefinition(FlowFalse, PortDirection.Output, PortType.Flow, 1, PortSide.Bottom),
            output
        });
        private static readonly IReadOnlyList<GraphPortDefinition> condition = Array.AsReadOnly(new[]
        {
            new GraphPortDefinition(ConditionOutput, PortDirection.Output, PortType.Condition, 1)
        });
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
                case NodeKind.If:
                    return branch;
                case NodeKind.HealthBelow:
                    return condition;
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
