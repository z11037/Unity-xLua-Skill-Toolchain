using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SkillRepository
{
    // 只统计有效区 SkillSO；回收站配置仍可恢复，但不占用有效引用数。
    private static readonly Dictionary<string, int> luaReferenceCounts = new Dictionary<string, int>();
    private static bool referenceCountsDirty = true;

    static SkillRepository()
    {
        EditorApplication.projectChanged += InvalidateLuaReferenceCounts;
        Undo.undoRedoPerformed += InvalidateLuaReferenceCounts;
        Undo.postprocessModifications += OnPropertiesModified;
    }

    private static UndoPropertyModification[] OnPropertiesModified(UndoPropertyModification[] modifications)
    {
        // Inspector 中修改引用也会使缓存失效；不改变原有撤销记录。
        InvalidateLuaReferenceCounts();
        return modifications;
    }
    public static void InvalidateLuaReferenceCounts()
    {
        referenceCountsDirty = true;
    }

    public static string GetLuaGuid(SkillSO skill)
    {
        string path = GetLuaPath(skill);
        return string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
    }

    private static string GetLuaPath(SkillSO skill)
    {
        if (skill == null)
        {
            return string.Empty;
        }

        // 优先使用资源身份；路径只作为尚未迁移的旧配置的兼容入口。
        string path = skill.luaScript != null ? AssetDatabase.GetAssetPath(skill.luaScript) : skill.filePath;
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return path?.Replace('\\', '/') ?? string.Empty;
    }

    private static bool IsRecycled(string path)
    {
        return path.StartsWith(SkillPathConfig.RecycleBin + "/", StringComparison.Ordinal);
    }

    public static void RebuildLuaReferenceCounts()
    {
        luaReferenceCounts.Clear();
        // 统计全项目，避免漏掉不在编辑器列表目录中的技能。
        foreach (string skillGuid in AssetDatabase.FindAssets("t:SkillSO"))
        {
            string path = AssetDatabase.GUIDToAssetPath(skillGuid);
            if (IsRecycled(path))
            {
                continue;
            }

            SkillSO skill = AssetDatabase.LoadAssetAtPath<SkillSO>(path);
            string luaGuid = GetLuaGuid(skill);
            if (string.IsNullOrEmpty(luaGuid))
            {
                continue;
            }

            luaReferenceCounts.TryGetValue(luaGuid, out int count);
            luaReferenceCounts[luaGuid] = count + 1;
        }

        referenceCountsDirty = false;
    }

    public static int GetLuaReferenceCount(string luaGuid)
    {
        if (referenceCountsDirty)
        {
            RebuildLuaReferenceCounts();
        }

        return !string.IsNullOrEmpty(luaGuid) && luaReferenceCounts.TryGetValue(luaGuid, out int count) ? count : 0;
    }

    /// <summary>
    /// 加载
    /// </summary>
    public static List<SkillSO> LoadAll()
    {
        var skills = new List<SkillSO>();

        string[] guids = AssetDatabase.FindAssets("t:SkillSO", new[] { SkillPathConfig.SkillFolder });

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (path.Contains("/RecycleBin/"))
            {
                continue;
            }

            SkillSO skill = AssetDatabase.LoadAssetAtPath<SkillSO>(path);

            if (skill != null)
            {
                skills.Add(skill);
            }
        }

        skills.Sort((a, b) => a.skillID.CompareTo(b.skillID));
        RebuildLuaReferenceCounts();

        return skills;
    }

    /// <summary>
    /// 创建
    /// </summary>
    public static SkillSO Create(string skillName, int skillId)
    {
        string folderPath = SkillPathConfig.SkillFolder;

        EnsureSkillFolder();

        SkillSO skill = ScriptableObject.CreateInstance<SkillSO>();

        skill.skillID = skillId;
        skill.skillName = skillName;

        string assetName = $"{skillId}_{skillName}.asset";
        string path = $"{folderPath}/{assetName}";

        int counter = 1;

        while (File.Exists(path))
        {
            path = $"{folderPath}/{skillId}_{skillName}_{counter}.asset";
            counter++;
        }

        AssetDatabase.CreateAsset(skill, path);
        AssetDatabase.SaveAssets();

        return skill;
    }

    private static void EnsureSkillFolder()
    {
        if (AssetDatabase.IsValidFolder(SkillPathConfig.SkillFolder))
        {
            return;
        }

        AssetDatabase.CreateFolder("Assets/ScriptableObjects", "SkillSO");
    }

    /// <summary>
    /// 删除到回收站
    /// </summary>
    public static UndoStack.UndoAction MoveToRecycleBin(SkillSO skill)
    {
        List<UndoStack.UndoAction> actions = MoveToRecycleBin(new[] { skill });
        return actions.Count == 0 ? default : actions[0];
    }

    public static List<UndoStack.UndoAction> MoveToRecycleBin(IEnumerable<SkillSO> selectedSkills)
    {
        // 删除前强制重建，不能依赖可能被 Inspector 或外部修改过的旧缓存。
        RebuildLuaReferenceCounts();
        var candidates = new List<SkillSO>();
        var uniqueSkills = new HashSet<SkillSO>();
        var selectedCounts = new Dictionary<string, int>();
        var skillLuaGuids = new Dictionary<SkillSO, string>();
        foreach (SkillSO skill in selectedSkills)
        {
            if (skill == null || !uniqueSkills.Add(skill))
            {
                continue;
            }

            string path = AssetDatabase.GetAssetPath(skill);
            if (string.IsNullOrEmpty(path) || IsRecycled(path))
            {
                continue;
            }

            candidates.Add(skill);
            string luaGuid = GetLuaGuid(skill);
            skillLuaGuids[skill] = luaGuid;
            if (!string.IsNullOrEmpty(luaGuid))
            {
                selectedCounts.TryGetValue(luaGuid, out int count);
                selectedCounts[luaGuid] = count + 1;
            }
        }

        var actions = new List<UndoStack.UndoAction>();
        var confirmedLuaGuids = new HashSet<string>();
        // 所有确认先完成；取消任一确认时，本批次尚未移动任何资源。
        foreach (var pair in selectedCounts)
        {
            if (GetLuaReferenceCount(pair.Key) - pair.Value != 0)
            {
                continue;
            }

            string luaPath = AssetDatabase.GUIDToAssetPath(pair.Key);
            if (!File.Exists(luaPath) || IsRecycled(luaPath))
            {
                continue;
            }

            int choice = EditorUtility.DisplayDialogComplex("Lua 有效引用即将归零", $"本次删除后，没有有效技能引用：\n{luaPath}\n\n是否同时将 Lua 移入回收站？\n计数仅覆盖 SkillSO，不包含 Lua require 依赖。", "同时回收Lua", "取消本次删除", "保留Lua");
            if (choice == 1)
            {
                return actions;
            }
            if (choice == 0)
            {
                confirmedLuaGuids.Add(pair.Key);
            }
        }

        if (candidates.Count == 0)
        {
            return actions;
        }

        EnsureRecycleBin();
        try
        {
            foreach (SkillSO skill in candidates)
            {
                string currentPath = AssetDatabase.GetAssetPath(skill);
                string recyclePath = GetAvailableRecyclePath(Path.GetFileName(currentPath));
                string error = AssetDatabase.MoveAsset(currentPath, recyclePath);
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogError($"移动技能失败：{error}");
                    continue;
                }

                actions.Add(new UndoStack.UndoAction
                {
                    type = UndoStack.UndoActionType.Delete,
                    skill = skill,
                    originalPath = currentPath,
                    recyclePath = recyclePath
                });
            }

            // 按实际成功结果重建；有技能移动失败时，其引用仍会保护 Lua。
            RebuildLuaReferenceCounts();
            foreach (string luaGuid in confirmedLuaGuids)
            {
                if (GetLuaReferenceCount(luaGuid) != 0)
                {
                    continue;
                }

                for (int i = actions.Count - 1; i >= 0; i--)
                {
                    if (skillLuaGuids[(SkillSO)actions[i].skill] != luaGuid)
                    {
                        continue;
                    }

                    UndoStack.UndoAction action = actions[i];
                    MoveLuaToRecycleBin(AssetDatabase.GUIDToAssetPath(luaGuid), ref action);
                    actions[i] = action;
                    break;
                }
            }
        }
        finally
        {
            RebuildLuaReferenceCounts();
        }

        return actions;
    }
    private static string GetAvailableRecyclePath(string fileName)
    {
        string recyclePath = $"{SkillPathConfig.RecycleBin}/{fileName}";

        int counter = 1;

        while (File.Exists(recyclePath))
        {
            string name = Path.GetFileNameWithoutExtension(fileName);

            recyclePath = $"{SkillPathConfig.RecycleBin}/{name}_{counter}.asset";

            counter++;
        }

        return recyclePath;
    }

    private static void MoveLuaToRecycleBin(string luaPath, ref UndoStack.UndoAction action)
    {
        if (string.IsNullOrEmpty(luaPath))
        {
            return;
        }

        if (!File.Exists(luaPath))
        {
            return;
        }

        string luaName = Path.GetFileName(luaPath);
        string recycleLua = $"{SkillPathConfig.RecycleBin}/{luaName}";

        string error = AssetDatabase.MoveAsset(luaPath, recycleLua);

        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogError($"移动Lua失败：{error}");
            return;
        }

        action.luaOriginalPath = luaPath;
        action.luaRecyclePath = recycleLua;
    }

    public static void EnsureRecycleBin()
    {
        if (AssetDatabase.IsValidFolder(SkillPathConfig.RecycleBin))
        {
            return;
        }

        string parent = Path.GetDirectoryName(SkillPathConfig.RecycleBin);
        string folder = Path.GetFileName(SkillPathConfig.RecycleBin);

        AssetDatabase.CreateFolder(parent, folder);
    }

    /// <summary>
    /// 从回收站恢复
    /// </summary>
    public static UndoStack.UndoAction RestoreFromRecycleBin(SkillSO recycleSkill)
    {
        if (recycleSkill == null)
        {
            return default;
        }

        string recyclePath = AssetDatabase.GetAssetPath(recycleSkill);

        if (string.IsNullOrEmpty(recyclePath))
        {
            return default;
        }

        string fileName = Path.GetFileName(recyclePath);
        string targetPath = $"{SkillPathConfig.SkillFolder}/{fileName}";

        int counter = 1;

        while (File.Exists(targetPath))
        {
            string name = Path.GetFileNameWithoutExtension(fileName);

            targetPath = $"{SkillPathConfig.SkillFolder}/{name}_{counter}.asset";

            counter++;
        }

        string error = AssetDatabase.MoveAsset(recyclePath, targetPath);

        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogError($"恢复技能失败：{error}");
            return default;
        }

        SkillSO restoredSkill = AssetDatabase.LoadAssetAtPath<SkillSO>(targetPath);

        var action = new UndoStack.UndoAction
        {
            type = UndoStack.UndoActionType.Restore,
            skill = restoredSkill,
            recyclePath = recyclePath,
            originalPath = targetPath
        };

        RestoreLuaFile(restoredSkill, ref action);
        RebuildLuaReferenceCounts();

        return action;
    }

    private static void RestoreLuaFile(SkillSO skill, ref UndoStack.UndoAction action)
    {
        if (skill == null || string.IsNullOrEmpty(skill.filePath))
        {
            return;
        }

        string luaName = Path.GetFileName(skill.filePath);
        string recycleLua = $"{SkillPathConfig.RecycleBin}/{luaName}";

        if (!File.Exists(recycleLua))
        {
            return;
        }

        string targetLua = skill.filePath.Replace("\\", "/");

        string error = AssetDatabase.MoveAsset(recycleLua, targetLua);

        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogError($"恢复Lua失败：{error}");
            return;
        }

        action.luaRecyclePath = recycleLua;
        action.luaOriginalPath = targetLua;
    }
}