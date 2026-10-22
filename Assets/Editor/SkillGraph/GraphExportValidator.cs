using System;
using System.Collections.Generic;
using UnityEditor;

namespace SkillGraphEditor
{
    // 草稿可以缺少参数；导出时检查入口执行链与资源引用，不判断 Buff 的业务配置。
    public static class GraphExportValidator
    {
        public static List<GraphIssue> Validate(GraphData graph)
        {
            List<GraphIssue> issues = GraphValidator.ValidateStructure(graph);
            if (issues.Count > 0)
            {
                return issues;
            }

            HashSet<string> reachable = GraphValidator.ParticipatingNodes(graph);
            int actionCount = 0;
            foreach (GraphNode node in graph.nodes)
            {
                if (node.kind == NodeKind.Entry || !reachable.Contains(node.id))
                {
                    continue;
                }
                if (node.kind == NodeKind.If)
                {
                    ValidateBranch(graph, node, issues);
                    continue;
                }
                if (node.kind == NodeKind.HealthBelow)
                {
                    if (float.IsNaN(node.healthPercent) || float.IsInfinity(node.healthPercent) || node.healthPercent < 0f || node.healthPercent > 100f)
                    {
                        issues.Add(new GraphIssue("InvalidHealthPercent", node.id, "", "healthPercent", "生命比例必须是 0 到 100 之间的有限数值。"));
                    }
                    continue;
                }
                actionCount++;
                GraphIssue issue = ValidateAction(node);
                if (issue != null)
                {
                    issues.Add(issue);
                }
            }
            if (actionCount == 0)
            {
                GraphNode entry = graph.nodes.Find(node => node.kind == NodeKind.Entry);
                issues.Add(new GraphIssue("NoActions", entry.id, GraphPorts.FlowOutput, "", "入口执行链至少需要一个动作。"));
            }
            return issues;
        }

        private static void ValidateBranch(GraphData graph, GraphNode node, List<GraphIssue> issues)
        {
            GraphEdge condition = graph.edges.Find(edge => edge.to == node.id && edge.toPort == GraphPorts.ConditionInput);
            if (condition == null)
            {
                issues.Add(new GraphIssue("MissingCondition", node.id, GraphPorts.ConditionInput, "", "请将生命比例条件的输出连接到 If 的条件输入。"));
            }
            GraphEdge satisfied = GraphValidator.Outgoing(graph, node.id, GraphPorts.FlowTrue);
            GraphEdge unsatisfied = GraphValidator.Outgoing(graph, node.id, GraphPorts.FlowFalse);
            if (satisfied == null && unsatisfied == null)
            {
                issues.Add(new GraphIssue("EmptyBranches", node.id, GraphPorts.FlowTrue, "", "满足和不满足分支至少需要连接一个动作；后续动作不能代替分支内容。"));
            }
            ValidateBranchSequence(graph, satisfied, issues);
            ValidateBranchSequence(graph, unsatisfied, issues);
        }

        // 首版分支仅支持顺序动作；多个 If 可以通过“后续”按顺序执行。
        private static void ValidateBranchSequence(GraphData graph, GraphEdge edge, List<GraphIssue> issues)
        {
            while (edge != null)
            {
                GraphNode node = graph.Find(edge.to);
                if (node.kind == NodeKind.If)
                {
                    issues.Add(new GraphIssue("NestedBranchUnsupported", node.id, GraphPorts.FlowInput, "", "当前版本的分支内仅支持伤害和 Buff 顺序动作，请将另一个 If 连接到前一个 If 的后续输出。"));
                    return;
                }
                edge = GraphValidator.Outgoing(graph, node.id, GraphPorts.FlowOutput);
            }
        }

        // 顺序动作的两个生成器共用节点校验，不再构造另一份配置。
        internal static void RequireValidActions(IReadOnlyList<GraphNode> nodes)
        {
            var issues = new List<GraphIssue>();
            if (nodes == null || nodes.Count == 0)
            {
                issues.Add(new GraphIssue("NoActions", null, null, "nodes", "动作节点列表不能为空。"));
            }
            else
            {
                foreach (GraphNode node in nodes)
                {
                    GraphIssue issue = ValidateAction(node);
                    if (issue != null)
                    {
                        issues.Add(issue);
                    }
                }
            }
            if (issues.Count > 0)
            {
                throw new GraphValidationException(issues);
            }
        }

        internal static GraphIssue ValidateAction(GraphNode node)
        {
            if (node == null)
            {
                return new GraphIssue("NODE_NULL", null, null, "nodes", "动作节点为空。");
            }
            if (!Enum.IsDefined(typeof(TargetKind), node.target))
            {
                return new GraphIssue("NODE_TARGET", node.id, null, "target", "节点的目标类型无效。");
            }
            switch (node.kind)
            {
                case NodeKind.Damage:
                    return node.damage > 0 ? null : new GraphIssue("InvalidDamage", node.id, "", "damage", $"固定伤害必须为大于 0 的整数（当前值：{node.damage}）。");
                case NodeKind.ApplyBuff:
                    string path = AssetDatabase.GUIDToAssetPath(node.buffGuid ?? "");
                    BuffSO buff = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<BuffSO>(path);
                    if (buff == null)
                    {
                        string message = string.IsNullOrWhiteSpace(node.buffGuid) ? "请为 Buff 节点选择一个 Buff 资源。" : "引用的 Buff 资源已失效或不是 BuffSO，请重新选择。";
                        return new GraphIssue("MissingBuffResource", node.id, "", "buffGuid", message);
                    }
                    return null;
                default:
                    return new GraphIssue("UnsupportedAction", node.id, null, "kind", "该节点类型不支持作为顺序动作生成。");
            }
        }
    }
}
