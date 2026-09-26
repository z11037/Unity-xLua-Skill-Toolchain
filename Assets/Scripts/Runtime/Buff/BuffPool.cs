using System;
using System.Collections.Generic;

// 仅在主线程使用；拥有者必须先解除管理关系并结束相关回调，再归还实例。
public sealed class BuffPool
{
    private readonly Stack<Buff> available = new Stack<Buff>();
    private readonly HashSet<Buff> rented = new HashSet<Buff>();

    public int CreatedCount { get; private set; }
    public int RentCount { get; private set; }
    public int AvailableCount => available.Count;
    public int RentedCount => rented.Count;

    public Buff Rent(BuffSO config, Character source, Character owner, CharacterRuntime runtime)
    {
        // 无效请求不能消耗空闲实例，也不能改变租借统计。
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }
        if (owner == null)
        {
            throw new ArgumentNullException(nameof(owner));
        }
        if (runtime == null || runtime.IsDisposed || runtime.IsDead)
        {
            throw new ArgumentException("目标 Runtime 不可用。", nameof(runtime));
        }

        Buff buff;
        if (available.Count > 0)
        {
            buff = available.Pop();
        }
        else
        {
            buff = new Buff();
            CreatedCount++;
        }
        buff.Initialize(config, source, owner, runtime);
        rented.Add(buff);
        RentCount++;
        return buff;
    }

    public bool Return(Buff buff)
    {
        if (buff == null || !rented.Contains(buff))
        {
            return false;
        }
        if (buff.TargetRuntime != null && buff.TargetRuntime.ContainsBuff(buff))
        {
            return false;
        }
        rented.Remove(buff);
        buff.ResetForPool();
        available.Push(buff);
        return true;
    }

    public void ClearAvailable()
    {
        // 不回收仍在使用的实例。
        available.Clear();
    }
}
