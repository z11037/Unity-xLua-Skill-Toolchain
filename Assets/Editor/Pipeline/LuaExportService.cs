using System;
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
    public static void ExportSkill(SkillSO skill, string executeBody = null, string moduleDeclarations = null, UnityEngine.Object[] resources = null, bool updateBoundScript = false)
    {
        if (skill == null)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(skill.skillName))
        {
            return;
        }

        if (executeBody != null || moduleDeclarations != null)
        {
            string body = BuildTemplate(skill, executeBody, moduleDeclarations, resources != null);
            if (updateBoundScript)
            {
                ExportBoundTemplate(skill, body, resources ?? new UnityEngine.Object[0]);
            }
            else
            {
                ExportCustomTemplate(skill, body, resources ?? new UnityEngine.Object[0]);
            }
            return;
        }
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

        EnsureLuaFolder();
        string luaContent = BuildTemplate(skill);
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
    // null 使用默认片段，空字符串明确生成空内容。
    public static string BuildTemplate(SkillSO skill, string executeBody = null, string moduleDeclarations = null, bool includeResources = false)
    {
        string declarations = moduleDeclarations == null || moduleDeclarations.Length == 0 ? "" : moduleDeclarations + "\n\n";
        string parameters = includeResources ? "attacker, target, resources" : "attacker, target";
        string body = executeBody ?? @"    -- TODO 播放动画

    -- TODO 播放特效

    -- TODO 造成伤害
    target:TakeDamage(attacker:GetAttackValue())";
        return $@"-- ====================================
-- Skill : {skill.skillName}
-- ID    : {skill.skillID}
-- Auto Generated
-- ====================================

local skill = {{}}

skill.cooldown = {skill.cooldown.ToString(System.Globalization.CultureInfo.InvariantCulture)}

{declarations}------------------------------------------------
-- 技能执行入口
------------------------------------------------
function skill.Execute({parameters})

{body}

end

return skill
";
    }

    // Graph 调用方明确确认覆盖后，原位更新脚本，并同步所有共享技能的模板资源。
    private static void ExportBoundTemplate(SkillSO skill, string body, UnityEngine.Object[] resources)
    {
        string path = AssetDatabase.GetAssetPath(skill.luaScript);
        if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            throw new InvalidOperationException("当前绑定的 Lua 文件不存在或路径无效。");
        }
        string guid = AssetDatabase.AssetPathToGUID(path);
        var owners = new List<SkillSO>();
        foreach (string skillGuid in AssetDatabase.FindAssets("t:SkillSO"))
        {
            string ownerPath = AssetDatabase.GUIDToAssetPath(skillGuid);
            if (ownerPath.Contains("/RecycleBin/"))
            {
                continue;
            }
            SkillSO owner = AssetDatabase.LoadAssetAtPath<SkillSO>(ownerPath);
            if (owner != null && SkillRepository.GetLuaGuid(owner) == guid)
            {
                owners.Add(owner);
            }
        }
        File.WriteAllText(path, body, new System.Text.UTF8Encoding(false));
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        foreach (SkillSO owner in owners)
        {
            owner.luaResources = (UnityEngine.Object[])resources.Clone();
            EditorUtility.SetDirty(owner);
        }
    }

    private static void ExportCustomTemplate(SkillSO skill, string body, UnityEngine.Object[] resources)
    {
        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(skill));
        if (string.IsNullOrEmpty(guid))
        {
            throw new InvalidOperationException("请先保存技能资源，再生成 Lua。");
        }

        string marker = "-- ActionLuaOwner: " + guid;
        string path = $"Assets/luaScript/{skill.skillName}_{skill.skillID}.lua";
        if (File.Exists(path) && !File.ReadAllText(path).Replace("\r\n", "\n").StartsWith(marker + "\n", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("生成路径已有非本技能拥有的文件，未覆盖：" + path);
        }
        string scriptGuid = AssetDatabase.AssetPathToGUID(path);
        int ownReference = SkillRepository.GetLuaGuid(skill) == scriptGuid ? 1 : 0;
        if (!string.IsNullOrEmpty(scriptGuid) && SkillRepository.GetLuaReferenceCount(scriptGuid) > ownReference)
        {
            throw new InvalidOperationException("生成脚本被多个技能共享，请先解除共享再重新生成。");
        }
        Directory.CreateDirectory("Assets/luaScript");
        File.WriteAllText(path, marker + "\n" + body, new System.Text.UTF8Encoding(false));
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextAsset script = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        if (script == null)
        {
            throw new InvalidOperationException("生成的 Lua 导入失败：" + path);
        }
        skill.luaScript = script;
        skill.filePath = path;
        skill.luaResources = resources;
        EditorUtility.SetDirty(skill);
        SkillRepository.InvalidateLuaReferenceCounts();
    }

    public static BuildResult ExportAll()
    {
        List<SkillSO> skills = SkillRepository.LoadAll();

        Export(skills);

        int count = skills.Count;
        return BuildResult.Ok(count, $"处理 {count} 个 Lua 技能");
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