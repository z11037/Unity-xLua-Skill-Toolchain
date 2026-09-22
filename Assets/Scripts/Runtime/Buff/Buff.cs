using System;
using UnityEngine;

public class Buff
{
    public BuffSO Config { get; private set; }
    public int CurrentStack { get; private set; }
    public float RemainingTime { get; private set; }

    public float TickAccumulator { get; private set; }
    public bool NeedTick =>
    Config != null && Config.tickInterval > 0f;

    public Character Owner { get; private set; }
    public Character Source { get; private set; }

    public bool IsExpired => RemainingTime <= 0f;
    public int MaxStack => Mathf.Max(1, Config.maxStack);

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Config.buffName))
            {
                return Config.buffName;
            }

            return $"Buff_{Config.buffID}";
        }
    }

    public Buff(BuffSO config, Character source, Character owner)
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

        CurrentStack = 1;
        RemainingTime = Mathf.Max(0f, config.duration);
        TickAccumulator = 0f;
    }

    public void UpdateDuration(float deltaTime)
    {
        if (deltaTime <= 0f || IsExpired)
        {
            return;
        }

        RemainingTime -= deltaTime;

        if (RemainingTime < 0f)
        {
            RemainingTime = 0f;
        }
    }

    public int UpdateTick(float deltaTime)
    {
        if (!NeedTick || deltaTime <= 0f || IsExpired)
        {
            return 0;
        }

        TickAccumulator += deltaTime;

        int tickCount = Mathf.FloorToInt(
            TickAccumulator / Config.tickInterval
        );

        if (tickCount > 0)
        {
            TickAccumulator -= tickCount * Config.tickInterval;
        }

        return tickCount;
    }

    public bool Reapply()
    {
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
        RemainingTime = Mathf.Max(0f, Config.duration);
    }
}