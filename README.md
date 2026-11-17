# Unity 技能系统与制作工具

基于 **Unity、C# 和 xLua** 开发的个人游戏项目，覆盖技能配置、脚本生成和战斗执行。以「造成伤害并施加 Buff」为例，可以在 Unity 编辑器里连好两个节点，从同一份图生成 Lua 技能模板和标准化描述，再由角色在战斗中执行 Lua。

## 项目亮点

- **技能制作。** 简易 Graph 支持固定伤害、施加 Buff 和生命比例条件分支，提供 JSON 保存、图内撤销和导出前校验。Lua 与描述分别生成，复杂行为可继续手写 Lua。[Graph 编辑器](Assets/Editor/SkillGraph/SkillGraphWindow.cs) · [生成入口](Assets/Editor/SkillGraph/GraphTemplateExport.cs)
- **资源管理。** 技能使用 ScriptableObject 配置，通过资源 GUID 跟踪共享 Lua。回收一个技能时，仍被其他有效技能引用的 Lua 会保留。技能创建、回收和恢复接入 Unity Undo／Redo。[引用与回收](Assets/Editor/SkillEditor/SkillRepository.cs) · [资源撤销](Assets/Editor/SkillEditor/SkillResourceUndoJournal.cs)
- **战斗运行时。** 每个角色维护独立的技能和冷却状态，技能缓存 Lua 执行委托。Buff 统一处理周期结算、叠层、刷新和到期移除，并通过对象池复用实例。[技能执行](Assets/Scripts/Runtime/Skill/Skill.cs) · [Buff 调度](Assets/Scripts/Runtime/Buff/BuffManager.cs) · [对象池](Assets/Scripts/Runtime/Buff/BuffPool.cs)

```text
制作：SkillSO → Graph 编排 → Lua 模板 / 标准化技能描述
运行：角色 → SkillManager → CharacterRuntime → Skill → Lua → 伤害 / Buff
```

## 在 Unity 中查看

1. 使用 **Unity 2022.3.59f1c1** 打开项目。xLua 已包含在仓库中，可通过 Package Manager 确认 Input System（1.11.2）和 Cinemachine（2.10.7）。
2. 打开 **Tools → 技能 → 技能编辑器**，选择技能并绑定 Lua。新技能可以先导出普通模板。
3. 在 **Lua 详情与工具 → 打开技能 Graph** 中连接节点，检查配置后分别生成描述和 Lua。也可在编辑器中查看基础 CSV 导入、配置校验和 SkillID 枚举导出。
4. 将技能配置给场景角色。仓库内的 [SampleScene](Assets/Scenes/SampleScene.unity) 可用于查看现有角色、技能与 Buff 的配置。

修改 Graph 后可以按需重新生成。覆盖已有 Lua 前会提示确认，图内撤销只影响 Graph 的编辑记录。
