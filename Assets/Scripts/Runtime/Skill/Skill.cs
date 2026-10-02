using System;
using UnityEngine;
using XLua;

[CSharpCallLua]
public delegate void SkillExecute(Character attacker, Character target, UnityEngine.Object[] resources);

public sealed class Skill : IDisposable
{
    public SkillSO Config { get; private set; }

    public int SkillId => Config.skillID;
    public string SkillName => Config.skillName;

    public float CurrentCooldown { get; private set; }
    public bool CanExecute => !disposed && !isExecuting && CurrentCooldown <= 0f;

    private bool disposed;
    private bool isExecuting;
    private LuaTable luaTable;
    private SkillExecute execute;
    public bool IsReady
    {
        get
        {
            return !disposed && execute != null;
        }
    }

    public Skill(SkillSO config)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        Config = config;
        CurrentCooldown = 0f;

        LoadLuaModule();
    }

    private void LoadLuaModule()
    {
        if (LuaManager.Instance == null)
        {
            Log.Skill($"[Error] LuaManager.Instance为空，无法加载技能 {SkillId}");
            return;
        }

        if (string.IsNullOrWhiteSpace(Config.filePath))
        {
            Log.Skill($"[Error] 技能 {SkillId} 的Lua路径为空");
            return;
        }

        try
        {
            string moduleName = LuaManager.Instance.NormalizeLuaModuleName(Config.filePath);
            Log.Skill($"技能 {SkillId} 开始加载Lua模块：{moduleName}");

            luaTable = LuaManager.Instance.RequireModule(Config.filePath);

            if (luaTable == null)
            {
                Log.Skill($"[Error] 技能 {SkillId} 的Lua模块加载失败");
                return;
            }

            execute = luaTable.Get<SkillExecute>("Execute");

            if (execute == null)
            {
                Log.Skill($"[Error] 技能 {SkillId} 的Lua模块不存在Execute函数");
                luaTable.Dispose();
                luaTable = null;
                return;
            }

            Log.Skill($"技能 {SkillId} Lua模块加载成功");
        }
        catch (Exception exception)
        {
            Log.Skill($"[Error] 技能 {SkillId} Lua模块加载失败");
            Debug.LogException(exception);
        }
    }

    public bool TryExecute(Character caster, Character target)
    {
        if (!CanExecute)
        {
            Log.Skill( $"技能 {SkillId} 冷却中，剩余 {CurrentCooldown:F1} 秒");

            return false;
        }

        if (execute == null)
        {
            Log.Skill( $"[Error] 技能 {SkillId} 的 Execute 委托为空");

            return false;
        }

        if (caster == null || float.IsNaN(Config.cooldown) || float.IsInfinity(Config.cooldown) || Config.cooldown < 0f)
        {
            Log.Skill($"[Error] 技能 {SkillId} 的施法者为空或冷却配置无效");

            return false;
        }

        isExecuting = true;
        try
        {
            Log.Skill( $"技能 {SkillId} 开始执行");

            execute(caster, target, Config.luaResources);
        }
        catch (Exception exception)
        {
            Log.Skill($"[Error] 技能 {SkillId} 的 Lua 逻辑执行失败");
            Debug.LogException(exception);
            return false;
        }

        finally
        {
            isExecuting = false;
        }



        CurrentCooldown = Mathf.Max(0f, Config.cooldown);

        Log.Skill( $"技能 {SkillId} 执行完成，冷却时间：{CurrentCooldown:F1} 秒");

        return true;
    }

    public void Tick(float deltaTime)
    {
        if (CurrentCooldown <= 0f)
            return;

        CurrentCooldown -= deltaTime;

        if (CurrentCooldown <= 0f)
        {
            CurrentCooldown = 0f;

            Log.Skill( $"技能 {SkillId} 冷却结束");
        }
    }

    public void ResetCooldown()
    {
        CurrentCooldown = 0f;

        Log.Skill( $"技能 {SkillId} 冷却已重置");
    }

    public void Dispose()
    {
        disposed = true;
        execute = null;

        if (luaTable != null)
        {
            luaTable.Dispose();
            luaTable = null;
        }

        Log.Skill( $"技能 {SkillId} 运行时资源已释放");
    }
}