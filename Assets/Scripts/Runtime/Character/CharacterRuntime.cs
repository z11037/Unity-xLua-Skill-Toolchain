using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class CharacterRuntime : IDisposable
{
    public int CharacterId { get; private set; }

    public FinalState MaxHealth { get; private set; }
    public FinalState Attack { get; private set; }
    public FinalState MoveSpeed { get; private set; }
    public float CurrentHealth { get; private set; }

    public bool IsDisposed { get; private set; }

    public bool IsDead => CurrentHealth <= 0f;

    private readonly Dictionary<int, Skill> skills = new Dictionary<int, Skill>();
    private readonly List<Buff> buffs = new List<Buff>();
    private readonly List<Buff> tickBuffs = new List<Buff>();
    private readonly HashSet<Buff> buffMembership = new HashSet<Buff>();
    public event Action<float, float> OnHealthChanged;
    public event Action OnDied;
    public IReadOnlyList<Buff> Buffs
    {
        get
        {
            return buffs;
        }
    }
    public IReadOnlyList<Buff> TickBuffs
    {
        get
        {
            return tickBuffs;
        }
    }

    public CharacterRuntime(int characterId, float initialMaxHealth, float initialAttack, float initialMoveSpeed)
    {
        CharacterId = characterId;

        float validMaxHealth = Mathf.Max(1f, initialMaxHealth);
        float validAttack = Mathf.Max(0f, initialAttack);
        float validMoveSpeed = Mathf.Max(0f, initialMoveSpeed);

        MaxHealth = new FinalState(validMaxHealth);
        Attack = new FinalState(validAttack);
        MoveSpeed = new FinalState(validMoveSpeed);

        CurrentHealth = MaxHealth.Value;
    }

    public void TakeDamage(int damage)
    {
        if (damage <= 0 || IsDead || IsDisposed)
        {
            return;
        }

        float previousHealth = CurrentHealth;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);

        if (Mathf.Approximately(previousHealth, CurrentHealth))
        {
            return;
        }

        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth.Value);
        Log.Buff($"角色 {CharacterId} 受到 {damage} 点伤害，当前生命值 {CurrentHealth}/{MaxHealth.Value}");

        if (IsDead)
        {
            OnDied?.Invoke();
        }
    }

    public void Heal(int amount)
    {
        if (amount <= 0 || IsDead || IsDisposed)
        {
            return;
        }

        float previousHealth = CurrentHealth;
        CurrentHealth = Mathf.Min(MaxHealth.Value, CurrentHealth + amount);

        if (Mathf.Approximately(previousHealth, CurrentHealth))
        {
            return;
        }

        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth.Value);
        Log.Buff($"角色 {CharacterId} 恢复 {amount} 点生命，当前生命值 {CurrentHealth}/{MaxHealth.Value}");
    }

    public void AddAttack(int value)
    {
        Attack.AddBonus(value);
        Log.Buff($"角色 {CharacterId} 攻击力变化 {value}，当前攻击力 {Attack.Value}");
    }

    public int GetHealthValue()
    {
        return Mathf.FloorToInt(CurrentHealth);
    }

    public int GetMaxHealthValue()
    {
        return Mathf.FloorToInt(MaxHealth.Value);
    }

    public int GetAttackValue()
    {
        return Mathf.FloorToInt(Attack.Value);
    }

    public bool RegisterSkill(SkillSO config)
    {
        if (config == null)
        {
            Log.Skill($"[Warning] 角色 {CharacterId} 注册技能失败：配置为空");
            return false;
        }

        int skillId = config.skillID;

        if (skills.ContainsKey(skillId))
        {
            Log.Skill($"[Warning] 角色 {CharacterId} 已经注册技能 {skillId}");
            return false;
        }

        try
        {
            Skill skill = new Skill(config);

            if (!skill.IsReady)
            {
                Log.Skill($"[Error] 角色 {CharacterId} 注册技能 {skillId} 失败：Lua 技能未正确加载");
                skill.Dispose();
                return false;
            }

            skills.Add(skillId, skill);
            Log.Skill($"角色 {CharacterId} 注册技能 {skillId} 成功");
            return true;
        }
        catch (Exception exception)
        {
            Log.Skill($"[Error] 角色 {CharacterId} 创建技能 {skillId} 失败");
            Debug.LogException(exception);
            return false;
        }
    }

    public bool UnregisterSkill(int skillId)
    {
        if (!skills.TryGetValue(skillId, out Skill skill))
        {
            return false;
        }

        skill.Dispose();
        skills.Remove(skillId);

        Log.Skill($"角色 {CharacterId} 已移除技能 {skillId}");
        return true;
    }

    public bool TryGetSkill(int skillId, out Skill skill)
    {
        return skills.TryGetValue(skillId, out skill);
    }

    public bool TryCast(int skillId, Character caster, Character target)
    {
        if (IsDead || IsDisposed)
        {
            Log.Skill($"角色 {CharacterId} 已死亡，无法释放技能 {skillId}");
            return false;
        }

        if (!skills.TryGetValue(skillId, out Skill skill))
        {
            Log.Skill($"角色 {CharacterId} 未注册技能 {skillId}");
            return false;
        }

        return skill.TryExecute(caster, target);
    }

    public float GetRemainingCooldown(int skillId)
    {
        if (!skills.TryGetValue(skillId, out Skill skill))
        {
            return 0f;
        }

        return skill.CurrentCooldown;
    }

    public bool IsOnCooldown(int skillId)
    {
        if (!skills.TryGetValue(skillId, out Skill skill))
        {
            return false;
        }

        return !skill.CanExecute;
    }

    public Buff FindBuff(int buffId)
    {
        for (int i = 0; i < buffs.Count; i++)
        {
            Buff buff = buffs[i];

            if (buff == null || buff.Config == null)
            {
                continue;
            }

            if (buff.Config.buffID == buffId)
            {
                return buff;
            }
        }

        return null;
    }

    public Buff FindBuff(BuffSO config)
    {
        if (config == null)
        {
            return null;
        }

        return FindBuff(config.buffID);
    }

    internal bool AddBuff(Buff buff)
    {
        if (buff == null)
        {
            Log.Buff("[Warning] 无法添加空 Buff");
            return false;
        }

        if (buff.Config == null)
        {
            Log.Buff("[Warning] 无法添加配置为空的 Buff");
            return false;
        }

        if (FindBuff(buff.Config.buffID) != null)
        {
            Log.Buff($"[Warning] 角色 {CharacterId} 已经持有 Buff {buff.Config.buffID}");
            return false;
        }

        buffs.Add(buff);
        buffMembership.Add(buff);

        if (buff.NeedTick)
        {
            tickBuffs.Add(buff);
        }

        return true;
    }

    internal bool ContainsBuff(Buff buff)
    {
        return buff != null && buffMembership.Contains(buff);
    }

    internal bool DetachBuff(Buff buff)
    {
        if (buff == null)
        {
            return false;
        }

        bool removed = buffMembership.Remove(buff);
        if (removed)
        {
            buffs.Remove(buff);
        }

        if (removed)
        {
            tickBuffs.Remove(buff);
        }

        return removed;
    }

    public void Tick(float deltaTime)
    {
        foreach (Skill skill in skills.Values)
        {
            skill.Tick(deltaTime);
        }
    }

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }
        // 先阻止回调重新施加，再完整撤销仍然生效的效果。
        IsDisposed = true;
        if (BuffManager.Instance != null)
        {
            BuffManager.Instance.RemoveAllBuffs(this);
        }
        foreach (Skill skill in skills.Values)
        {
            skill.Dispose();
        }

        skills.Clear();

        if (buffs.Count > 0)
        {
            Log.Buff($"[Warning] 角色 {CharacterId} 释放时仍有 {buffs.Count} 个 Buff，将直接清空引用");
        }

        buffs.Clear();
        tickBuffs.Clear();
        buffMembership.Clear();
        OnHealthChanged = null;
        OnDied = null;
        Debug.Log($"角色 {CharacterId} 的 CharacterRuntime 已释放");
    }
}