public class UI_StageMap : UI_Scene
{
    public override void Init()
    {
        base.Init();
        RefreshStageMap();
    }

    public void RefreshStageMap()
    {
        var bootstrapper = GameRunBootstrapper.Instance;
        if (bootstrapper == null) return;

        var stageMgr = bootstrapper.Run?.StagePointManager;

        // 이미 Resolve된 Context 기반으로 UI 구성
    }
}
