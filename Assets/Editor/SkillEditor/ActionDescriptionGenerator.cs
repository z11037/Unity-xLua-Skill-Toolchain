using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// 记录一条标准句覆盖的动作范围，保留组合描述的顺序与次数。
public sealed class DescriptionSegment
{
    public int StartIndex { get; }
    public int Count { get; }
    public string Text { get; }

    public DescriptionSegment(int startIndex, int count, string text)
    {
        StartIndex = startIndex;
        Count = count;
        Text = text;
    }
}

// 只将结构化动作转换为说明文本，不保存数据，也不解析或修改 Lua。
public static class ActionDescriptionGenerator
{
    public static string Generate(IReadOnlyList<SkillActionConfig> actions)
    {
        List<DescriptionSegment> segments = GenerateSegments(actions);
        var descriptions = new List<string>();
        foreach (DescriptionSegment segment in segments)
        {
            descriptions.Add(segment.Text);
        }
        return string.Join("\n", descriptions);
    }

    // 只合并同目标、同类型的相邻动作；保留实际执行次数和数值顺序。
    public static List<DescriptionSegment> GenerateSegments(IReadOnlyList<SkillActionConfig> actions)
    {
        if (!SkillActionValidator.Validate(actions, out string error))
        {
            throw new ArgumentException(error, nameof(actions));
        }

        var segments = new List<DescriptionSegment>();
        int start = 0;
        while (start < actions.Count)
        {
            SkillActionConfig action = actions[start];
            int end = start + 1;
            while (end < actions.Count && actions[end].type == action.type && actions[end].target == action.target)
            {
                end++;
            }
            int count = end - start;
            string target = action.target == SkillActionTarget.Self ? "自身" : "敌方";
            string text;
            switch (action.type)
            {
                case SkillActionType.Damage:
                    text = BuildDamageText(actions, start, count, target);
                    break;
                case SkillActionType.Buff:
                    text = BuildBuffText(actions, start, count, target);
                    break;
                default:
                    throw new ArgumentException($"动作 {start + 1} 的类型不支持生成描述。", nameof(actions));
            }
            segments.Add(new DescriptionSegment(start, count, text));
            start = end;
        }
        return segments;
    }

    private static string BuildDamageText(IReadOnlyList<SkillActionConfig> actions, int start, int count, string target)
    {
        int damage = actions[start].damage;
        string value = damage.ToString(CultureInfo.InvariantCulture);
        if (count == 1)
        {
            return $"对{target}造成{value}点伤害。";
        }

        bool equal = true;
        for (int i = start + 1; i < start + count; i++)
        {
            if (actions[i].damage != damage)
            {
                equal = false;
                break;
            }
        }
        if (equal)
        {
            return $"对{target}造成{count.ToString(CultureInfo.InvariantCulture)}次伤害，每次{value}点。";
        }

        var text = new StringBuilder($"对{target}依次造成");
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                text.Append(i == count - 1 ? "和" : "、");
            }
            text.Append(actions[start + i].damage.ToString(CultureInfo.InvariantCulture));
            text.Append("点");
        }
        text.Append("伤害。");
        return text.ToString();
    }

    private static string BuildBuffText(IReadOnlyList<SkillActionConfig> actions, int start, int count, string target)
    {
        var text = new StringBuilder($"对{target}");
        for (int i = 0; i < count; i++)
        {
            SkillActionConfig action = actions[start + i];
            if (string.IsNullOrWhiteSpace(action.buff.buffName))
            {
                throw new ArgumentException($"动作 {start + i + 1} 引用的 Buff（ID {action.buff.buffID}）缺少名称，请先填写 Buff 名称后生成描述。", nameof(actions));
            }
            if (i > 0)
            {
                text.Append("；随后");
            }
            text.Append($"施加{action.buff.buffName}效果，持续{action.buff.duration.ToString(CultureInfo.InvariantCulture)}秒");
        }
        // 多次施加按原顺序表达，不推断 Buff 叠层或累计持续时间。
        text.Append("。");
        return text.ToString();
    }
}
