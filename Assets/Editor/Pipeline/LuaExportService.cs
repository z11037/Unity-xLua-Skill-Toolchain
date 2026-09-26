using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
public static class LuaExportService
{

    private static string LuaFolder =>
        Path.Combine(Application.dataPath, "luaScript");
    private static void EnsureLuaFolder()
    {
        if (!Directory.Exists(LuaFolder))
            Directory.CreateDirectory(LuaFolder);
    }
    private static void ExportSkill(SkillSO skill)
    {
        if (string.IsNullOrWhiteSpace(skill.skillName))
            return;

        // 已有引用优先，导出模板不能覆盖手动绑定或共享脚本。
        if (skill.luaScript != null)
        {
            if (!SkillLuaReferenceUtility.IsLuaPath(AssetDatabase.GetAssetPath(skill.luaScript)))
            {
                throw new System.InvalidOperationException($"技能 {skill.skillID} 引用了非 Lua 资源。" );
            }
            skill.SyncLuaPath();
            EditorUtility.SetDirty(skill);
            return;
        }

        if (!string.IsNullOrWhiteSpace(skill.filePath))
        {
            TextAsset existingScript = SkillLuaReferenceUtility.LoadScript(skill.filePath);
            if (existingScript == null)
            {
                throw new System.InvalidOperationException($"技能 {skill.skillID} 的 Lua 路径无效：{skill.filePath}，请重新绑定后再导出。");
            }

            skill.luaScript = existingScript;
            skill.SyncLuaPath();
            EditorUtility.SetDirty(skill);
            return;
        }

        string luaContent =
$@"-- ====================================
-- Skill : {skill.skillName}
-- ID    : {skill.skillID}
-- Auto Generated
-- ====================================

local skill = {{}}

skill.cooldown = {skill.cooldown}

------------------------------------------------
-- 技能执行入口
------------------------------------------------
function skill.Execute(attacker, target)

    -- TODO 播放动画

    -- TODO 播放特效

    -- TODO 造成伤害
    target:TakeDamage(attacker:GetAttackValue())

end

return skill
";
        string fileName = $"{skill.skillName}_{skill.skillID}.lua";
        string luaPath = Path.Combine(LuaFolder, fileName);
        string relativePath = "Assets/luaScript/" + fileName;

        if (!File.Exists(luaPath))
        {
            File.WriteAllText(luaPath, luaContent);
        }

        TextAsset script = SkillLuaReferenceUtility.LoadScript(relativePath);
        if (script == null)
        {
            throw new System.InvalidOperationException($"Lua 资源导入失败：{relativePath}");
        }

        skill.luaScript = script;
        skill.SyncLuaPath();
        EditorUtility.SetDirty(skill);
    }
    public static BuildResult ExportAll()
    {
        List<SkillSO> skills = SkillRepository.LoadAll();

        Export(skills);

        return BuildResult.Ok(
            skills.Count,
            $"导出 {skills.Count} 个 Lua 文件");
    }
    public static void Export(IEnumerable<SkillSO> skills)
    {
        EnsureLuaFolder();

        foreach (var skill in skills)
        {
            ExportSkill(skill);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}