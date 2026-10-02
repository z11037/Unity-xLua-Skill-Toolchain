using UnityEngine;

public enum SkillTag
{
    Attack,
    Buff,
    Control,
    Heal
}

[CreateAssetMenu()]
public class SkillSO : ScriptableObject
{
    [HideInInspector] public UnityEngine.Object[] luaResources = new UnityEngine.Object[0];
    public System.Collections.Generic.List<SkillActionConfig> actions = new System.Collections.Generic.List<SkillActionConfig>();
    public int skillID;
    public string skillName;
    // 编辑器直接绑定 Lua 资源；保留路径以兼容现有运行时加载及旧配置。
    public TextAsset luaScript;
    public string filePath;
    public float cooldown;
    public SkillTag tag = SkillTag.Attack;  // 改为枚举，默认 Attack
#if UNITY_EDITOR
    private void OnValidate()
    {
        SyncLuaPath();
    }

    public void SyncLuaPath()
    {
        if (luaScript == null)
        {
            return;
        }

        string path = UnityEditor.AssetDatabase.GetAssetPath(luaScript);
        if (!path.StartsWith("Assets/", System.StringComparison.Ordinal) || !path.EndsWith(".lua", System.StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning("技能只能引用 Assets 目录中的 .lua 文件。", this);
            luaScript = null;
            return;
        }

        // Lua 暂存回收站时，保留原始路径以兼容恢复流程。
        if (!path.StartsWith("Assets/ScriptableObjects/SkillSO/RecycleBin/", System.StringComparison.Ordinal))
        {
            filePath = path;
        }
    }
#endif
}