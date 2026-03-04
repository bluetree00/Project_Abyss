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
        if (!Input.GetKeyDown(clearRoomKey)) return;

        var inst = bootstrapper != null ? bootstrapper : GameRunBootstrapper.Instance;
        var run = inst != null ? inst.Run : null;
        if (run == null || !run.IsRunning) return;
        if (run.CurrentRunState == GameRunSession.RunState.Map) return;

        var spm = run.StagePointManager;
        if (spm != null && spm.CurrentPointId >= 0)
            spm.MarkCleared(spm.CurrentPointId);

        run.EnterMap();
        Debug.Log($"[DebugRunPanel] {clearRoomKey} → 방 클리어 스킵, Map 전환");
    }

    private async UniTaskVoid StartRun()
    {
        await UniTask.WaitUntil(() => AppBootstrapper.Instance != null && AppBootstrapper.Instance.IsReady);

        if (bootstrapper == null)
            bootstrapper = FindObjectOfType<GameRunBootstrapper>(true);

        if (bootstrapper == null)
        {
            Debug.LogError("[DebugRunPanel] GameRunBootstrapper not found.");
            return;
        }

        // 씬 오브젝트 바인딩 한 번 보장
        bootstrapper.Bind();

        await bootstrapper.StartRunAsync(chapter);
    }
}
