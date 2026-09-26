using System;
using System.Collections.Generic;
using UnityEngine;

public class BuffManager : MonoBehaviour
{
    public static BuffManager Instance { get; private set; }

    private readonly IBuffExecutor executor = new DefaultBuffExecutor();

    private readonly HashSet<CharacterRuntime> activeRuntimes = new HashSet<CharacterRuntime>();

    private readonly List<CharacterRuntime> runtimeSnapshot = new List<CharacterRuntime>();
    private readonly List<Buff> buffSnapshot = new List<Buff>();
    private readonly HashSet<CharacterRuntime> clearingRuntimes = new HashSet<CharacterRuntime>();
    private bool isShuttingDown;
    private readonly BuffPool pool = new BuffPool();
    private readonly List<Buff> pendingReturns = new List<Buff>();
    private int operationDepth;

    public int CreatedBuffCount => pool.CreatedCount;
    public int BuffRentCount => pool.RentCount;
    public int AvailableBuffCount => pool.AvailableCount;
    public int RentedBuffCount => pool.RentedCount;

    private void EndOperation()
    {
        operationDepth--;
        if (operationDepth != 0)
        {
            return;
        }
        // 整个更新或最外层调用结束后才允许复用，防止快照和回调继续访问旧实例。
        foreach (Buff buff in pendingReturns)
        {
            pool.Return(buff);
        }
        pendingReturns.Clear();
        if (isShuttingDown)
        {
            pool.ClearAvailable();
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.Log("[BuffManager] 场景中存在重复实例，当前实例将被销毁");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        Debug.Log("[BuffManager] 初始化完成");
    }

    private void Update()
    {
        operationDepth++;
        try
        {
            UpdateCore();
        }
        finally
        {
            runtimeSnapshot.Clear();
            buffSnapshot.Clear();
            EndOperation();
        }
    }

    private void UpdateCore()
    {
        float deltaTime = Time.deltaTime;
        // 回调可以移除 Buff、注销角色或给其他角色添加 Buff，遍历快照避免集合失效。
        runtimeSnapshot.Clear();
        runtimeSnapshot.AddRange(activeRuntimes);
        foreach (CharacterRuntime runtime in runtimeSnapshot)
        {
            if (!activeRuntimes.Contains(runtime))
            {
                continue;
            }
            if (runtime.IsDead || runtime.IsDisposed)
            {
                RemoveAllBuffs(runtime);
                continue;
            }

            buffSnapshot.Clear();
            buffSnapshot.AddRange(runtime.Buffs);
            for (int i = buffSnapshot.Count - 1; i >= 0; i--)
            {
                Buff buff = buffSnapshot[i];
                if (!ContainsBuff(runtime, buff) || !buff.NeedTick)
                {
                    continue;
                }
                UpdateTick(runtime, buff, deltaTime);
                if (runtime.IsDead || runtime.IsDisposed)
                {
                    break;
                }
            }
            if (runtime.IsDead || runtime.IsDisposed)
            {
                RemoveAllBuffs(runtime);
                continue;
            }
            for (int i = buffSnapshot.Count - 1; i >= 0; i--)
            {
                Buff buff = buffSnapshot[i];
                if (ContainsBuff(runtime, buff))
                {
                    UpdateDuration(runtime, buff, deltaTime);
                }
            }
        }
        // 避免空闲帧之间仍通过快照持有已注销角色。
        runtimeSnapshot.Clear();
        buffSnapshot.Clear();
    }

    private static bool ContainsBuff(CharacterRuntime runtime, Buff buff)
    {
        return runtime.ContainsBuff(buff);
    }
    private void OnDestroy()
    {
        operationDepth++;
        try
        {
            OnDestroyCore();
        }
        finally
        {
            EndOperation();
        }
    }

    private void OnDestroyCore()
    {
        if (Instance == this)
        {
            isShuttingDown = true;
            foreach (CharacterRuntime runtime in new List<CharacterRuntime>(activeRuntimes))
            {
                RemoveAllBuffs(runtime);
            }
            Instance = null;
        }

        Debug.Log("[BuffManager] 已销毁");
    }

    public void AddBuff(Character target, BuffSO config, Character source)
    {
        operationDepth++;
        try
        {
            AddBuffCore(target, config, source);
        }
        finally
        {
            EndOperation();
        }
    }

    private void AddBuffCore(Character target, BuffSO config, Character source)
    {
        if (isShuttingDown)
        {
            return;
        }
        if (target == null)
        {
            Log.Buff("[Warning] Buff 添加失败：目标角色为空");
            return;
        }

        if (config == null)
        {
            Log.Buff("[Warning] Buff 添加失败：配置为空");
            return;
        }

        if (CharacterRuntimeManager.Instance == null)
        {
            Log.Buff($"[Warning] Buff {config.buffID} 添加失败：CharacterRuntimeManager 尚未初始化");
            return;
        }

        CharacterRuntime runtime = CharacterRuntimeManager.Instance.GetRuntime(target.GetInstanceID());

        if (runtime == null)
        {
            Log.Buff($"[Warning] Buff {config.buffID} 添加失败：目标角色未注册 CharacterRuntime");
            return;
        }

        if (runtime.IsDead || runtime.IsDisposed || clearingRuntimes.Contains(runtime))
        {
            return;
        }

        Buff existingBuff = runtime.FindBuff(config.buffID);

        if (existingBuff != null)
        {
            ReapplyBuff(existingBuff);
            return;
        }

        CreateBuff(runtime, target, config, source);
    }

    public bool RemoveBuff(CharacterRuntime runtime, Buff buff)
    {
        operationDepth++;
        try
        {
            return RemoveBuffCore(runtime, buff);
        }
        finally
        {
            EndOperation();
        }
    }

    private bool RemoveBuffCore(CharacterRuntime runtime, Buff buff)
    {
        if (runtime == null || buff == null || !runtime.DetachBuff(buff))
        {
            return false;
        }
        // 先脱离索引，移除回调即使再次请求移除，也不会重复扣除属性。
        if (runtime.Buffs.Count == 0)
        {
            activeRuntimes.Remove(runtime);
        }
        try
        {
            ExecuteRemove(buff);
        }
        finally
        {
            pendingReturns.Add(buff);
        }
        return true;
    }

    public void RemoveAllBuffs(CharacterRuntime runtime)
    {
        operationDepth++;
        try
        {
            RemoveAllBuffsCore(runtime);
        }
        finally
        {
            EndOperation();
        }
    }

    private void RemoveAllBuffsCore(CharacterRuntime runtime)
    {
        if (runtime == null || !clearingRuntimes.Add(runtime))
        {
            return;
        }
        try
        {
            // 清理期间禁止重新施加；单个移除可能触发其他移除回调。
            while (runtime.Buffs.Count > 0)
            {
                RemoveBuff(runtime, runtime.Buffs[runtime.Buffs.Count - 1]);
            }
            activeRuntimes.Remove(runtime);
        }
        finally
        {
            clearingRuntimes.Remove(runtime);
        }
    }
    private void UpdateDuration(CharacterRuntime runtime, Buff buff, float deltaTime)
    {
        buff.UpdateDuration(deltaTime);

        if (!buff.IsExpired)
        {
            return;
        }

        RemoveBuff(runtime, buff);

        Log.Buff($"[BuffManager] Buff 到期移除：{buff.DisplayName}");
    }

    private void UpdateTick(CharacterRuntime runtime, Buff buff, float deltaTime)
    {
        int tickCount = buff.UpdateTick(deltaTime);

        for (int i = 0; i < tickCount; i++)
        {
            if (runtime.IsDead || runtime.IsDisposed || !ContainsBuff(runtime, buff))
            {
                break;
            }

            ExecuteTick(buff);
        }
    }

    private void ReapplyBuff(Buff buff)
    {
        bool stackIncreased = buff.Reapply();

        if (stackIncreased)
        {
            ExecuteStack(buff);

            Log.Buff($"[BuffManager] Buff 叠加：{buff.DisplayName}，当前层数 {buff.CurrentStack}/{buff.MaxStack}");
            return;
        }

        Log.Buff($"[BuffManager] Buff 已达到最大层数，仅刷新持续时间：{buff.DisplayName}，当前层数 {buff.CurrentStack}/{buff.MaxStack}");
    }

    private void CreateBuff(CharacterRuntime runtime, Character target, BuffSO config, Character source)
    {
        Buff newBuff;

        try
        {
            newBuff = pool.Rent(config, source, target, runtime);
        }
        catch (Exception exception)
        {
            Log.Buff($"[Error] Buff {config.buffID} 创建失败");
            Debug.LogException(exception);
            return;
        }
        if (!runtime.AddBuff(newBuff))
        {
            pendingReturns.Add(newBuff);
            return;
        }

        activeRuntimes.Add(runtime);
        try
        {
            executor.OnApply(newBuff);
        }
        catch (Exception exception)
        {
            RemoveBuff(runtime, newBuff);

            Log.Buff($"[Error] Buff {newBuff.DisplayName} 初始效果执行异常，已取消添加");
            Debug.LogException(exception);
            return;
        }

        Log.Buff($"[BuffManager] Buff 添加：{newBuff.DisplayName}，层数 {newBuff.CurrentStack}/{newBuff.MaxStack}，持续 {newBuff.RemainingTime} 秒");
    }

    private void ExecuteTick(Buff buff)
    {
        try
        {
            executor.OnTick(buff);
        }
        catch (Exception exception)
        {
            Log.Buff($"[Error] Buff {buff.DisplayName} Tick 执行异常");
            Debug.LogException(exception);
        }
    }

    private void ExecuteStack(Buff buff)
    {
        try
        {
            executor.OnStack(buff);
        }
        catch (Exception exception)
        {
            Log.Buff($"[Error] Buff {buff.DisplayName} 叠层效果执行异常");
            Debug.LogException(exception);
        }
    }

    private void ExecuteRemove(Buff buff)
    {
        try
        {
            executor.OnRemove(buff);
        }
        catch (Exception exception)
        {
            Log.Buff($"[Error] Buff {buff.DisplayName} 移除效果执行异常");
            Debug.LogException(exception);
        }
    }

}