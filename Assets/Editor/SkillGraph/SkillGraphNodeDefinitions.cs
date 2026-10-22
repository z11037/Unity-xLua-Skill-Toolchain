using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkillGraphEditor
{
    // 只保存编辑器的静态定义；节点参数仍由 GraphNode 保存。
    public sealed class GraphNodeDefinition
    {
        public NodeKind Kind { get; }
        public string DisplayName { get; }
        public string PaletteDescription { get; }
        public bool ShowInPalette { get; }
        public Vector2 Size { get; }
        public Rect SummaryBounds { get; }
        public IReadOnlyList<GraphPortDefinition> Ports { get; }

        public GraphNodeDefinition(NodeKind kind, string displayName, string paletteDescription, bool showInPalette, Vector2 size, Rect summaryBounds, params GraphPortDefinition[] ports)
        {
            if (!Enum.IsDefined(typeof(NodeKind), kind) || string.IsNullOrWhiteSpace(displayName)
                || size.x <= 0 || size.y <= 0 || ports == null)
            {
                throw new ArgumentException("节点定义的类型、名称、尺寸或端口无效。");
            }
            var ids = new HashSet<string>();
            foreach (GraphPortDefinition port in ports)
            {
                if (port == null || !ids.Add(port.Id))
                {
                    throw new ArgumentException("节点定义包含空端口或重复的端口 ID。");
                }
            }
            Kind = kind;
            DisplayName = displayName;
            PaletteDescription = paletteDescription;
            ShowInPalette = showInPalette;
            Size = size;
            SummaryBounds = summaryBounds;
            Ports = Array.AsReadOnly((GraphPortDefinition[])ports.Clone());
        }

        public GraphPortDefinition FindPort(string id)
        {
            foreach (GraphPortDefinition port in Ports)
            {
                if (port.Id == id)
                {
                    return port;
                }
            }
            return null;
        }
    }

    public static class GraphNodeDefinitions
    {
        // 列表同时决定节点面板顺序；同一份端口定义供绘制和连线校验使用。
        public static IReadOnlyList<GraphNodeDefinition> All { get; } = CreateDefinitions();
        private static readonly Dictionary<NodeKind, GraphNodeDefinition> definitions = CreateLookup();

        public static GraphNodeDefinition Get(NodeKind kind)
        {
            if (!TryGet(kind, out GraphNodeDefinition definition))
            {
                throw new InvalidOperationException("节点类型尚未定义：" + kind);
            }
            return definition;
        }

        public static bool TryGet(NodeKind kind, out GraphNodeDefinition definition)
        {
            return definitions.TryGetValue(kind, out definition);
        }

        public static GraphPortDefinition FindPort(NodeKind kind, string id)
        {
            return TryGet(kind, out GraphNodeDefinition definition) ? definition.FindPort(id) : null;
        }

        private static IReadOnlyList<GraphNodeDefinition> CreateDefinitions()
        {
            var size = new Vector2(180, 100);
            var summary = new Rect(10, 23, 165, 20);
            var input = new GraphPortDefinition(GraphPorts.FlowInput, "执行流程输入", PortDirection.Input, PortType.Flow, 1, PortSide.Left, new Rect(5, 55, 30, 25));
            var output = new GraphPortDefinition(GraphPorts.FlowOutput, "执行流程输出", PortDirection.Output, PortType.Flow, 1, PortSide.Right, new Rect(145, 55, 30, 25));
            return Array.AsReadOnly(new[]
            {
                new GraphNodeDefinition(NodeKind.Entry, "入口", "", false, size, summary, output),
                new GraphNodeDefinition(NodeKind.Damage, "伤害", "▶ 固定数值", true, size, summary, input, output),
                new GraphNodeDefinition(NodeKind.ApplyBuff, "施加 Buff", "▶ 引用配置", true, size, summary, input, output),
                new GraphNodeDefinition(NodeKind.HealthBelow, "生命比例条件", "▶ 低于指定百分比", true, size, summary,
                    new GraphPortDefinition(GraphPorts.ConditionOutput, "条件输出", PortDirection.Output, PortType.Condition, 1, PortSide.Right, new Rect(145, 55, 30, 25))),
                new GraphNodeDefinition(NodeKind.If, "If 条件分支", "▼ 分支　▶ 后续", true, new Vector2(180, 170), new Rect(10, 48, 165, 20),
                    new GraphPortDefinition(GraphPorts.FlowInput, "执行流程输入", PortDirection.Input, PortType.Flow, 1, PortSide.Left, new Rect(5, 72, 30, 25)),
                    new GraphPortDefinition(GraphPorts.ConditionInput, "条件输入", PortDirection.Input, PortType.Condition, 1, PortSide.Top, new Rect(75, 24, 30, 24), "条件", new Rect(110, 25, 60, 20)),
                    new GraphPortDefinition(GraphPorts.FlowTrue, "满足分支输出", PortDirection.Output, PortType.Flow, 1, PortSide.Bottom, new Rect(30, 140, 30, 25), "满足", new Rect(25, 117, 50, 20)),
                    new GraphPortDefinition(GraphPorts.FlowFalse, "不满足分支输出", PortDirection.Output, PortType.Flow, 1, PortSide.Bottom, new Rect(120, 140, 30, 25), "不满足", new Rect(117, 117, 60, 20)),
                    new GraphPortDefinition(GraphPorts.FlowOutput, "后续流程输出", PortDirection.Output, PortType.Flow, 1, PortSide.Right, new Rect(145, 72, 30, 25), "后续", new Rect(124, 96, 50, 20)))
            });
        }

        private static Dictionary<NodeKind, GraphNodeDefinition> CreateLookup()
        {
            var lookup = new Dictionary<NodeKind, GraphNodeDefinition>();
            foreach (GraphNodeDefinition definition in All)
            {
                lookup.Add(definition.Kind, definition);
            }
            foreach (NodeKind kind in Enum.GetValues(typeof(NodeKind)))
            {
                if (!lookup.ContainsKey(kind))
                {
                    throw new InvalidOperationException("节点类型缺少定义：" + kind);
                }
            }
            return lookup;
        }
    }
}
