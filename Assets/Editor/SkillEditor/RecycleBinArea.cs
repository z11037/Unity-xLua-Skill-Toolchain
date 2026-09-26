using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class RecycleBinArea
{
    private string recycleBinPath;
    private SkillEditorWindow parentWindow;

    public RecycleBinArea(string path, SkillEditorWindow parent)
    {
        recycleBinPath = path;
        parentWindow = parent;
    }

    public void Draw()
    {
        if (!System.IO.Directory.Exists(recycleBinPath))
            return;

        string[] recycleGuids = AssetDatabase.FindAssets("t:SkillSO", new[] { recycleBinPath });
        if (recycleGuids.Length == 0)
            return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("回收站", EditorStyles.boldLabel);

        foreach (string guid in recycleGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            // 撤销新建所暂存的资源，只通过原生重做恢复。
            if (path.StartsWith(SkillResourceUndoJournal.CachePath + "/", System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            SkillSO recycleSkill = AssetDatabase.LoadAssetAtPath<SkillSO>(path);
            if (recycleSkill == null) continue;

            Color oldColor = GUI.color;
            GUI.color = Color.gray;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"[{recycleSkill.skillID}] {recycleSkill.skillName} (已删除)");

            if (GUILayout.Button("恢复", GUILayout.Width(50)))
            {
                parentWindow.RestoreFromRecycleBin(recycleSkill);
            }
            EditorGUILayout.EndHorizontal();
            GUI.color = oldColor;
        }
    }
}