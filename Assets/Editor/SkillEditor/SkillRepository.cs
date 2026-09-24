using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class SkillRepository
{
    public static List<SkillSO> LoadAll()
    {
        var skills = new List<SkillSO>();

        string[] guids = AssetDatabase.FindAssets(
            "t:SkillSO",
            new[] { SkillPathConfig.SkillFolder });

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (path.Contains("/RecycleBin/"))
                continue;

            SkillSO skill =
                AssetDatabase.LoadAssetAtPath<SkillSO>(path);

            if (skill != null)
                skills.Add(skill);
        }


        skills.Sort(
            (a, b) => a.skillID.CompareTo(b.skillID));

        return skills;
    }

    public static SkillSO Create(string skillName, int skillId)
    {
        string folderPath = SkillPathConfig.SkillFolder;

        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder(
                "Assets/ScriptableObjects",
                "SkillSO"
            );
        }


        SkillSO skill = ScriptableObject.CreateInstance<SkillSO>();

        skill.skillID = skillId;
        skill.skillName = skillName;


        string assetName = $"{skillId}_{skillName}.asset";

        string path =
            $"{folderPath}/{assetName}";


        int counter = 1;

        while (System.IO.File.Exists(path))
        {
            path =
                $"{folderPath}/{skillId}_{skillName}_{counter}.asset";

            counter++;
        }


        AssetDatabase.CreateAsset(skill, path);
        AssetDatabase.SaveAssets();

        return skill;
    }
}