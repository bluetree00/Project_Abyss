using UnityEngine;
using MapGeneratorManager;
namespace StageSystem
{
    /// <summary>
    /// 기본 그래프 생성 구현. 기존 StageManager.GenerateGraph 로직 이동.
    /// </summary>
    public class DefaultStageGraphBuilder : IStageGraphBuilder
    {
        public Graph BuildGraph(StageGraphData data)
        {
            try
            {
                var chapterData = new StageData.ChapterData
                {
                    chapterName = System.Enum.Parse<StageData.ChapterName>(data.chapterName),
                    layerSizes = data.layerSizes.ToArray(),
                    stages = data.stages.ConvertAll(s => new StageData.ChapterData.StageSettings
                    {
                        stageName = System.Enum.Parse<StageData.StageName>(s.stageName)
                    }),
                    bossStageName = "Stage_10" // TODO: derive from data
                };
                return MapGeneratorManager.MapGeneratorManager.Generate(chapterData);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[DefaultStageGraphBuilder] Graph build failed: {e.Message}. Using fallback.");
                var fallback = new StageData.ChapterData
                {
                    chapterName = StageData.ChapterName.Chapter1,
                    layerSizes = new int[] {1,2,3,4,3,2,1},
                    stages = CreateFallbackStages(),
                    bossStageName = StageData.StageName.Stage_10.ToString()
                };
                return MapGeneratorManager.MapGeneratorManager.Generate(fallback);
            }
        }

        private System.Collections.Generic.List<StageData.ChapterData.StageSettings> CreateFallbackStages()
        {
            var list = new System.Collections.Generic.List<StageData.ChapterData.StageSettings>();
            for (int i = 0; i < 10; i++)
            {
                list.Add(new StageData.ChapterData.StageSettings { stageName = (StageData.StageName)i });
            }
            return list;
        }
    }
}
