using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace SkillGraphEditor
{
    public sealed class GraphStore
    {
        private readonly string directory;

        public GraphStore(string directory)
        {
            this.directory = directory;
        }

        public string PathFor(string luaGuid)
        {
            if (!Guid.TryParseExact(luaGuid, "N", out _))
            {
                throw new ArgumentException("Lua GUID 无效。");
            }
            return Path.Combine(directory, luaGuid.ToLowerInvariant() + ".json");
        }

        public GraphSession Load(string luaGuid)
        {
            string path = PathFor(luaGuid);
            if (!File.Exists(path))
            {
                return new GraphSession(GraphData.Create(luaGuid), null);
            }
            string json = File.ReadAllText(path, Encoding.UTF8);
            // Parse 可在内存升级旧图；原始磁盘文本仍用于后续保存时检测外部修改。
            GraphData data = GraphSession.Parse(json);
            if (data.luaGuid != luaGuid)
            {
                throw new InvalidOperationException("Graph 中的 Lua GUID 与文件归属不一致。");
            }
            return new GraphSession(data, json);
        }

        public void Save(GraphSession session)
        {
            session.Data.ValidateStructure();
            string path = PathFor(session.Data.luaGuid);
            string current = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
            if (current != session.DiskJson)
            {
                throw new IOException("Graph 文件已在外部改变，请先备份当前编辑，再重新打开；本次未覆盖文件。");
            }
            string json = session.Snapshot();
            Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else
                {
                    File.Move(temporary, path);
                }
                session.MarkSaved(json);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
    }
}
