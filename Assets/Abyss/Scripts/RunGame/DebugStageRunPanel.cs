using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

public sealed class DebugStageRunPanel : MonoBehaviour
{
    [SerializeField] private Button startButton;
    [SerializeField] private ChapterId chapter = ChapterId.Chapter1;

    [Header("Optional")]
    [SerializeField] private GameRunBootstrapper bootstrapper;

    [Header("Temp UI")]
    [SerializeField] private GameObject stageUIRoot;   // 👈 추가

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

            // 👇 버튼 눌리면 StageUI 비활성화
            if (stageUIRoot != null)
                stageUIRoot.SetActive(false);

            StartRun().Forget();
        });
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
