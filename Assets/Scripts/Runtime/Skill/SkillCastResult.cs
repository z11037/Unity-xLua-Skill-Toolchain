// 结果只描述本次施法流程，不统计每个伤害或 Buff 是否实际生效。
public enum SkillCastStatus
{
    NotRequested,
    Completed,
    CoolingDown,
    AlreadyExecuting,
    LuaNotReady,
    InvalidCaster,
    CasterDead,
    RuntimeDisposed,
    SkillDisposed,
    SkillNotFound,
    RuntimeManagerUnavailable,
    RuntimeNotFound,
    InvalidCooldown,
    ExecutionFailed
}

public readonly struct SkillCastResult
{
    public SkillCastStatus Status { get; }
    public float RemainingCooldown { get; }
    public bool Succeeded => Status == SkillCastStatus.Completed;
    public bool Started => Status == SkillCastStatus.Completed || Status == SkillCastStatus.ExecutionFailed;

    public SkillCastResult(SkillCastStatus status, float remainingCooldown = 0f)
    {
        Status = status;
        RemainingCooldown = remainingCooldown;
    }

    public string Message
    {
        get
        {
            switch (Status)
            {
                case SkillCastStatus.NotRequested:
                    return "尚未发起施法";
                case SkillCastStatus.Completed:
                    return "技能执行完成";
                case SkillCastStatus.CoolingDown:
                    return $"技能冷却中，剩余 {RemainingCooldown:F1} 秒";
                case SkillCastStatus.AlreadyExecuting:
                    return "技能正在执行，不能重复进入";
                case SkillCastStatus.LuaNotReady:
                    return "Lua 执行函数未就绪";
                case SkillCastStatus.InvalidCaster:
                    return "施法者为空";
                case SkillCastStatus.CasterDead:
                    return "角色已死亡";
                case SkillCastStatus.RuntimeDisposed:
                    return "角色运行时已释放";
                case SkillCastStatus.SkillDisposed:
                    return "技能运行时已释放";
                case SkillCastStatus.SkillNotFound:
                    return "角色未注册该技能";
                case SkillCastStatus.RuntimeManagerUnavailable:
                    return "角色管理器未初始化";
                case SkillCastStatus.RuntimeNotFound:
                    return "角色运行时不存在";
                case SkillCastStatus.InvalidCooldown:
                    return "技能冷却配置无效";
                case SkillCastStatus.ExecutionFailed:
                    return "技能执行异常，已产生的效果与冷却保留";
                default:
                    return "未知施法结果";
            }
        }
    }
}
