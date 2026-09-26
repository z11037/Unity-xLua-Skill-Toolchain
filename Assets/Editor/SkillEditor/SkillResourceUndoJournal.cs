using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public class SkillResourceUndoJournal : ScriptableSingleton<SkillResourceUndoJournal>
{
    [Serializable]
    private class Move
    {
        public string guid;
        public string before;
        public string after;
    }

    [Serializable]
    private class Operation
    {
        public string id;
        public List<Move> moves = new();
    }

    [SerializeField] private SkillResourceUndoState state;
    [SerializeField] private List<Operation> operations = new();
    [SerializeField] private List<string> appliedIds = new();
    [SerializeField] private List<Move> pendingRollback = new();
    [SerializeField] private string failure = "";
    private bool executing;
    public static event Action Changed;
    public static string CachePath => SkillPathConfig.RecycleBin + "/UndoHistory";
    public string Failure => failure;
    public bool HasHistory => operations.Count > 0;

    static SkillResourceUndoJournal()
    {
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    private static void OnUndoRedo()
    {
        instance.Synchronize();
    }

    public bool CanOperate()
    {
        if (!string.IsNullOrEmpty(failure))
        {
            EditorUtility.DisplayDialog("资源撤销未完成", "请先解决路径冲突并重试，或通过原生重做返回原状态。\n" + failure, "确定");
            return false;
        }
        return !executing;
    }

    // 永久清空回收站前，仅清理本工具的代理记录，不清空全局撤销历史。
    public void ClearHistory()
    {
        if (state != null)
        {
            Undo.ClearUndo(state);
            state.operationIds.Clear();
        }
        operations.Clear();
        appliedIds.Clear();
        pendingRollback.Clear();
        failure = "";
    }

    public void Record(List<UndoStack.UndoAction> actions)
    {
        if (actions == null || actions.Count == 0)
        {
            return;
        }
        if (state == null)
        {
            state = ScriptableObject.CreateInstance<SkillResourceUndoState>();
            state.hideFlags = HideFlags.HideAndDontSave;
            state.name = "技能资源撤销状态";
        }
        Operation operation = new() { id = Guid.NewGuid().ToString("N") };
        foreach (var action in actions)
        {
            string current = AssetDatabase.GetAssetPath(action.skill);
            string before = action.originalPath;
            string after = action.recyclePath;
            if (action.type == UndoStack.UndoActionType.Create)
            {
                before = CachePath + "/" + operation.id + "_" + Path.GetFileName(current);
                after = current;
            }
            else if (action.type == UndoStack.UndoActionType.Restore)
            {
                before = action.recyclePath;
                after = action.originalPath;
            }
            operation.moves.Add(new Move { guid = AssetDatabase.AssetPathToGUID(current), before = before, after = after });
            if (!string.IsNullOrEmpty(action.luaRecyclePath))
            {
                bool restore = action.type == UndoStack.UndoActionType.Restore;
                string luaBefore = restore ? action.luaRecyclePath : action.luaOriginalPath;
                string luaAfter = restore ? action.luaOriginalPath : action.luaRecyclePath;
                operation.moves.Add(new Move { guid = AssetDatabase.AssetPathToGUID(luaAfter), before = luaBefore, after = luaAfter });
            }
        }
        // 隔离分组，避免与同一界面中的属性修改合并。
        Undo.FlushUndoRecordObjects();
        Undo.IncrementCurrentGroup();
        string label = actions[0].type == UndoStack.UndoActionType.Create ? "新建技能资源" : actions[0].type == UndoStack.UndoActionType.Delete ? "回收技能资源" : "恢复技能资源";
        Undo.SetCurrentGroupName(label);
        Undo.RegisterCompleteObjectUndo(state, label);
        operations.Add(operation);
        appliedIds.Add(operation.id);
        state.operationIds = new List<string>(appliedIds);
        EditorUtility.SetDirty(state);
        Undo.IncrementCurrentGroup();
    }

    public void Synchronize()
    {
        if (executing || state == null)
        {
            return;
        }
        executing = true;
        var completed = new List<Move>();
        try
        {
            // 上次补偿失败时先完成补偿，不能把部分移动误判为已同步。
            RollbackPending();
            int common = 0;
            while (common < appliedIds.Count && common < state.operationIds.Count && appliedIds[common] == state.operationIds[common])
            {
                common++;
            }
            for (int i = appliedIds.Count - 1; i >= common; i--)
            {
                Execute(appliedIds[i], false, completed);
            }
            for (int i = common; i < state.operationIds.Count; i++)
            {
                Execute(state.operationIds[i], true, completed);
            }
            appliedIds = new List<string>(state.operationIds);
            failure = "";
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            // 文件操作不是 Unity 的对象快照，失败时需要按逆序补偿。
            pendingRollback.AddRange(completed);
            try
            {
                RollbackPending();
            }
            catch (Exception rollbackException)
            {
                failure += "\n回滚未完成，请解决冲突后重试：" + rollbackException.Message;
            }
            Debug.LogError("资源撤销/重做失败：" + failure);
        }
        finally
        {
            executing = false;
            SkillRepository.InvalidateLuaReferenceCounts();
            Changed?.Invoke();
        }
    }

    private void RollbackPending()
    {
        while (pendingRollback.Count > 0)
        {
            Move move = pendingRollback[pendingRollback.Count - 1];
            if (AssetDatabase.GUIDToAssetPath(move.guid) != move.after)
            {
                throw new IOException("回滚资源位置已改变：" + move.after);
            }
            string error = AssetDatabase.MoveAsset(move.after, move.before);
            if (!string.IsNullOrEmpty(error))
            {
                throw new IOException(error);
            }
            pendingRollback.RemoveAt(pendingRollback.Count - 1);
        }
    }

    private void Execute(string id, bool forward, List<Move> completed)
    {
        Operation operation = operations.Find(item => item.id == id);
        if (operation == null)
        {
            throw new InvalidOperationException("找不到资源操作记录：" + id);
        }
        var moves = operation.moves;
        foreach (Move move in moves)
        {
            string from = forward ? move.before : move.after;
            string to = forward ? move.after : move.before;
            if (string.IsNullOrEmpty(move.guid) || AssetDatabase.GUIDToAssetPath(move.guid) != from)
            {
                throw new InvalidOperationException("资源已被移动或删除：" + from);
            }
            if (File.Exists(to) || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(to)))
            {
                throw new InvalidOperationException("目标路径已被占用：" + to);
            }
            if (to.StartsWith(SkillPathConfig.RecycleBin + "/", StringComparison.OrdinalIgnoreCase) && from.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            {
                SkillRepository.InvalidateLuaReferenceCounts();
                if (SkillRepository.GetLuaReferenceCount(move.guid) > 0)
                {
                    throw new InvalidOperationException("Lua 仍被有效技能引用，不能回收：" + from);
                }
            }
            EnsureFolder(Path.GetDirectoryName(to).Replace('\\', '/'));
            string error = AssetDatabase.MoveAsset(from, to);
            if (!string.IsNullOrEmpty(error))
            {
                throw new InvalidOperationException(error);
            }
            completed.Add(new Move { guid = move.guid, before = from, after = to });
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        string guid = AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        if (string.IsNullOrEmpty(guid))
        {
            throw new IOException("无法创建暂存目录：" + path);
        }
    }
}
