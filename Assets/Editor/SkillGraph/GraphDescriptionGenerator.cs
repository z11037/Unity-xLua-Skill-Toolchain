using System.Collections.Generic;
using System.Globalization;

namespace SkillGraphEditor
{
    public static class GraphDescriptionGenerator
    {
        public static string Generate(GraphGenerationPlan plan)
        {
            var descriptions = new List<string>();
            var actions = new List<GraphNode>();
            bool followsBranch = false;
            foreach (GraphGenerationStep step in plan.Steps)
            {
                if (step is GraphActionStep action)
                {
                    actions.Add(action.Node);
                    continue;
                }
                bool immediatelyFollowsBranch = followsBranch && actions.Count == 0;
                AppendActions(actions, descriptions, followsBranch);
                var branch = (GraphIfStep)step;
                string target = branch.Condition.target == TargetKind.Self ? "自身" : "敌方";
                string percent = branch.Condition.healthPercent.ToString("R", CultureInfo.InvariantCulture);
                string prefix = immediatelyFollowsBranch ? "随后，" : "";
                descriptions.Add($"{prefix}若{target}当前生命低于最大生命的{percent}%，{DescribeBranch(branch.Satisfied)}；否则{DescribeBranch(branch.Unsatisfied)}。");
                followsBranch = true;
            }
            AppendActions(actions, descriptions, followsBranch);
            return string.Join("\n", descriptions);
        }

        private static void AppendActions(List<GraphNode> actions, List<string> descriptions, bool followsBranch)
        {
            if (actions.Count == 0)
            {
                return;
            }
            string text = ActionDescriptionGenerator.Generate(actions);
            descriptions.Add((followsBranch ? "随后" : "") + text);
            actions.Clear();
        }

        private static string DescribeBranch(IReadOnlyList<GraphGenerationStep> steps)
        {
            if (steps.Count == 0)
            {
                return "不执行额外动作";
            }
            var actions = new List<GraphNode>();
            foreach (GraphGenerationStep step in steps)
            {
                actions.Add(((GraphActionStep)step).Node);
            }
            // 只在同一分支内组合语句，分支和公共后续之间不合并动作。
            return ActionDescriptionGenerator.Generate(actions).Replace("。\n", "；随后").TrimEnd('。');
        }
    }
}
