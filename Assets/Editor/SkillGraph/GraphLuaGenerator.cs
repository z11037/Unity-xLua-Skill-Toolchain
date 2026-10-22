using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SkillGraphEditor
{
    public static class GraphLuaGenerator
    {
        public static string Generate(GraphGenerationPlan plan, out UnityEngine.Object[] resources)
        {
            var references = new List<UnityEngine.Object>();
            var body = new StringBuilder();
            int actionNumber = 1;
            int conditionNumber = 1;
            WriteSequence(plan.Steps, "    ", body, references, ref actionNumber, ref conditionNumber);
            resources = references.ToArray();
            return body.ToString();
        }

        private static void WriteSequence(IReadOnlyList<GraphGenerationStep> steps, string indentation, StringBuilder body, List<UnityEngine.Object> resources, ref int actionNumber, ref int conditionNumber)
        {
            foreach (GraphGenerationStep step in steps)
            {
                if (step is GraphActionStep action)
                {
                    body.Append(ActionLuaGenerator.Generate(new[] { action.Node }, out UnityEngine.Object[] references, resources.Count, indentation, actionNumber++));
                    resources.AddRange(references);
                    continue;
                }
                var branch = (GraphIfStep)step;
                string suffix = (conditionNumber++).ToString(CultureInfo.InvariantCulture);
                string receiver = branch.Condition.target == TargetKind.Self ? "attacker" : "target";
                string percent = branch.Condition.healthPercent.ToString("R", CultureInfo.InvariantCulture);
                body.AppendLine($"{indentation}-- 条件 {suffix}：生命比例低于 {percent}%");
                body.AppendLine($"{indentation}local conditionTarget{suffix} = {receiver}");
                body.AppendLine($"{indentation}local maxHealth{suffix} = conditionTarget{suffix} ~= nil and conditionTarget{suffix}:GetMaxHealthValue() or 0");
                // 先保护目标和最大生命，再求值一次；比较式避免百分比除法。
                body.AppendLine($"{indentation}if maxHealth{suffix} > 0 and conditionTarget{suffix}:GetHealthValue() * 100 < maxHealth{suffix} * {percent} then");
                WriteSequence(branch.Satisfied, indentation + "    ", body, resources, ref actionNumber, ref conditionNumber);
                if (branch.Unsatisfied.Count > 0)
                {
                    body.AppendLine($"{indentation}else");
                    WriteSequence(branch.Unsatisfied, indentation + "    ", body, resources, ref actionNumber, ref conditionNumber);
                }
                body.AppendLine($"{indentation}end");
            }
        }
    }
}
