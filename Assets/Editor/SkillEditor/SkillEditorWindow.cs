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
        LoadSkillData();
        recycleBinArea = new RecycleBinArea(recycleBinPath, this);
    }

    private void OnGUI()
    {
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
        HandleUndoShortcut();
    }

    //暂时妥协的结果
    private void HandleUndoShortcut()
    {
        Event e = Event.current;

        if (e.type != EventType.KeyDown ||
            !e.control ||
            e.keyCode != KeyCode.Z)
            return;


        if (unifiedUndoStack.HasUndo())
        {
            unifiedUndoStack.PerformUndo();
            // 刷新数据并重绘UI
            LoadSkillData();
            Repaint();
            e.Use();
        }
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
        if (System.IO.Directory.Exists(SkillPathConfig.RecycleBin))
        {
            // 确认对话框，防止误触
            if (EditorUtility.DisplayDialog(
                "清空回收站",
                "确定要永久删除回收站中的所有技能吗？此操作不可撤销。",
                "确定",
                "取消"))
            {
                // 删除回收站文件夹
                System.IO.Directory.Delete(recycleBinPath, true);
                // 删除对应的 .meta 文件
                string metaPath = recycleBinPath + ".meta";
                if (System.IO.File.Exists(metaPath))
                    System.IO.File.Delete(metaPath);

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
        if (selectedSkills.Count == 0) return;

        var skillsToDelete = new List<SkillSO>(selectedSkills);

        // 确保回收站文件夹存在
        if (!AssetDatabase.IsValidFolder(recycleBinPath))
        {
            string parent = System.IO.Path.GetDirectoryName(recycleBinPath);
            string folder = System.IO.Path.GetFileName(recycleBinPath);
            AssetDatabase.CreateFolder(parent, folder);
        }

        // 记录本次操作的所有撤销信息
        var actions = new List<UndoStack.UndoAction>();

        foreach (SkillSO skillToDelete in skillsToDelete)
        {
            // 移动 .asset 文件到回收站
            string currentPath = AssetDatabase.GetAssetPath(skillToDelete);
            string fileName = System.IO.Path.GetFileName(currentPath);
            string recyclePath = recycleBinPath + "/" + fileName;

            // 处理重名
            int counter = 1;
            while (System.IO.File.Exists(recyclePath))
            {
                string nameNoExt = System.IO.Path.GetFileNameWithoutExtension(fileName);
                recyclePath = recycleBinPath + "/" + nameNoExt + " " + counter + ".asset";
                counter++;
            }

            AssetDatabase.MoveAsset(currentPath, recyclePath);

            var undoAction = new UndoAction
            {
                type = UndoActionType.Delete,
                skill = skillToDelete,
                originalPath = currentPath,
                recyclePath = recyclePath,  // recyclePath 是上面计算好的实际路径
            };

            // 移动关联 Lua 文件
            if (!string.IsNullOrEmpty(skillToDelete.filePath))
            {
                string luaCurrentPath =skillToDelete.filePath.Replace("\\", "/");
                if (File.Exists(luaCurrentPath))
                {
                    string luaFileName = Path.GetFileName(luaCurrentPath);
                    string luaRecyclePath =recycleBinPath + "/" + luaFileName;
                    string error = AssetDatabase.MoveAsset( luaCurrentPath,luaRecyclePath);
                    if (!string.IsNullOrEmpty(error))
                    {
                        Debug.LogError("移动Lua失败：" + error);
                    }
                    else
                    {
                        undoAction.luaOriginalPath = luaCurrentPath;
                        undoAction.luaRecyclePath =luaRecyclePath;
                    }
                }
            
        }
            // 记录本次操作，用于撤销
            actions.Add(undoAction);
            // 从内存列表移除
            selectedSkills.Remove(skillToDelete);
            skills.Remove(skillToDelete);
            foldouts.Remove(skillToDelete);
        }

        // 把本次所有删除操作压入统一撤销栈
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
        if (!string.IsNullOrWhiteSpace(newSkillName))
        {
            int maxID = skills.Count > 0 ? skills.Max(s => s.skillID) : 1000;

            SkillSO newSkill = SkillRepository.Create(newSkillName.Trim(), maxID + 1);

            skills.Add(newSkill);
            LuaExportService.Export(new List<SkillSO>() { newSkill });

            newSkillName = "";
            GUI.FocusControl(null);

            var action = new UndoAction
            {
                type = UndoActionType.Create,
                skill = newSkill,
                luaOriginalPath = newSkill.filePath
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
        string recyclePath =AssetDatabase.GetAssetPath(recycleSkill);
        string fileName =Path.GetFileName(recyclePath);
        string targetPath =SkillPathConfig.SkillFolder + "/" + fileName;

        int counter = 1;

        while (File.Exists(targetPath))
        {
            string name =Path.GetFileNameWithoutExtension(fileName);
            targetPath = SkillPathConfig.SkillFolder + "/" +name + " " +counter + ".asset";
            counter++;
        }

        AssetDatabase.MoveAsset(recyclePath,targetPath);

        var action = new UndoStack.UndoAction
        {
            type = UndoStack.UndoActionType.Restore,

            // 注意方向
            recyclePath = recyclePath,
            originalPath = targetPath,
        };


        RestoreLuaFile(recycleSkill,ref action);

        unifiedUndoStack.Record(new List<UndoStack.UndoAction>{action});
        AssetDatabase.Refresh();
        LoadSkillData();
        Repaint();
    }
    private void RestoreLuaFile(SkillSO skill, ref UndoStack.UndoAction action)
    {
        if (string.IsNullOrEmpty(skill.filePath))
            return;
        string luaName = Path.GetFileName(skill.filePath);
        string recycleLua =Path.Combine(SkillPathConfig.RecycleBin,luaName).Replace("\\", "/");
        if (!File.Exists(recycleLua))
        {
            Debug.LogWarning("找不到回收站Lua:" + recycleLua);
            return;
        }
        string targetLua =skill.filePath.Replace("\\", "/");
        string error = AssetDatabase.MoveAsset( recycleLua, targetLua);
        action.luaRecyclePath = recycleLua;
        action.luaOriginalPath = targetLua;
        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogError( "恢复Lua失败:" + error);
        }
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
            if (tagProp != null) EditorGUILayout.PropertyField(tagProp);
            EditorGUILayout.PropertyField(nameProp);
            EditorGUILayout.PropertyField(cooldownProp);

            //lua脚本路径
            
            EditorGUILayout.LabelField(
                "Lua脚本",
                string.IsNullOrEmpty(skill.filePath)
                    ? "未绑定"
                    : skill.filePath);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("绑定Lua脚本"))
            {
                BindLuaScript(skill);
            }
            if (GUILayout.Button("定位", GUILayout.Width(50)))
            {
                string luaPath = skill.filePath;
                if (System.IO.File.Exists(luaPath))
                {
                    // 在系统文件管理器中高亮对应的 Lua 文件
                    EditorUtility.RevealInFinder(luaPath);
                }
                else
                {
                    Debug.LogWarning($"Lua 脚本不存在：{luaPath}");
                }
            }
            EditorGUILayout.EndHorizontal();
            //buffSO引用
            if (buffProp != null)
            {
                EditorGUILayout.PropertyField(buffProp);

                // 只有选了 Buff 才显示目标选择
                if (buffProp.objectReferenceValue != null && targetProp != null)
                    EditorGUILayout.PropertyField(targetProp);
            }

            if (EditorGUI.EndChangeCheck())
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(skill);
            }

          
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    private void BindLuaScript(SkillSO skill)
    {
        string absolutePath =
            EditorUtility.OpenFilePanel(
                "选择Lua脚本",
                Application.dataPath,
                "lua");


        if (string.IsNullOrEmpty(absolutePath))
            return;


        string assetPath =
            "Assets" +
            absolutePath
            .Replace(Application.dataPath, "")
            .Replace("\\", "/");

        skill.filePath = assetPath;

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