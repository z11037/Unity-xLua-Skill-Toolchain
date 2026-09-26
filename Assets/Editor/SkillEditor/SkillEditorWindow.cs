using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.PackageManager;
using UnityEngine;
using UndoAction = UndoStack.UndoAction;
using UndoActionType = UndoStack.UndoActionType;

public class SkillEditorWindow : EditorWindow
{
    private HashSet<SkillSO> selectedSkills = new();
    private Dictionary<SkillSO, bool> foldouts = new();
    private List<SkillSO> skills = new();
    private string newSkillName = "";
    private string searchFilter = "";
    private bool sortByName = true; // true=按名字排序，false=按ID排序
    private double lastValidationTime = 0;
    private const double validationInterval = 0.5; // 每0.5秒最多校验一次
    private SkillTag tagFilter = (SkillTag)(-1); // -1 = All
    private RecycleBinArea recycleBinArea;
    // 记录每次删除操作涉及的文件路径映射：原路径 → 回收站路径
    private string recycleBinPath = SkillPathConfig.RecycleBin;
    private string lastValidationLog = "";

    private readonly UndoStack unifiedUndoStack = new();

    [MenuItem("Tools/技能/技能编辑器")]
    public static void ShowWindow()
    {
        EditorWindow.GetWindow<SkillEditorWindow>("技能编辑器");

    }

    private void OnEnable()
    {
        SkillResourceUndoJournal.Changed += RefreshAfterUndo;
        LoadSkillData();
        recycleBinArea = new RecycleBinArea(recycleBinPath, this);
    }

    private void OnGUI()
    {
        if (!string.IsNullOrEmpty(SkillResourceUndoJournal.instance.Failure))
        {
            EditorGUILayout.HelpBox(SkillResourceUndoJournal.instance.Failure, MessageType.Error);
            if (GUILayout.Button("重试资源撤销/重做"))
            {
                SkillResourceUndoJournal.instance.Synchronize();
            }
        }
        DrawToolbar();                    // 刷新、删除选中
        DrawCreateSkillPanel();           // 新增技能输入框 + 添加按钮
        DrawValidationAndExportButtons(); // 校验、保存、导出Lua
        DrawSkillList();                  // 技能折叠列表
        DrawSelectedInfo();               // 当前选中显示
        recycleBinArea.Draw();
        // OnGUI 末尾，自动校验
        if (EditorApplication.timeSinceStartup - lastValidationTime > validationInterval)
        {
            string currentLog = "";
            var errors = SkillValidator.Validate(skills);

            if (errors.Count == 0)
            {
                currentLog = "✅ 所有配置校验通过。";
            }
            else
            {
                // 将所有错误信息拼接成一个字符串，用作对比
                currentLog = string.Join("\n", errors);
            }

            // 结果不变时不重复输出
            if (currentLog != lastValidationLog)
            {
                lastValidationLog = currentLog;
                if (errors.Count == 0)
                    Debug.Log(currentLog);
                else
                    foreach (string err in errors)
                        Debug.LogWarning(err);
            }

            lastValidationTime = EditorApplication.timeSinceStartup;
        }
    }

    private void OnDisable()
    {
        SkillResourceUndoJournal.Changed -= RefreshAfterUndo;
    }

    private void RefreshAfterUndo()
    {
        LoadSkillData();
        selectedSkills.Clear();
        foldouts.Clear();
        Repaint();
    }

