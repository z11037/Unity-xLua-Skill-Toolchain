using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SkillLuaReferenceUtility
{
    static SkillLuaReferenceUtility()
    {
        // 等资源导入完成后，兼容只有路径的旧技能配置。
        ScheduleSync();
    }

    public static bool IsLuaPath(string path)
    {
        return !string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal) && path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase);
    }

    public static TextAsset LoadScript(string path)
    {
        path = path?.Replace('\\', '/');
        if (!IsLuaPath(path) || !File.Exists(path))
        {
            return null;
        }

        TextAsset script = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        if (script == null)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            script = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        }

        return script;
    }

    public static void ScheduleSync()
    {
        EditorApplication.delayCall -= SyncReferences;
        EditorApplication.delayCall += SyncReferences;
    }

    [MenuItem("Tools/技能/同步Lua资源引用")]
    public static void SyncReferences()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            ScheduleSync();
            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        List<SkillSO> changedSkills = new List<SkillSO>();
        foreach (string guid in AssetDatabase.FindAssets("t:SkillSO"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            // 回收站保留原始路径，供现有恢复和撤销流程使用。
            if (path.StartsWith(SkillPathConfig.RecycleBin + "/", StringComparison.Ordinal))
            {
                continue;
            }

            SkillSO skill = AssetDatabase.LoadAssetAtPath<SkillSO>(path);
            if (skill == null)
            {
                continue;
            }

            SerializedObject serialized = new SerializedObject(skill);
            bool changed = false;
            SerializedProperty scriptProperty = serialized.FindProperty("luaScript");
            // Missing 引用不能按旧路径自动绑定到另一个新文件。
            if (skill.luaScript == null && scriptProperty.objectReferenceInstanceIDValue == 0)
            {
                TextAsset script = LoadScript(skill.filePath);
                if (script != null)
                {
                    scriptProperty.objectReferenceValue = script;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(skill);
                    changed = true;
                }
            }

            string previousPath = skill.filePath;
            skill.SyncLuaPath();
            if (previousPath != skill.filePath)
            {
                EditorUtility.SetDirty(skill);
                changed = true;
            }

            if (changed)
            {
                changedSkills.Add(skill);
            }
        }

        foreach (SkillSO skill in changedSkills)
        {
            AssetDatabase.SaveAssetIfDirty(skill);
        }

        SkillRepository.InvalidateLuaReferenceCounts();
    }
}

public sealed class SkillLuaReferencePostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        // 导入和移动后延迟同步，避免在资源导入回调内保存配置。
        foreach (string path in importedAssets)
        {
            if (SkillLuaReferenceUtility.IsLuaPath(path))
            {
                SkillLuaReferenceUtility.ScheduleSync();
                return;
            }
        }

        if (movedAssets.Length > 0)
        {
            SkillLuaReferenceUtility.ScheduleSync();
        }
    }
}
