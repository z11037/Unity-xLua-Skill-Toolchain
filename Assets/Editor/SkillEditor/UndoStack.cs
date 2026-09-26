using System;
using System.Collections.Generic;
using UnityEngine;

// 兼容 Repository 的操作记录入口，时间线交给 Unity 管理。
public class UndoStack
{
    public enum UndoActionType { Create, Delete, Restore }

    [Serializable]
    public struct UndoAction
    {
        public UndoActionType type;
        public ScriptableObject skill;
        public string originalPath;
        public string recyclePath;
        public string luaOriginalPath;
        public string luaRecyclePath;
    }

    public void Record(List<UndoAction> actions)
    {
        SkillResourceUndoJournal.instance.Record(actions);
    }
}
