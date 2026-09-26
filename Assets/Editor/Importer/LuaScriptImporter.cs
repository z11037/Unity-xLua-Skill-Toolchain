using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

// 保留 .lua 扩展名，将脚本导入为可被 SkillSO 引用的文本资源。
[ScriptedImporter(1, "lua")]
public sealed class LuaScriptImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext context)
    {
        TextAsset script = new TextAsset(File.ReadAllText(context.assetPath));
        context.AddObjectToAsset("lua", script);
        context.SetMainObject(script);
    }
}
