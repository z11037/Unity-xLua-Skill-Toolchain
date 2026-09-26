using System;
using System.Collections.Generic;
using UnityEngine;

public class BuffManager : MonoBehaviour
{
    public static BuffManager Instance { get; private set; }

    private readonly IBuffExecutor executor = new DefaultBuffExecutor();

    private readonly HashSet<CharacterRuntime> activeRuntimes = new HashSet<CharacterRuntime>();

    private readonly List<CharacterRuntime> removeCache = new List<CharacterRuntime>();

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

        float deltaTime = Time.deltaTime;
        removeCache.Clear();

        foreach (CharacterRuntime runtime in activeRuntimes)
        {

            if (runtime == null)
            {
                continue;
            }

            if (runtime.IsDead)
            {
                RemoveAllBuffs(runtime);
                removeCache.Add(runtime);
                continue;
            }

            // 先结算 Tick，此时每个 Buff 的剩余时间仍是本帧开始时的值。
            for (int i = runtime.TickBuffs.Count - 1; i >= 0; i--)
            {
                Buff buff = runtime.TickBuffs[i];

                if (buff == null)
                {
                    continue;
                }

                UpdateTick(runtime, buff, deltaTime);

                if (runtime.IsDead)
                {
                    break;
                }
            }

            // 退出 Tick 遍历后再统一清理，避免遍历过程中列表索引失效。
            if (runtime.IsDead)
            {
                RemoveAllBuffs(runtime);
                removeCache.Add(runtime);
                continue;
            }

            for (int i = runtime.Buffs.Count - 1; i >= 0; i--)
            {
                Buff buff = runtime.Buffs[i];

                if (buff == null)
                {
                    continue;
                }

                UpdateDuration(runtime, buff, deltaTime);
            }

            if (runtime.Buffs.Count == 0)
            {
                removeCache.Add(runtime);
            }
        }

        for (int i = 0; i < removeCache.Count; i++)
        {
            activeRuntimes.Remove(removeCache[i]);
        }

    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        Debug.Log("[BuffManager] 已销毁");
    }

    public void AddBuff(Character target, BuffSO config, Character source)
    {
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

        Buff existingBuff = runtime.FindBuff(config.buffID);

        if (existingBuff != null)
        {
            ReapplyBuff(existingBuff);
            return;
        }

        CreateBuff(runtime, target, config, source);
    }

    public void RemoveAllBuffs(CharacterRuntime runtime)
    {
        if (runtime == null)
        {
            return;
        }

        for (int i = runtime.Buffs.Count - 1; i >= 0; i--)
        {
            Buff buff = runtime.Buffs[i];

            if (buff == null)
            {
                continue;
            }

            ExecuteRemove(buff);
            runtime.RemoveBuff(buff);
        }

        Log.Buff($"[BuffManager] 角色 {runtime.CharacterId} 的 Buff 已全部清理");
    }

    private void UpdateDuration(CharacterRuntime runtime, Buff buff, float deltaTime)
    {
        buff.UpdateDuration(deltaTime);

        if (!buff.IsExpired)
        {
            return;
        }

        ExecuteRemove(buff);
        runtime.RemoveBuff(buff);

        Log.Buff($"[BuffManager] Buff 到期移除：{buff.DisplayName}");
    }

    private void UpdateTick(CharacterRuntime runtime, Buff buff, float deltaTime)
    {
        int tickCount = buff.UpdateTick(deltaTime);

        for (int i = 0; i < tickCount; i++)
        {
            if (runtime.IsDead)
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
            newBuff = new Buff(config, source, target);
        }
        catch (Exception exception)
        {
            Log.Buff($"[Error] Buff {config.buffID} 创建失败");
            Debug.LogException(exception);
            return;
        }
        if (!runtime.AddBuff(newBuff))
        {
            return;
        }

        activeRuntimes.Add(runtime);
        try
        {
            executor.OnApply(newBuff);
        }
        catch (Exception exception)
        {
            runtime.RemoveBuff(newBuff);

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