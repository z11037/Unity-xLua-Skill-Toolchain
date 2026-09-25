using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class SkillRepository
{
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
        if (skill == null)
        {
            return default;
        }

        string currentPath = AssetDatabase.GetAssetPath(skill);

        if (string.IsNullOrEmpty(currentPath))
        {
            return default;
        }

        string luaPath = skill.filePath;

        if (!string.IsNullOrEmpty(luaPath))
        {
            luaPath = luaPath.Replace("\\", "/");
        }

        string fileName = Path.GetFileName(currentPath);
        string recyclePath = GetAvailableRecyclePath(fileName);

        string error = AssetDatabase.MoveAsset(currentPath, recyclePath);

        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogError($"移动技能失败：{error}");
            return default;
        }

        var action = new UndoStack.UndoAction
        {
            type = UndoStack.UndoActionType.Delete,
            skill = skill,
            originalPath = currentPath,
            recyclePath = recyclePath
        };

        MoveLuaToRecycleBin(luaPath, ref action);

        return action;
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