using UnityEngine;
using Cysharp.Threading.Tasks;

public sealed class GameRunBootstrapper : MonoBehaviour
{
    [SerializeField] private StageMapSpawner spawner;

    private StagePointUI[] _points;

    private void Awake()
    {
        Bind();
    }
     
     public void Bind()
    {
        if (spawner == null)
            spawner = FindObjectOfType<StageMapSpawner>(true);

        Managers.GameRun.Spawner = spawner;

        // (선택) 런이 이미 진행 중인데 씬이 다시 로드된 상황 대비
        if (Managers.GameRun.IsRunning && Managers.GameRun.StagePointManager != null)
        {
            _points = FindObjectsOfType<StagePointUI>(true);
            Managers.GameRun.RegisterPoints(_points);
        }
    }

    public async UniTask StartRunAsync(ChapterId chapter)
    {
        // 1) 런 로직 초기화
        await Managers.GameRun.StartNewRunAsync(chapter);

        if (!Managers.GameRun.IsRunning || Managers.GameRun.RoomManager == null || !Managers.GameRun.RoomManager.IsInitialized)
            return;

        // 2) 씬 UI 등록 (구독 먼저!)
        _points = FindObjectsOfType<StagePointUI>(true);
        Managers.GameRun.RegisterPoints(_points); // <- GameRunManager에 추가해둔 메서드

        // 3) 전체 Resolve + Start 세팅
        Managers.GameRun.ResolveAllPointsAndSetStart(); // <- 이것도 GameRunManager에 추가

        // 4) 시작 맵 스폰
        Managers.GameRun.Spawner = spawner; // 주입(혹은 Bind에서 한 번)
        Managers.GameRun.SpawnCurrentPointMap();
    }
}
