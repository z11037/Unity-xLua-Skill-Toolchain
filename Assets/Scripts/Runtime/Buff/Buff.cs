using System;
using UnityEngine;

public class Buff
{
    public BuffSO Config { get; private set; }
    public int CurrentStack { get; private set; }
    private double remainingTime;
    private double tickAccumulator;

    public float RemainingTime => (float)remainingTime;

    public float TickAccumulator => (float)Math.Max(0d, tickAccumulator);
    public bool NeedTick =>
    Config != null && Config.tickInterval > 0f;

    public Character Owner { get; private set; }
    public Character Source { get; private set; }
    public CharacterRuntime TargetRuntime { get; private set; }

    public bool IsExpired => remainingTime <= 0d;
    public int MaxStack => Config == null ? 0 : Mathf.Max(1, Config.maxStack);

    public string DisplayName
    {
        get
        {
            if (Config == null)
            {
                return "未激活Buff";
            }
            if (!string.IsNullOrWhiteSpace(Config.buffName))
            {
                return Config.buffName;
            }

            return $"Buff_{Config.buffID}";
        }
    }

    internal Buff()
    {
    }

    public Buff(BuffSO config, Character source, Character owner, CharacterRuntime targetRuntime = null)
    {
        Initialize(config, source, owner, targetRuntime);
    }

    internal void Initialize(BuffSO config, Character source, Character owner, CharacterRuntime targetRuntime)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (owner == null)
        {
            throw new ArgumentNullException(nameof(owner));
        }

        Config = config;
        Source = source;
        Owner = owner;
        TargetRuntime = targetRuntime;

        CurrentStack = 1;
        remainingTime = Math.Max(0d, config.duration);
        tickAccumulator = 0d;
    }

    // 归还时清除全部状态和强引用；下一次获取必须按新配置重新初始化。
    internal void ResetForPool()
    {
        CurrentStack = 0;
        remainingTime = 0d;
        tickAccumulator = 0d;
        Config = null;
        Owner = null;
        Source = null;
        TargetRuntime = null;
    }

    public void UpdateDuration(float deltaTime)
    {
        if (deltaTime <= 0f || IsExpired)
        {
            return;
        }

        remainingTime = Math.Max(0d, remainingTime - deltaTime);
    }

    // 在 UpdateDuration 之前调用：仅累计 Buff 有效期内的时间来结算 Tick。
    public int UpdateTick(float deltaTime)
    {
        if (!NeedTick || deltaTime <= 0f || IsExpired)
        {
            return 0;
        }

        double activeDeltaTime = Math.Min((double)deltaTime, remainingTime);
        tickAccumulator += activeDeltaTime;

        double interval = Config.tickInterval;
        // 容差用于抵消浮点配置的舍入误差，不直接累加到每次 Tick 的计时中。
        double tolerance = Math.Min(interval * 0.0001d, Math.Max(interval, Config.duration) * 0.0000001d);
        int tickCount = Math.Max(0, (int)Math.Floor((tickAccumulator + tolerance) / interval));

        if (tickCount > 0)
        {
            tickAccumulator -= tickCount * interval;
            // 保留微小的负余量，后续累计时间时补回，避免 Tick 因误差逐渐提前。
        }

        return tickCount;
    }

    public bool Reapply()
    {
        if (Config == null)
        {
            return false;
        }
        bool stackIncreased = false;

        if (CurrentStack < MaxStack)
        {
            CurrentStack++;
            stackIncreased = true;
        }

        RefreshDuration();
        return stackIncreased;
    }

    public void RefreshDuration()
    {
        remainingTime = Config == null ? 0d : Math.Max(0d, Config.duration);
    }
}