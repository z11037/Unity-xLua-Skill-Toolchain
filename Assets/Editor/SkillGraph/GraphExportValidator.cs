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

            HashSet<string> reachable = GraphValidator.ReachableFlow(graph);
            int actionCount = 0;
            foreach (GraphNode node in graph.nodes)
            {
                if (node.kind == NodeKind.Entry || !reachable.Contains(node.id))
                {
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
