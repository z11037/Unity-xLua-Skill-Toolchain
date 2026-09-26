public class DefaultBuffExecutor : IBuffExecutor
{
    public void OnApply(Buff buff)
    {
        switch (buff.Config.type)
        {
            case BuffType.Attack:
                {
                    int attackValue = CalculateTotalValue(buff);
                    buff.TargetRuntime.AddAttack(attackValue);

                    Log.Buff($"[DefaultBuffExecutor] {buff.DisplayName} 生效：攻击力变化 {attackValue}，当前层数 {buff.CurrentStack}");
                    break;
                }

            case BuffType.Poison:
            case BuffType.Heal:
            case BuffType.Shield:
                {
                    break;
                }
        }
    }

    public void OnTick(Buff buff)
    {
        int tickValue = CalculateTotalValue(buff);

        switch (buff.Config.type)
        {
            case BuffType.Poison:
                {
                    buff.TargetRuntime.TakeDamage(tickValue);

                    Log.Buff($"[DefaultBuffExecutor] {buff.DisplayName} Tick：造成 {tickValue} 点伤害，当前层数 {buff.CurrentStack}");
                    break;
                }

            case BuffType.Heal:
                {
                    buff.TargetRuntime.Heal(tickValue);

                    Log.Buff($"[DefaultBuffExecutor] {buff.DisplayName} Tick：恢复 {tickValue} 点生命，当前层数 {buff.CurrentStack}");
                    break;
                }

            case BuffType.Attack:
            case BuffType.Shield:
                {
                    break;
                }
        }
    }

    public void OnStack(Buff buff)
    {
        switch (buff.Config.type)
        {
            case BuffType.Attack:
                {
                    int previousStack = buff.CurrentStack - 1;
                    int previousValue = CalculateValue(buff.Config.effectValue, previousStack);
                    int currentValue = CalculateTotalValue(buff);
                    int addedValue = currentValue - previousValue;

                    buff.TargetRuntime.AddAttack(addedValue);

                    Log.Buff($"[DefaultBuffExecutor] {buff.DisplayName} 叠层：攻击力额外变化 {addedValue}，当前层数 {buff.CurrentStack}");
                    break;
                }

            case BuffType.Poison:
            case BuffType.Heal:
            case BuffType.Shield:
                {
                    break;
                }
        }
    }

    public void OnRemove(Buff buff)
    {
        switch (buff.Config.type)
        {
            case BuffType.Attack:
                {
                    int attackValue = CalculateTotalValue(buff);
                    buff.TargetRuntime.AddAttack(-attackValue);

                    Log.Buff($"[DefaultBuffExecutor] {buff.DisplayName} 移除：撤销攻击力变化 {attackValue}");
                    break;
                }

            case BuffType.Poison:
            case BuffType.Heal:
            case BuffType.Shield:
                {
                    break;
                }
        }
    }

    private int CalculateTotalValue(Buff buff)
    {
        return CalculateValue(buff.Config.effectValue, buff.CurrentStack);
    }

    private int CalculateValue(float effectValue, int stack)
    {
        return (int)(effectValue * stack);
    }
}