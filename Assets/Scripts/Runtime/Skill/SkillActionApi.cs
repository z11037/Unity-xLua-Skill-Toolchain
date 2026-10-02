using XLua;

// 只转发已有业务入口，状态和结算规则由底层系统负责。
[LuaCallCSharp]
public static class SkillActionApi
{
    public static void Damage(Character caster, Character target, int amount)
    {
        if (target != null)
        {
            target.TakeDamage(amount);
        }
    }

    public static void ApplyBuff(Character caster, Character target, BuffSO buff)
    {
        if (BuffManager.Instance != null)
        {
            BuffManager.Instance.AddBuff(target, buff, caster);
        }
    }
}
