namespace SkillGraphEditor
{
    // 导出顺序仅由连接决定，画布位置和节点列表顺序不参与执行顺序。
    public static class GraphTemplateExport
    {
        public static string Generate(GraphData graph, out UnityEngine.Object[] resources)
        {
            return GraphLuaGenerator.Generate(GraphGenerationPlan.Build(graph), out resources);
        }

        public static string GenerateDescription(GraphData graph)
        {
            return GraphDescriptionGenerator.Generate(GraphGenerationPlan.Build(graph));
        }
    }
}