    private void DrawToolbar()
    {
        if (GUILayout.Button("刷新列表"))
        {
            LoadSkillData();
            selectedSkills.Clear();
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("删除选中"))
        {
            DeleteSelectedSkills();
        }
        EditorGUILayout.Space();

        if (GUILayout.Button("清空回收站"))
        {
            ClearRecycleBin();
        }
    }
    private void ClearRecycleBin()
    {
        if (!SkillResourceUndoJournal.instance.CanOperate())
        {
            return;
        }

        if (System.IO.Directory.Exists(SkillPathConfig.RecycleBin))
        {
            // 确认对话框，防止误触
            if (EditorUtility.DisplayDialog(
                "清空回收站",
                "确定要永久删除回收站中的所有技能及撤销暂存资源吗？此操作不可撤销，并会清除本工具的资源撤销/重做记录。",
                "确定",
                "取消"))
            {
                SkillResourceUndoJournal.instance.ClearHistory();
                // 删除回收站文件夹
                System.IO.Directory.Delete(recycleBinPath, true);
                // 删除对应的 .meta 文件
                string metaPath = recycleBinPath + ".meta";
                if (System.IO.File.Exists(metaPath))
                {
                    System.IO.File.Delete(metaPath);
                }
                AssetDatabase.Refresh();
                Debug.Log("回收站已清空。");
            }
        }
        else
        {
            Debug.Log("回收站已空。");
        }
    }
    private void DeleteSelectedSkills()
    {
        if (!SkillResourceUndoJournal.instance.CanOperate())
        {
            return;
        }

        if (selectedSkills.Count == 0) return;

        var skillsToDelete = new List<SkillSO>(selectedSkills);

        // Repository 按整批计算共享引用，并在移动前完成归零确认。
        List<UndoStack.UndoAction> actions = SkillRepository.MoveToRecycleBin(skillsToDelete);
        foreach (UndoStack.UndoAction action in actions)
        {
            SkillSO deletedSkill = action.skill as SkillSO;
            selectedSkills.Remove(deletedSkill);
            skills.Remove(deletedSkill);
            foldouts.Remove(deletedSkill);
        }
        // 将本次批量资源操作登记到原生撤销时间线。
        unifiedUndoStack.Record(actions);

        AssetDatabase.Refresh();
        LoadSkillData();
        Repaint();
    }
    private void DrawCreateSkillPanel()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("新增技能", EditorStyles.boldLabel);
        newSkillName = EditorGUILayout.TextField("技能名称", newSkillName);
        if (GUILayout.Button("添加技能"))
        {
            CreateNewSkill();
        }
    }

    private void CreateNewSkill()
    {
        if (!SkillResourceUndoJournal.instance.CanOperate())
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(newSkillName))
        {
            int maxID = skills.Count > 0 ? skills.Max(s => s.skillID) : 1000;

            SkillSO newSkill = SkillRepository.Create(newSkillName.Trim(), maxID + 1);

            skills.Add(newSkill);

            newSkillName = "";
            GUI.FocusControl(null);

            var action = new UndoAction
            {
                type = UndoActionType.Create,
                skill = newSkill
            };

            unifiedUndoStack.Record(new List<UndoAction> { action });
        }
    }

    private void DrawValidationAndExportButtons()
    {
        if (GUILayout.Button("保存修改"))
        {
            AssetDatabase.SaveAssets();
            Debug.Log("技能数据已保存。");
        }

        if (GUILayout.Button("校验配置"))
        {
            ValidateSkills();
            
        }

        if (GUILayout.Button("Lua脚本导出"))
        {
            ExportLuaTemplates();
        }

        if (GUILayout.Button("技能配置构建"))
        {
            BuildSkillConfig();
        }
    }

    private void BuildSkillConfig()
    {
        SkillPipeline.Build();
    }
    private void ExportLuaTemplates()
    {
        LuaExportService.Export(skills);
    }
    private void ValidateSkills()
    {
        var errors = SkillValidator.Validate(skills);
        if (errors.Count == 0)
            Debug.Log("所有配置校验通过。");
        else
            foreach (string err in errors)
                Debug.LogWarning(err);
    }

    Vector2 scrollPosition;
    private void DrawSkillList()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        string[] options = System.Enum.GetNames(typeof(SkillTag));
        int index = (int)tagFilter + 1; // +1 因为有“全部”

        string[] display = new string[options.Length + 1];
        display[0] = "All";
        System.Array.Copy(options, 0, display, 1, options.Length);

        index = EditorGUILayout.Popup("Tag", index, display);

        tagFilter = (SkillTag)(index - 1);

        EditorGUILayout.BeginHorizontal();
        searchFilter = EditorGUILayout.TextField("搜索技能", searchFilter);
        if (GUILayout.Button(sortByName ? "当前：名字排序" : "当前：ID排序", GUILayout.Width(120)))
        {
            sortByName = !sortByName;
            skills.Sort(sortByName ?
                (a, b) => a.skillName.CompareTo(b.skillName) :
                (a, b) => a.skillID.CompareTo(b.skillID));


        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();
        EditorGUILayout.Space();
        
        if (skills.Count == 0)
        {
            EditorGUILayout.HelpBox("尚未创建任何技能数据资产。", MessageType.Warning);
            return;
        }
        for (int i = 0; i < skills.Count; i++)
        {
            if (!string.IsNullOrEmpty(searchFilter) && !skills[i].skillName.ToLower().Contains(searchFilter.ToLower()))
            {
                continue;
            }
            if ((int)tagFilter != -1 && skills[i].tag != tagFilter)
            {
                continue;
            }

            DrawSkillItem(skills[i]);
        }
        EditorGUILayout.EndScrollView();
    }

    public void RestoreFromRecycleBin(SkillSO recycleSkill)
    {
        if (!SkillResourceUndoJournal.instance.CanOperate())
        {
            return;
        }

        UndoAction action = SkillRepository.RestoreFromRecycleBin(recycleSkill);

        if (action.skill == null)
        {
            return;
        }

        unifiedUndoStack.Record(new List<UndoStack.UndoAction> { action });

        AssetDatabase.Refresh();
        LoadSkillData();
        Repaint();
    }
    
    private void DrawSkillItem(SkillSO skill)
    {
        if (skill == null) return;
        if (!foldouts.ContainsKey(skill)) foldouts[skill] = false;

        SerializedObject so = new SerializedObject(skill);
        so.Update();

        SerializedProperty nameProp = so.FindProperty("skillName");
        SerializedProperty cooldownProp = so.FindProperty("cooldown");
        SerializedProperty tagProp = so.FindProperty("tag");
        SerializedProperty buffProp = so.FindProperty("associatedBuff");
        SerializedProperty targetProp = so.FindProperty("buffTarget");
        SerializedProperty luaProp = so.FindProperty("luaScript");

        EditorGUILayout.BeginHorizontal();

        // 多选复选框
        bool wasSelected = selectedSkills.Contains(skill);
        bool isSelected = EditorGUILayout.Toggle(wasSelected, GUILayout.Width(20));
        if (isSelected && !wasSelected) selectedSkills.Add(skill);
        else if (!isSelected && wasSelected) selectedSkills.Remove(skill);

        EditorGUILayout.BeginVertical();

        // 折叠面板
        EditorGUILayout.BeginHorizontal();
        foldouts[skill] = EditorGUILayout.Foldout(foldouts[skill], $"[{skill.skillID}] {skill.skillName}");
        EditorGUILayout.EndHorizontal();

        // 展开后的编辑区域
        if (foldouts[skill])
        {
            EditorGUI.BeginChangeCheck();

            // ID（只读）
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.IntField("ID", skill.skillID);
            EditorGUI.EndDisabledGroup();

            // 可编辑字段
            if (tagProp != null)
            {
                EditorGUILayout.PropertyField(tagProp);
            }
            EditorGUILayout.PropertyField(nameProp);
            EditorGUILayout.PropertyField(cooldownProp);

            // 使用资源引用选择 Lua，并保留路径展示以兼容运行时加载。
            EditorGUI.BeginChangeCheck();
            TextAsset selectedLua = (TextAsset)EditorGUILayout.ObjectField("Lua资源", luaProp.objectReferenceValue, typeof(TextAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                string selectedPath = selectedLua == null ? string.Empty : AssetDatabase.GetAssetPath(selectedLua);
                if (selectedLua == null || SkillLuaReferenceUtility.IsLuaPath(selectedPath))
                {
                    luaProp.objectReferenceValue = selectedLua;
                    so.FindProperty("filePath").stringValue = selectedPath;
                }
                else
                {
                    Debug.LogWarning("请选择 .lua 文件，不能绑定普通文本文件。");
                }
            }

            EditorGUILayout.LabelField("Lua路径", so.FindProperty("filePath").stringValue);
            EditorGUILayout.LabelField("Lua有效引用数", SkillRepository.GetLuaReferenceCount(SkillRepository.GetLuaGuid(skill)).ToString());
            if (GUILayout.Button("定位"))
            {
                if (luaProp.objectReferenceValue != null)
                {
                    EditorGUIUtility.PingObject(luaProp.objectReferenceValue);
                }
                else if (File.Exists(skill.filePath))
                {
                    EditorUtility.RevealInFinder(skill.filePath);
                }
            }
            //buffSO引用
            if (buffProp != null)
            {
                EditorGUILayout.PropertyField(buffProp);

                // 只有选了 Buff 才显示目标选择
                if (buffProp.objectReferenceValue != null && targetProp != null)
                {
                    EditorGUILayout.PropertyField(targetProp);
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                so.ApplyModifiedProperties();
                SkillRepository.InvalidateLuaReferenceCounts();
                EditorUtility.SetDirty(skill);
            }

          
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    private void BindLuaScript(SkillSO skill)
    {
        string absolutePath = EditorUtility.OpenFilePanel("选择Lua脚本", Application.dataPath, "lua");

        if (string.IsNullOrEmpty(absolutePath))
        {
            return;
        }

        string assetsPath = Application.dataPath.Replace("\\", "/");
        string selectedPath = absolutePath.Replace("\\", "/");

        if (!selectedPath.StartsWith(assetsPath + "/", System.StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError("Lua文件必须位于Assets目录下");
            return;
        }

        string assetPath = "Assets" + selectedPath.Substring(assetsPath.Length);

        TextAsset script = SkillLuaReferenceUtility.LoadScript(assetPath);
        if (script == null)
        {
            Debug.LogError("Lua 资源导入失败，保留原绑定。");
            return;
        }

        Undo.RecordObject(skill, "绑定Lua资源");
        skill.luaScript = script;
        skill.SyncLuaPath();
        SkillRepository.InvalidateLuaReferenceCounts();

        EditorUtility.SetDirty(skill);
        AssetDatabase.SaveAssets();
    }

    private void DrawSelectedInfo()
    {
        if (selectedSkills.Count > 0)
        {
            EditorGUILayout.Space();
            var selectedNames = new List<string>();
            foreach (SkillSO skill in selectedSkills)
            {
                if (skill != null)
                    selectedNames.Add(skill.skillName);
            }
            EditorGUILayout.LabelField("当前选中", string.Join(", ", selectedNames));
        }
    }

    public void LoadSkillData()
    {
        skills = SkillRepository.LoadAll();
    }


}