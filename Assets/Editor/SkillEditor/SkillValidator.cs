using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
public static class SkillValidator
{
    public static List<string> Validate(List<SkillSO> skills)
    {
        var errors = new List<string>();

        foreach (var skill in skills)
        {
            if (string.IsNullOrWhiteSpace(skill.skillName))
                errors.Add($"警告：技能 ID {skill.skillID} 名称不能为空。");
            if (skill.cooldown < 0)
                errors.Add($"警告：技能 {skill.skillName} 冷却时间不能为负数。");
        }

        var idSet = new HashSet<int>();
        foreach (var skill in skills)
        {
            if (!idSet.Add(skill.skillID))
                errors.Add($"警告：技能 {skill.skillName} 的 ID {skill.skillID} 重复。");
        }

        var nameMap = new Dictionary<string, int>();
        foreach (var skill in skills)
        {
            if (string.IsNullOrWhiteSpace(skill.skillName)) continue;
            if (nameMap.ContainsKey(skill.skillName))
            {
                if (nameMap[skill.skillName] != skill.skillID)
                    errors.Add($"警告：技能名称 \"{skill.skillName}\" 重复。");
            }
            else
                nameMap.Add(skill.skillName, skill.skillID);
        }
        foreach (var skill in skills)
        {
            if (skill.luaScript != null)
            {
                string path = AssetDatabase.GetAssetPath(skill.luaScript);
                if (!SkillLuaReferenceUtility.IsLuaPath(path))
                {
                    errors.Add($"技能 {skill.skillName} 引用的资源不是 .lua 文件。");
                }
                else if (path != skill.filePath)
                {
                    errors.Add($"技能 {skill.skillName} 的 Lua 引用与路径不一致，请执行同步Lua资源引用。");
                }
            }
            else if (!string.IsNullOrWhiteSpace(skill.filePath) && (!SkillLuaReferenceUtility.IsLuaPath(skill.filePath.Replace('\\', '/')) || !System.IO.File.Exists(skill.filePath)))
            {
                errors.Add($"技能 {skill.skillName} 的 Lua 文件不存在或路径无效：{skill.filePath}");
            }
            // 未绑定的新技能允许通过构建，后续模板导出步骤负责创建资源。
        }
        return errors;
    }
}