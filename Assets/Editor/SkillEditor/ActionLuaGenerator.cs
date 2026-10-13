using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

// 只读取模块数据并产出文本与资源表，不读写文件，也不依赖编辑器窗口。
public static class ActionLuaGenerator
{
    public static string Generate(IReadOnlyList<SkillActionConfig> actions, out UnityEngine.Object[] resources, int firstResourceIndex = 0, string indentation = "    ", int firstActionNumber = 1)
    {
        if (!SkillActionValidator.Validate(actions, out string error))
        {
            throw new ArgumentException(error, nameof(actions));
        }
        if (firstResourceIndex < 0 || firstActionNumber < 1 || indentation == null)
        {
            throw new ArgumentException("动作生成的资源索引、编号或缩进无效。");
        }
        var references = new List<UnityEngine.Object>();
        var code = new StringBuilder();
        int resourceIndex = firstResourceIndex;
        for (int i = 0; i < actions.Count; i++)
        {
            SkillActionConfig action = actions[i];
            string receiver = action.target == SkillActionTarget.Self ? "attacker" : "target";
            code.AppendLine($"{indentation}-- 动作 {firstActionNumber + i}：{action.type}");
            string call = action.type == SkillActionType.Damage
                ? $"api.Damage(attacker, {receiver}, {action.damage.ToString(CultureInfo.InvariantCulture)})"
                : $"api.ApplyBuff(attacker, {receiver}, resources[{resourceIndex++}])";
            if (action.type == SkillActionType.Buff)
            {
                references.Add(action.buff);
            }
            code.AppendLine($"{indentation}{call}");
        }
        resources = references.ToArray();
        return code.ToString();
    }
}
