public class UI_StageMap : UI_Scene
{
    public override void Init()
    {
        base.Init();
        RefreshStageMap();
    }

    public void RefreshStageMap()
    {
        var app = AppBootstrapper.Instance;
        if (app == null) return;

        var session = app.CurrentRun;
        if (session == null) return;

        var stageMgr = session.StagePointManager;

        // 이미 Resolve된 Context 기반으로 UI 구성
    }
}
