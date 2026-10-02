using System;
using UnityEngine;

public enum SkillActionType { Damage, Buff }
public enum SkillActionTarget { CurrentTarget, Self }

// 仅保存配置；施法者、目标和执行状态属于单次执行上下文。
[Serializable]
public sealed class SkillActionConfig
{
    public SkillActionType type;
    public SkillActionTarget target;
    public int damage = 10;
    public BuffSO buff;
}

public static class SkillActionValidator
{
    // 编辑器与运行时共用同一套配置校验，未知枚举不能静默执行。
    public static bool Validate(System.Collections.Generic.IReadOnlyList<SkillActionConfig> actions, out string error)
    {
        error = null;
        if (actions == null || actions.Count == 0)
        {
            error = "动作列表不能为空";
            return false;
        }
        for (int i = 0; i < actions.Count; i++)
        {
            SkillActionConfig action = actions[i];
            string reason = null;
            if (action == null)
            {
                reason = "动作为空";
            }
            else if (!Enum.IsDefined(typeof(SkillActionTarget), action.target))
            {
                reason = "目标类型无效";
            }
            else if (action.type == SkillActionType.Damage)
            {
                if (action.damage <= 0)
                {
                    reason = "固定伤害必须大于零";
                }
            }
            else if (action.type == SkillActionType.Buff)
            {
                if (action.buff == null)
                {
                    reason = "未引用 BuffSO";
                }
                else if (action.buff.type != BuffType.Poison && action.buff.type != BuffType.Heal && action.buff.type != BuffType.Attack)
                {
                    reason = "Buff 类型尚未实现";
                }
                else if (!IsFinite(action.buff.duration) || action.buff.duration < 0f || !IsFinite(action.buff.tickInterval) || action.buff.tickInterval < 0f || !IsFinite(action.buff.effectValue) || action.buff.maxStack < 1)
                {
                    reason = "BuffSO 的时间、层数或效果值无效";
                }
            }
            else
            {
                reason = "动作类型无效";
            }
            if (reason != null)
            {
                error = $"动作 {i + 1}：{reason}";
                return false;
            }
        }
        return true;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

}
