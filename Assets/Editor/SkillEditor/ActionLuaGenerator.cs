using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

// 只读取模块数据并产出文本与资源表，不读写文件，也不依赖编辑器窗口。
public static class ActionLuaGenerator
{
    public static string Generate(IReadOnlyList<SkillActionConfig> actions, out UnityEngine.Object[] resources)
    {
        if (!SkillActionValidator.Validate(actions, out string error))
        {
            throw new ArgumentException(error, nameof(actions));
        }
        var references = new List<UnityEngine.Object>();
        var code = new StringBuilder();
        int resourceIndex = 0;
        for (int i = 0; i < actions.Count; i++)
        {
            SkillActionConfig action = actions[i];
            string receiver = action.target == SkillActionTarget.Self ? "attacker" : "target";
            code.AppendLine($"    -- 动作 {i + 1}：{action.type}");
            string call = action.type == SkillActionType.Damage
                ? $"api.Damage(attacker, {receiver}, {action.damage.ToString(CultureInfo.InvariantCulture)})"
                : $"api.ApplyBuff(attacker, {receiver}, resources[{resourceIndex++}])";
            if (action.type == SkillActionType.Buff)
            {
                references.Add(action.buff);
            }
            code.AppendLine($"    {call}");
        }
        resources = references.ToArray();
        return code.ToString();
    }
}
