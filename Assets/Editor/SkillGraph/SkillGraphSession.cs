using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkillGraphEditor
{
    // 图编辑历史只属于当前会话，不使用 Unity Undo，也不写入图文件。
    public sealed class GraphSession
    {
        [Serializable]
        private sealed class GraphVersionHeader
        {
            // 缺少版本字段时保持 0，防止 GraphData 的默认版本掩盖损坏文件。
            public int version = 0;
        }

        public GraphData Data { get; private set; }
        public string SavedJson { get; private set; }
        public string DiskJson { get; private set; }
        private readonly Stack<string> undo = new Stack<string>();
        private readonly Stack<string> redo = new Stack<string>();
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;
        public bool IsDirty => Snapshot() != SavedJson;

        public GraphSession(GraphData data, string diskJson)
        {
            Data = data;
            DiskJson = diskJson;
            SavedJson = diskJson == null ? null : Snapshot();
        }

        public string Snapshot()
        {
            return JsonUtility.ToJson(Data, true);
        }

        public static GraphData Parse(string json)
        {
            GraphVersionHeader header = JsonUtility.FromJson<GraphVersionHeader>(json);
            if (header == null || (header.version != 1 && header.version != 2))
            {
                throw new InvalidOperationException("Graph JSON 缺少有效版本，或版本尚不支持。");
            }
            GraphData graph = JsonUtility.FromJson<GraphData>(json);
            if (graph == null)
            {
                throw new InvalidOperationException("Graph JSON 为空。");
            }
            if (header.version == 1)
            {
                MigrateLegacyGraph(graph);
            }
            else
            {
                graph.ValidateStructure();
            }
            return graph;
        }

        private static void MigrateLegacyGraph(GraphData graph)
        {
            if (graph.edges == null)
            {
                throw new InvalidOperationException("旧版 Graph 的连接列表无效。");
            }

            // 先用临时图检查旧节点和连接，全部有效后再修改实际图；读取不写回磁盘。
            var checkedGraph = new GraphData { version = 2, luaGuid = graph.luaGuid, nodes = graph.nodes };
            foreach (GraphEdge edge in graph.edges)
            {
                if (edge == null)
                {
                    throw new InvalidOperationException("旧版 Graph 存在空连接。");
                }
                checkedGraph.edges.Add(new GraphEdge { from = edge.from, fromPort = GraphPorts.FlowOutput, to = edge.to, toPort = GraphPorts.FlowInput });
            }
            checkedGraph.ValidateStructure();
            foreach (GraphEdge edge in graph.edges)
            {
                edge.fromPort = GraphPorts.FlowOutput;
                edge.toPort = GraphPorts.FlowInput;
            }
            graph.version = 2;
        }

        public void Edit(Action<GraphData> edit)
        {
            string before = Snapshot();
            try
            {
                edit(Data);
                Data.ValidateStructure();
                Commit(before);
            }
            catch
            {
                Data = Parse(before);
                throw;
            }
        }

        // 拖动过程只更新位置，松开时提交一次快照。
        public void Commit(string before)
        {
            if (before != Snapshot())
            {
                undo.Push(before);
                redo.Clear();
            }
        }

        public void Undo()
        {
            if (CanUndo)
            {
                redo.Push(Snapshot());
                Data = Parse(undo.Pop());
            }
        }

        public void Redo()
        {
            if (CanRedo)
            {
                undo.Push(Snapshot());
                Data = Parse(redo.Pop());
            }
        }

        public void MarkSaved(string diskJson)
        {
            SavedJson = Snapshot();
            DiskJson = diskJson;
        }

        public void RestoreBaseline(string savedJson, string diskJson)
        {
            // 程序集重载可能带回旧版保存点；规范化编辑基线，磁盘基线继续保留原文。
            SavedJson = savedJson == null ? null : JsonUtility.ToJson(Parse(savedJson), true);
            DiskJson = diskJson;
        }
    }
}
