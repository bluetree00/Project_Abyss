public class UI_StageMap : UI_Scene
{
    public override void Init()
    {
        base.Init();
        RefreshStageMap();
    }

    public void RefreshStageMap()
    {
        var stageMgr = Managers.GameRun.StagePointManager;

        // 이미 Resolve된 Context 기반으로 UI 구성
    }
}
