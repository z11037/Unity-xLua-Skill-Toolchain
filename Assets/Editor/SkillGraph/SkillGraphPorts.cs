using System;
using UnityEngine;

namespace SkillGraphEditor
{
    public enum PortType { Flow, Value, Condition }
    public enum PortDirection { Input, Output }
    public enum PortSide { Left, Right, Top, Bottom }

    // 端口由节点类型定义，图文件只保存稳定的端口 ID。
    public sealed class GraphPortDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public PortDirection Direction { get; }
        public PortType Type { get; }
        public int MaxConnections { get; }
        public PortSide Side { get; }
        public Rect Bounds { get; }
        public string Caption { get; }
        public Rect CaptionBounds { get; }

        public GraphPortDefinition(string id, string displayName, PortDirection direction, PortType type, int maxConnections, PortSide side, Rect bounds, string caption = null, Rect captionBounds = default)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(displayName) || !Enum.IsDefined(typeof(PortDirection), direction)
                || !Enum.IsDefined(typeof(PortType), type) || maxConnections < 1
                || !Enum.IsDefined(typeof(PortSide), side) || bounds.width <= 0 || bounds.height <= 0)
            {
                throw new ArgumentException("端口定义的 ID、方向、类型或连接数量无效。");
            }
            Id = id;
            DisplayName = displayName;
            Direction = direction;
            Type = type;
            MaxConnections = maxConnections;
            Side = side;
            Bounds = bounds;
            Caption = caption;
            CaptionBounds = captionBounds;
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

        public static bool AreCompatible(GraphPortDefinition from, GraphPortDefinition to)
        {
            return from != null && to != null && from.Direction == PortDirection.Output
                && to.Direction == PortDirection.Input && from.Type == to.Type;
        }
    }
}
