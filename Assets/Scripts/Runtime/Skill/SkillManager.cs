using System.Collections.Generic;
using UnityEngine;

public sealed class SkillManager : MonoBehaviour
{
    public static SkillManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Log.Skill("[Warning] 检测到重复的 SkillManager，销毁当前对象");
            Destroy(gameObject);
            return;
        }

        Instance = this;

        Log.Skill("SkillManager 初始化完成");
    }

    private void Update()
    {
        if (CharacterRuntimeManager.Instance == null)
        {
            return;
        }

        float deltaTime = Time.deltaTime;

        foreach (CharacterRuntime runtime in CharacterRuntimeManager.Instance.GetAllRuntimes())
        {
            runtime.Tick(deltaTime);
        }
    }

    public bool RegisterSkills(int characterId, List<SkillSO> skillConfigs)
    {
        if (CharacterRuntimeManager.Instance == null)
        {
            Log.Skill($"[Warning] 角色 {characterId} 注册技能失败：CharacterRuntimeManager 未初始化");
            return false;
        }

        CharacterRuntime runtime = CharacterRuntimeManager.Instance.GetRuntime(characterId);

        if (runtime == null)
        {
            Log.Skill($"[Warning] 角色 {characterId} 注册技能失败：Runtime 不存在");
            return false;
        }

        if (skillConfigs == null)
        {
            return true;
        }

        int successCount = 0;

        foreach (SkillSO config in skillConfigs)
        {
            if (config == null)
            {
                continue;
            }

            if (runtime.RegisterSkill(config))
            {
                successCount++;
            }
        }

        Log.Skill($"角色 {characterId} 技能注册完成，成功数量：{successCount}");

        return true;
    }

    public bool RequestCast(int characterId, int skillId, Character caster, Character target)
    {
        return RequestCast(characterId, skillId, caster, target, out _);
    }

    public bool RequestCast(int characterId, int skillId, Character caster, Character target, out SkillCastResult result)
    {
        if (CharacterRuntimeManager.Instance == null)
        {
            result = new SkillCastResult(SkillCastStatus.RuntimeManagerUnavailable);
            Log.Skill($"角色 {characterId} 请求释放技能 {skillId} 被拒绝：{result.Message}");
            return false;
        }
        CharacterRuntime runtime = CharacterRuntimeManager.Instance.GetRuntime(characterId);
        if (runtime == null)
        {
            result = new SkillCastResult(SkillCastStatus.RuntimeNotFound);
            Log.Skill($"角色 {characterId} 请求释放技能 {skillId} 被拒绝：{result.Message}");
            return false;
        }

        Log.Skill($"角色 {characterId} 请求释放技能 {skillId}");
        bool success = runtime.TryCast(skillId, caster, target, out result);
        if (!success)
        {
            string phase = result.Started ? "执行异常" : "请求被拒绝";
            Log.Skill($"角色 {characterId} 释放技能 {skillId} {phase}：{result.Message}");
        }
        return success;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        Log.Skill("SkillManager 已销毁");
    }
}