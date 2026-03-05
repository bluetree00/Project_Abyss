using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

public sealed class DebugStageRunPanel : MonoBehaviour
{
    [SerializeField] private Button startButton;
    [SerializeField] private ChapterId chapter = ChapterId.Chapter1;

    [Header("Optional")]
    [SerializeField] private GameRunBootstrapper bootstrapper;

    [Header("Debug Keys")]
    [SerializeField] private KeyCode clearRoomKey = KeyCode.F5;
    [SerializeField] private KeyCode returnToStageMapKey = KeyCode.F6;

    private bool _clicked;

    private void Awake()
    {
        if (startButton == null)
            startButton = GetComponent<Button>();

        if (startButton == null)
        {
            Debug.LogError("[DebugRunPanel] startButton is null.");
            return;
        }

        if (bootstrapper == null)
            bootstrapper = FindObjectOfType<GameRunBootstrapper>(true);

        startButton.onClick.AddListener(() =>
        {
            if (_clicked) return;
            _clicked = true;
            StartRun().Forget();
        });
    }

    private void Update()
    {
        if (Input.GetKeyDown(clearRoomKey))
            HandleClearRoom();
        else if (Input.GetKeyDown(returnToStageMapKey))
            HandleReturnToStageMap();
    }

    private void HandleClearRoom()
    {
        var run = GetCurrentRun();
        if (run == null || !run.IsRunning) return;
        if (run.CurrentRunState == GameRunSession.RunState.Map) return;

        var spm = run.StagePointManager;
        if (spm != null && spm.CurrentPointId >= 0)
            spm.MarkCleared(spm.CurrentPointId);

        run.EnterMap();
        Debug.Log($"[DebugRunPanel] {clearRoomKey} → 방 클리어 스킵, Map 전환");
    }

    private void HandleReturnToStageMap()
    {
        var run = GetCurrentRun();
        if (run == null || !run.IsRunning) return;

        // 현재 포인트 클리어 처리
        var spm = run.StagePointManager;
        if (spm != null && spm.CurrentPointId >= 0)
            spm.MarkCleared(spm.CurrentPointId);

        Debug.Log($"[DebugRunPanel] {returnToStageMapKey} → 전투 클리어, StageMap 씬 전환");
        AppBootstrapper.Instance.RequestLoad(Define.Scene.StageMap);
    }

    private async UniTaskVoid StartRun()
    {
        await UniTask.WaitUntil(() => AppBootstrapper.Instance != null && AppBootstrapper.Instance.IsReady);

        var app = AppBootstrapper.Instance;

        // StageMap 씬: StageMapBootstrapper가 비동기로 런을 초기화 중일 수 있으므로 대기
        if (StageMapBootstrapper.Instance != null)
        {
            await UniTask.WaitUntil(() => app.CurrentRun != null && app.CurrentRun.IsRunning);
            Debug.Log("[DebugRunPanel] StageMapBootstrapper 런 준비 완료.");
            _clicked = false;
            return;
        }

        // GameScene: GameRunBootstrapper를 통해 런 시작
        if (bootstrapper == null)
            bootstrapper = FindObjectOfType<GameRunBootstrapper>(true);

        if (bootstrapper == null)
        {
            Debug.LogError("[DebugRunPanel] GameRunBootstrapper not found.");
            _clicked = false;
            return;
        }

        bootstrapper.Bind();
        await bootstrapper.StartRunAsync(chapter);
    }

    /// <summary>
    /// GameRunBootstrapper 또는 AppBootstrapper.CurrentRun에서 세션을 가져옵니다.
    /// </summary>
    private GameRunSession GetCurrentRun()
    {
        if (bootstrapper != null) return bootstrapper.Run;
        var inst = GameRunBootstrapper.Instance;
        if (inst != null) return inst.Run;
        var app = AppBootstrapper.Instance;
        return app != null ? app.CurrentRun : null;
    }
}
