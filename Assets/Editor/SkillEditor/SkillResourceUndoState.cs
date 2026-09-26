using System.Collections.Generic;
using UnityEngine;

// 只有这个对象进入原生撤销记录，实际命令不随它一起回退。
public class SkillResourceUndoState : ScriptableObject
{
    public List<string> operationIds = new();
}

