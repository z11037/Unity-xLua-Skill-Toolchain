using System;
using System.Collections.Generic;
using UnityEditor;

namespace SkillGraphEditor
{
    // 草稿可以缺少参数；导出时只要求入口执行链中的动作及资源完整可用。
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
                SkillActionConfig action = ToAction(node);
                if (!SkillActionValidator.Validate(new[] { action }, out string error))
                {
                    string field = node.kind == NodeKind.Damage ? "damage" : "buffGuid";
                    string code = node.kind == NodeKind.Damage ? "InvalidDamage" : action.buff == null ? "MissingBuffResource" : "InvalidBuff";
                    const string prefix = "动作 1：";
                    string message = error.StartsWith(prefix, StringComparison.Ordinal) ? error.Substring(prefix.Length) : error;
                    if (node.kind == NodeKind.Damage)
                    {
                        message = $"固定伤害必须为大于 0 的整数（当前值：{node.damage}）。";
                    }
                    else if (action.buff == null)
                    {
                        message = string.IsNullOrWhiteSpace(node.buffGuid) ? "请为 Buff 节点选择一个 Buff 资源。" : "引用的 Buff 资源已失效或不是 BuffSO，请重新选择。";
                    }
                    else
                    {
                        message = "引用的 Buff 配置无效：" + message + "。";
                    }
                    issues.Add(new GraphIssue(code, node.id, "", field, message));
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

        // 转换只封装当前支持的两种动作，不增加运行时目标和状态判断。
        internal static SkillActionConfig ToAction(GraphNode node)
        {
            SkillActionTarget target;
            switch (node.target)
            {
                case TargetKind.CurrentTarget:
                    target = SkillActionTarget.CurrentTarget;
                    break;
                case TargetKind.Self:
                    target = SkillActionTarget.Self;
                    break;
                default:
                    throw new InvalidOperationException("节点目标类型无效。");
            }
            switch (node.kind)
            {
                case NodeKind.Damage:
                    return new SkillActionConfig { type = SkillActionType.Damage, target = target, damage = node.damage };
                case NodeKind.ApplyBuff:
                    string path = AssetDatabase.GUIDToAssetPath(node.buffGuid ?? "");
                    BuffSO buff = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<BuffSO>(path);
                    return new SkillActionConfig { type = SkillActionType.Buff, target = target, buff = buff };
                default:
                    throw new InvalidOperationException("该节点类型不支持转换为动作。");
            }
        }
    }
}
