using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SkillGraphEditor
{
    public sealed class SkillGraphWindow : EditorWindow
    {
        [SerializeField] private string workingJson;
        [SerializeField] private string savedJson;
        [SerializeField] private string diskJson;
        [SerializeField] private bool hasSavedBaseline;
        [SerializeField] private bool hasDiskBaseline;
        [SerializeField] private Vector2 scroll;
        private GraphSession session;
        private GraphStore store;
        [SerializeField] private SkillSO contextSkill;
        private string selectedId;
        private GraphEdge selectedEdge;
        private bool canvasHasFocus;
        private string connectingFrom;
        private string connectingFromPort;
        private string dragBefore;
        private string message;
        private List<GraphIssue> validationIssues = new List<GraphIssue>();
        private Vector2 inspectorScroll;
        private bool exportQueued;
        private const float NodeWidth = 180;
        private const float NodeHeight = 100;

        public static void Open(SkillSO skill)
        {
            if (skill == null || skill.luaScript == null)
            {
                EditorUtility.DisplayDialog("无法打开 Graph", "请先在技能编辑器中绑定 Lua 文件。", "确定");
                return;
            }
            var window = GetWindow<SkillGraphWindow>("技能 Graph");
            window.OpenLua(skill);
            window.Show();
        }

        private void OnEnable()
        {
            minSize = new Vector2(800, 480);
            store = new GraphStore(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "SkillGraphs"));
            saveChangesMessage = "Graph 有未保存的修改，是否保存？";
            if (!string.IsNullOrEmpty(workingJson))
            {
                try
                {
                    session = new GraphSession(GraphSession.Parse(workingJson), hasDiskBaseline ? diskJson : null);
                    session.RestoreBaseline(hasSavedBaseline ? savedJson : null, hasDiskBaseline ? diskJson : null);
                    SyncState();
                }
                catch (Exception exception)
                {
                    message = exception.Message;
                }
            }
        }

        private void SyncState()
        {
            if (session == null)
            {
                return;
            }
            workingJson = session.Snapshot();
            savedJson = session.SavedJson;
            diskJson = session.DiskJson;
            hasSavedBaseline = savedJson != null;
            hasDiskBaseline = diskJson != null;
            hasUnsavedChanges = session.IsDirty;
            if (selectedId != null && session.Data.Find(selectedId) == null)
            {
                selectedId = null;
            }
            if (selectedEdge != null && FindSelectedEdge() == null)
            {
                selectedEdge = null;
            }
            RefreshValidation();
        }

        private bool SaveGraph()
        {
            try
            {
                FinishDrag();
                store.Save(session);
                SyncState();
                message = "已保存：" + store.PathFor(session.Data.luaGuid);
                return true;
            }
            catch (Exception exception)
            {
                message = exception.Message;
                ShowNotification(new GUIContent("保存失败，修改仍保留在窗口中"));
                return false;
            }
        }

        public override void SaveChanges()
        {
            if (session != null && SaveGraph())
            {
                base.SaveChanges();
            }
        }

        public override void DiscardChanges()
        {
            workingJson = null;
            session = null;
            base.DiscardChanges();
        }

        private bool MaySwitch()
        {
            FinishDrag();
            if (session == null || !session.IsDirty)
            {
                return true;
            }
            int choice = EditorUtility.DisplayDialogComplex("未保存的 Graph", "切换前如何处理当前修改？", "保存", "取消", "放弃");
            return choice == 2 || (choice == 0 && SaveGraph());
        }

        private void OpenLua(SkillSO skill)
        {
            TextAsset lua = skill.luaScript;
            if (lua == null)
            {
                return;
            }
            string path = AssetDatabase.GetAssetPath(lua);
            if (!path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            {
                message = "请选择项目中的 .lua 资源。";
                return;
            }
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (session != null && session.Data.luaGuid == guid)
            {
                contextSkill = skill;
                return;
            }
            if (!MaySwitch())
            {
                return;
            }
            try
            {
                // 读取失败时保留当前图，不把损坏的文件替换为空图。
                GraphSession loaded = store.Load(guid);
                session = loaded;
                contextSkill = skill;
                selectedId = null;
                selectedEdge = null;
                canvasHasFocus = false;
                ClearConnection();
                scroll = Vector2.zero;
                inspectorScroll = Vector2.zero;
                message = "点击输出，再点击另一节点的输入建立连接。";
                SyncState();
            }
            catch (Exception exception)
            {
                message = "打开失败：" + exception.Message;
            }
        }

        private void Change(Action<GraphData> change)
        {
            try
            {
                session.Edit(change);
                SyncState();
                message = null;
            }
            catch (Exception exception)
            {
                message = exception.Message;
            }
            Repaint();
        }

        private void OnGUI()
        {
            HandleDeleteKey();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(session == null))
                {
                    if (GUILayout.Button("保存", EditorStyles.toolbarButton))
                    {
                        SaveGraph();
                    }
                }
                using (new EditorGUI.DisabledScope(session == null || !session.CanUndo))
                {
                    if (GUILayout.Button("撤销", EditorStyles.toolbarButton))
                    {
                        FinishDrag();
                        session.Undo();
                        ClearConnection();
                        SyncState();
                        message = null;
                    }
                }
                using (new EditorGUI.DisabledScope(session == null || !session.CanRedo))
                {
                    if (GUILayout.Button("重做", EditorStyles.toolbarButton))
                    {
                        FinishDrag();
                        session.Redo();
                        ClearConnection();
                        SyncState();
                        message = null;
                    }
                }
                using (new EditorGUI.DisabledScope(session == null))
                {
                    if (GUILayout.Button("检查", EditorStyles.toolbarButton))
                    {
                        FinishDrag();
                        RefreshValidation();
                        message = validationIssues.Count == 0 ? "检查通过，可以导出。" : $"发现 {validationIssues.Count} 处问题，草稿仍可保存。";
                    }
                }
                GraphNode selectedNode = session == null ? null : session.Data.Find(selectedId);
                using (new EditorGUI.DisabledScope(FindSelectedEdge() == null && (selectedNode == null || selectedNode.kind == NodeKind.Entry)))
                {
                    if (GUILayout.Button("删除所选", EditorStyles.toolbarButton))
                    {
                        DeleteSelection();
                    }
                }
            }
            if (session != null)
            {
                string luaPath = AssetDatabase.GUIDToAssetPath(session.Data.luaGuid);
                EditorGUILayout.LabelField("当前技能", contextSkill == null ? "原技能资源已失效" : $"{contextSkill.skillName}（ID {contextSkill.skillID}）");
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Lua 文件", luaPath);
                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(luaPath)))
                    {
                        if (GUILayout.Button("打开 Lua", GUILayout.Width(90)))
                        {
                            QueueOpenLuaFile();
                        }
                    }
                }
                int references = SkillRepository.GetLuaReferenceCount(session.Data.luaGuid);
                if (references > 1)
                {
                    EditorGUILayout.HelpBox($"此 Lua 被 {references} 个技能共享，图和导出脚本也共享。", MessageType.Warning);
                }
                using (new EditorGUI.DisabledScope(exportQueued || contextSkill == null || SkillRepository.GetLuaGuid(contextSkill) != session.Data.luaGuid))
                {
                    if (GUILayout.Button("导出当前 Graph 为 Lua 模板"))
                    {
                        QueueExportGraph();
                    }
                }
            }
            if (!string.IsNullOrEmpty(message))
            {
                EditorGUILayout.HelpBox(message, MessageType.Info);
            }
            if (session == null)
            {
                EditorGUILayout.HelpBox("请从技能编辑器的“打开技能 Graph”进入。", MessageType.Info);
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (connectingFrom != null && GUILayout.Button("取消连线", GUILayout.Width(90)))
                {
                    ClearConnection();
                }
                GUILayout.Label(session.IsDirty ? "未保存" : "已保存");
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawCanvas();
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(230)))
                {
                    inspectorScroll = GUILayout.BeginScrollView(inspectorScroll, GUILayout.ExpandHeight(true));
                    DrawInspector();
                    DrawValidation();
                    GUILayout.EndScrollView();
                }
            }
            DrawPalette();
            if (Event.current.rawType == EventType.MouseUp || Event.current.type == EventType.Ignore)
            {
                FinishDrag();
            }
        }

        private void DrawCanvas()
        {
            Rect viewport = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            Event dragEvent = Event.current;
            bool pointerInCanvas = viewport.Contains(dragEvent.mousePosition);
            if (dragEvent.rawType == EventType.MouseDown || dragEvent.rawType == EventType.ContextClick)
            {
                canvasHasFocus = pointerInCanvas;
                if (canvasHasFocus && (dragEvent.type == EventType.MouseDown || dragEvent.type == EventType.ContextClick))
                {
                    GUI.FocusControl(null);
                    GUIUtility.keyboardControl = 0;
                }
            }
            if (viewport.Contains(dragEvent.mousePosition) && (dragEvent.type == EventType.DragUpdated || dragEvent.type == EventType.DragPerform)
                && DragAndDrop.GetGenericData("SkillGraphNode") is NodeKind kind)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (dragEvent.type == EventType.DragPerform)
                {
                    Vector2 location = dragEvent.mousePosition - viewport.position + scroll;
                    DragAndDrop.AcceptDrag();
                    Change(graph => SelectNode(graph.Add(kind, location).id));
                    DragAndDrop.SetGenericData("SkillGraphNode", null);
                }
                dragEvent.Use();
            }
            float width = 2000;
            float height = 1600;
            foreach (GraphNode node in session.Data.nodes)
            {
                width = Mathf.Max(width, node.position.x + 400);
                height = Mathf.Max(height, node.position.y + 300);
            }
            scroll = GUI.BeginScrollView(viewport, scroll, new Rect(0, 0, width, height));
            GraphNode pointedNode = pointerInCanvas ? session.Data.nodes.FindLast(node => new Rect(node.position, new Vector2(NodeWidth, NodeHeight)).Contains(Event.current.mousePosition)) : null;
            if (pointerInCanvas && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                selectedId = null;
                selectedEdge = null;
                if (pointedNode != null)
                {
                    SelectNode(pointedNode.id);
                    if (new Rect(pointedNode.position, new Vector2(NodeWidth, 22)).Contains(Event.current.mousePosition))
                    {
                        dragBefore = session.Snapshot();
                    }
                }
            }
            Handles.BeginGUI();
            Color previousHandleColor = Handles.color;
            foreach (GraphEdge edge in session.Data.edges)
            {
                GraphNode from = session.Data.Find(edge.from);
                GraphNode to = session.Data.Find(edge.to);
                Vector3 start = PortPosition(from, edge.fromPort);
                Vector3 end = PortPosition(to, edge.toPort);
                Vector3 first = start + Vector3.right * 60;
                Vector3 second = end + Vector3.left * 60;
                bool selected = selectedEdge != null && SameEdge(edge, selectedEdge);
                Color color = selected ? Color.yellow : Color.cyan;
                Handles.DrawBezier(start, end, first, second, color, null, selected ? 4 : 3);
                Handles.color = color;
                Handles.DrawAAConvexPolygon(end, end + new Vector3(-10, -5), end + new Vector3(-10, 5));
                bool pointerOnEdge = pointerInCanvas && pointedNode == null && HandleUtility.DistancePointBezier(Event.current.mousePosition, start, end, first, second) < 10;
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && pointerOnEdge)
                {
                    SelectEdge(edge);
                    Event.current.Use();
                    Repaint();
                }
                if (Event.current.type == EventType.ContextClick && pointerOnEdge)
                {
                    SelectEdge(edge);
                    GraphEdge requestedEdge = selectedEdge;
                    GraphSession requestedSession = session;
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("断开连接"), false, () =>
                    {
                        if (this != null && session == requestedSession)
                        {
                            SelectEdge(requestedEdge);
                            DeleteSelection();
                        }
                    });
                    menu.ShowAsContext();
                    Event.current.Use();
                    Repaint();
                }
            }
            Handles.color = previousHandleColor;
            Handles.EndGUI();
            BeginWindows();
            for (int i = 0; i < session.Data.nodes.Count; i++)
            {
                GraphNode node = session.Data.nodes[i];
                Color previousBackground = GUI.backgroundColor;
                if (node.id == selectedId)
                {
                    GUI.backgroundColor = new Color(0.6f, 0.9f, 1f);
                }
                Rect rect = GUI.Window(i, new Rect(node.position, new Vector2(NodeWidth, NodeHeight)), DrawNode, NodeTitle(node));
                GUI.backgroundColor = previousBackground;
                node.position = new Vector2(Mathf.Max(0, rect.x), Mathf.Max(0, rect.y));
            }
            EndWindows();
            GUI.EndScrollView();
        }

        private void DrawNode(int index)
        {
            GraphNode node = session.Data.nodes[index];
            bool pointerInNode = new Rect(0, 0, NodeWidth, NodeHeight).Contains(Event.current.mousePosition);
            if (canvasHasFocus && Event.current.type == EventType.MouseDown && pointerInNode)
            {
                SelectNode(node.id);
            }
            if (canvasHasFocus && Event.current.type == EventType.ContextClick && pointerInNode)
            {
                SelectNode(node.id);
                string requestedNodeId = node.id;
                GraphSession requestedSession = session;
                var menu = new GenericMenu();
                if (node.kind == NodeKind.Entry)
                {
                    menu.AddDisabledItem(new GUIContent("删除节点"));
                }
                else
                {
                    menu.AddItem(new GUIContent("删除节点"), false, () =>
                    {
                        if (this != null && session == requestedSession)
                        {
                            SelectNode(requestedNodeId);
                            DeleteSelectedNode();
                        }
                    });
                }
                menu.ShowAsContext();
                Event.current.Use();
                Repaint();
            }
            GUI.Label(new Rect(10, 23, 165, 20), node.kind == NodeKind.Damage ? "固定伤害：" + node.damage : node.kind == NodeKind.ApplyBuff ? "引用 BuffSO" : "技能编排入口");
            int inputs = 0;
            int outputs = 0;
            foreach (GraphPortDefinition port in GraphPorts.For(node.kind))
            {
                int indexInDirection = port.Direction == PortDirection.Input ? inputs++ : outputs++;
                string tooltip = port.Direction == PortDirection.Input ? "执行流程输入" : "执行流程输出";
                if (!GUI.Button(PortRect(port, indexInDirection), new GUIContent("▶", tooltip)))
                {
                    continue;
                }
                SelectNode(node.id);
                if (port.Direction == PortDirection.Output)
                {
                    connectingFrom = node.id;
                    connectingFromPort = port.Id;
                }
                else if (connectingFrom != null)
                {
                    List<GraphIssue> issues = GraphValidator.ValidateConnection(session.Data, connectingFrom, connectingFromPort, node.id, port.Id);
                    if (issues.Count == 0)
                    {
                        string source = connectingFrom;
                        string sourcePort = connectingFromPort;
                        Change(graph => graph.Connect(source, sourcePort, node.id, port.Id, out _));
                        ClearConnection();
                    }
                    else
                    {
                        message = new GraphValidationException(issues).Message;
                    }
                }
            }
            GUI.DragWindow(new Rect(0, 0, NodeWidth, 22));
        }

        private static Rect PortRect(GraphPortDefinition port, int index)
        {
            return new Rect(port.Direction == PortDirection.Input ? 5 : 125, 55 + index * 26, 50, 25);
        }

        private static Vector3 PortPosition(GraphNode node, string portId)
        {
            int inputs = 0;
            int outputs = 0;
            foreach (GraphPortDefinition port in GraphPorts.For(node.kind))
            {
                int index = port.Direction == PortDirection.Input ? inputs++ : outputs++;
                if (port.Id == portId)
                {
                    return node.position + new Vector2(port.Direction == PortDirection.Input ? 10 : NodeWidth - 10, PortRect(port, index).center.y);
                }
            }
            return node.position;
        }

        private void ClearConnection()
        {
            connectingFrom = null;
            connectingFromPort = null;
        }

        private void SelectNode(string id)
        {
            selectedId = id;
            selectedEdge = null;
            canvasHasFocus = true;
        }

        private void SelectEdge(GraphEdge edge)
        {
            selectedId = null;
            selectedEdge = new GraphEdge { from = edge.from, fromPort = edge.fromPort, to = edge.to, toPort = edge.toPort };
            canvasHasFocus = true;
            ClearConnection();
        }

        private static bool SameEdge(GraphEdge first, GraphEdge second)
        {
            return first.from == second.from && first.fromPort == second.fromPort && first.to == second.to && first.toPort == second.toPort;
        }

        private GraphEdge FindSelectedEdge()
        {
            return session == null || selectedEdge == null ? null : session.Data.edges.Find(edge => SameEdge(edge, selectedEdge));
        }

        private void HandleDeleteKey()
        {
            Event current = Event.current;
            // 参数输入框保留 Delete 的编辑行为，只有画布选择可以触发删除。
            if (session != null && canvasHasFocus && !EditorGUIUtility.editingTextField && GUIUtility.keyboardControl == 0
                && current.type == EventType.KeyDown && current.keyCode == KeyCode.Delete)
            {
                DeleteSelection();
                current.Use();
            }
        }

        private void DeleteSelection()
        {
            GraphEdge edge = FindSelectedEdge();
            if (edge == null)
            {
                DeleteSelectedNode();
                return;
            }
            FinishDrag();
            Change(graph => graph.edges.RemoveAll(item => SameEdge(item, edge)));
            ClearConnection();
        }

        private void DeleteSelectedNode()
        {
            GraphNode node = session == null ? null : session.Data.Find(selectedId);
            if (node == null || node.kind == NodeKind.Entry)
            {
                return;
            }
            FinishDrag();
            Change(graph => graph.Remove(node.id));
            if (session.Data.Find(node.id) == null)
            {
                selectedId = null;
                ClearConnection();
            }
        }

        private void RefreshValidation()
        {
            validationIssues = session == null ? new List<GraphIssue>() : GraphExportValidator.Validate(session.Data);
        }

        private void DrawValidation()
        {
            GUILayout.Space(8);
            GUILayout.Label(validationIssues.Count == 0 ? "导出检查：通过" : $"导出检查：{validationIssues.Count} 处问题", EditorStyles.boldLabel);
            if (validationIssues.Count == 0)
            {
                return;
            }
            foreach (GraphIssue issue in validationIssues)
            {
                GraphNode node = session.Data.Find(issue.NodeId);
                string location = node == null ? "图结构" : NodeTitle(node);
                if (issue.Field == "damage")
                {
                    location += " · 固定伤害";
                }
                else if (issue.Field == "buffGuid")
                {
                    location += " · Buff 引用";
                }
                else if (!string.IsNullOrEmpty(issue.PortId))
                {
                    location += issue.PortId == GraphPorts.FlowInput ? " · 输入端口" : " · 输出端口";
                }
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label(new GUIContent(location, issue.Code), EditorStyles.wordWrappedLabel);
                    EditorGUILayout.HelpBox(issue.Message, MessageType.Error);
                    if (node != null && GUILayout.Button("定位节点"))
                    {
                        SelectNode(node.id);
                        GUI.FocusControl(null);
                        GUIUtility.keyboardControl = 0;
                        scroll = new Vector2(Mathf.Max(0, node.position.x - 60), Mathf.Max(0, node.position.y - 60));
                        inspectorScroll = Vector2.zero;
                        Repaint();
                    }
                }
            }
        }

        private void DrawInspector()
        {
            GraphEdge edge = FindSelectedEdge();
            if (edge != null)
            {
                GUILayout.Label("Flow 连线", EditorStyles.boldLabel);
                GUILayout.Label(NodeTitle(session.Data.Find(edge.from)) + " → " + NodeTitle(session.Data.Find(edge.to)), EditorStyles.wordWrappedLabel);
                if (GUILayout.Button("断开连接"))
                {
                    DeleteSelection();
                }
                return;
            }
            GraphNode node = session.Data.Find(selectedId);
            if (node == null)
            {
                GUILayout.Label("点击节点编辑参数");
                return;
            }
            GUILayout.Label(NodeTitle(node), EditorStyles.boldLabel);
            if (node.kind != NodeKind.Entry)
            {
                bool participates = IsConnectedToEntry(node.id);
                TargetKind target = (TargetKind)EditorGUILayout.EnumPopup("目标", node.target);
                if (target != node.target)
                {
                    Change(graph => graph.Find(node.id).target = target);
                }
                if (node.kind == NodeKind.Damage)
                {
                    int damage = EditorGUILayout.DelayedIntField("固定伤害", node.damage);
                    if (damage != node.damage)
                    {
                        Change(graph => graph.Find(node.id).damage = damage);
                    }
                    if (participates && node.damage <= 0)
                    {
                        EditorGUILayout.HelpBox("伤害值尚未有效填写，可保存为草稿。", MessageType.Warning);
                    }
                }
                else
                {
                    BuffSO buff = AssetDatabase.LoadAssetAtPath<BuffSO>(AssetDatabase.GUIDToAssetPath(node.buffGuid ?? ""));
                    EditorGUI.BeginChangeCheck();
                    BuffSO updated = (BuffSO)EditorGUILayout.ObjectField("Buff", buff, typeof(BuffSO), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Change(graph => graph.Find(node.id).buffGuid = updated == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(updated)));
                    }
                    if (participates && buff == null)
                    {
                        EditorGUILayout.HelpBox("Buff 未指定或引用失效，可保存为草稿。", MessageType.Warning);
                    }
                }
                if (!participates)
                {
                    EditorGUILayout.HelpBox("该节点未接入入口流程，不参与导出和参数检查。", MessageType.Info);
                }
            }
            if (GUILayout.Button("断开此节点的连接"))
            {
                Change(graph => graph.edges.RemoveAll(edge => edge.from == node.id || edge.to == node.id));
                ClearConnection();
            }
        }

        private void DrawPalette()
        {
            GUILayout.Label("节点面板：将卡片拖到画布", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope(GUILayout.Height(65)))
            {
                DrawCard(NodeKind.Damage, "伤害\n▶ 固定数值");
                DrawCard(NodeKind.ApplyBuff, "施加 Buff\n▶ 引用配置");
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawCard(NodeKind kind, string label)
        {
            Rect rect = GUILayoutUtility.GetRect(150, 60, GUILayout.Width(150));
            GUI.Box(rect, label, EditorStyles.helpBox);
            if (Event.current.type == EventType.MouseDrag && rect.Contains(Event.current.mousePosition) && Event.current.button == 0)
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = new UnityEngine.Object[0];
                DragAndDrop.SetGenericData("SkillGraphNode", kind);
                DragAndDrop.StartDrag(Label(kind));
                Event.current.Use();
            }
        }

        private void QueueExportGraph()
        {
            if (exportQueued)
            {
                return;
            }
            exportQueued = true;
            GraphSession requestedSession = session;
            SkillSO requestedSkill = contextSkill;
            // 弹窗和资源导入离开 OnGUI 后执行，避免重入尚未结束的绘制过程。
            EditorApplication.delayCall += () =>
            {
                if (this == null)
                {
                    return;
                }
                try
                {
                    if (session != requestedSession || contextSkill != requestedSkill || requestedSkill == null
                        || SkillRepository.GetLuaGuid(requestedSkill) != requestedSession.Data.luaGuid)
                    {
                        message = "编辑对象已改变，本次导出已取消。";
                        return;
                    }
                    ExportGraph();
                }
                finally
                {
                    exportQueued = false;
                    if (this != null)
                    {
                        Repaint();
                    }
                }
            };
        }

        private void QueueOpenLuaFile()
        {
            GraphSession requestedSession = session;
            string guid = session.Data.luaGuid;
            // 打开外部编辑器离开 OnGUI 执行，避免打断当前绘制。
            EditorApplication.delayCall += () =>
            {
                if (this == null || session != requestedSession || session.Data.luaGuid != guid)
                {
                    return;
                }
                try
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    TextAsset lua = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                    if (lua == null)
                    {
                        message = "Lua 资源已失效，无法打开。";
                    }
                    else if (!AssetDatabase.OpenAsset(lua))
                    {
                        message = "无法打开 Lua 文件，请检查 Unity 的脚本编辑器设置。";
                    }
                }
                catch (Exception exception)
                {
                    message = "打开 Lua 失败：" + exception.Message;
                }
                Repaint();
            };
        }

        private void ExportGraph()
        {
            try
            {
                FinishDrag();
                string body = GraphTemplateExport.Generate(session.Data, out UnityEngine.Object[] resources);
                if (!EditorUtility.DisplayDialog("导出 Lua 模板", "将覆盖当前图对应的 Lua 内容，手动修改会丢失。共用该 Lua 的技能会同时使用新模板。是否继续？", "导出", "取消"))
                {
                    return;
                }
                if (!SaveGraph())
                {
                    return;
                }
                LuaExportService.ExportSkill(contextSkill, body, "local api = CS.SkillActionApi", resources, true);
                AssetDatabase.SaveAssets();
                message = "Graph 与 Lua 模板已保存。";
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (GraphValidationException exception)
            {
                validationIssues = new List<GraphIssue>(exception.Issues);
                message = "导出已阻止，请点击检查列表定位问题；当前草稿仍可保存。";
            }
            catch (Exception exception)
            {
                message = "导出失败：" + exception.Message;
            }
        }

        private void FinishDrag()
        {
            if (dragBefore != null && session != null)
            {
                session.Commit(dragBefore);
                dragBefore = null;
                SyncState();
            }
        }

        private void OnLostFocus()
        {
            FinishDrag();
        }

        private void OnDisable()
        {
            FinishDrag();
        }

        private bool IsConnectedToEntry(string id)
        {
            return GraphValidator.ReachableFlow(session.Data).Contains(id);
        }

        private static string Label(NodeKind kind)
        {
            return kind == NodeKind.Entry ? "入口" : kind == NodeKind.Damage ? "伤害" : "施加 Buff";
        }

        private string NodeTitle(GraphNode node)
        {
            return Label(node.kind) + " · 节点 " + (session.Data.nodes.IndexOf(node) + 1);
        }
    }
}
