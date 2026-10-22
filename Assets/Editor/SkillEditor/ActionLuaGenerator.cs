using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SkillGraphEditor;
using UnityEditor;
using UnityEngine;

// 只读取模块数据并产出文本与资源表，不读写文件，也不依赖编辑器窗口。
public static class ActionLuaGenerator
{
    public static string Generate(IReadOnlyList<GraphNode> actions, out UnityEngine.Object[] resources, int firstResourceIndex = 0, string indentation = "    ", int firstActionNumber = 1)
    {
        GraphExportValidator.RequireValidActions(actions);
        if (firstResourceIndex < 0 || firstActionNumber < 1 || indentation == null)
        {
            throw new ArgumentException("动作生成的资源索引、编号或缩进无效。");
        }
        var references = new List<UnityEngine.Object>();
        var code = new StringBuilder();
        int resourceIndex = firstResourceIndex;
        for (int i = 0; i < actions.Count; i++)
        {
            GraphNode action = actions[i];
            string receiver = action.target == TargetKind.Self ? "attacker" : "target";
            string actionName = action.kind == NodeKind.Damage ? "Damage" : "Buff";
            code.AppendLine($"{indentation}-- 动作 {firstActionNumber + i}：{actionName}");
            string call = action.kind == NodeKind.Damage
                ? $"api.Damage(attacker, {receiver}, {action.damage.ToString(CultureInfo.InvariantCulture)})"
                : $"api.ApplyBuff(attacker, {receiver}, resources[{resourceIndex++}])";
            if (action.kind == NodeKind.ApplyBuff)
            {
                string path = AssetDatabase.GUIDToAssetPath(action.buffGuid);
                references.Add(AssetDatabase.LoadAssetAtPath<BuffSO>(path));
            }
            code.AppendLine($"{indentation}{call}");
        }
        resources = references.ToArray();
        return code.ToString();
    }
}
