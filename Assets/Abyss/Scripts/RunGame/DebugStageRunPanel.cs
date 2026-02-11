using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

public sealed class DebugStageRunPanel : MonoBehaviour
{
    [SerializeField] private Button startButton;
    [SerializeField] private ChapterId chapter = ChapterId.Chapter1;

    [Header("Optional")]
    [SerializeField] private GameRunBootstrapper bootstrapper;

    private bool _clicked;

    private void Awake()
    {
        if (startButton == null)
            startButton = GetComponent<Button>();

        if (startButton == null)
        {
            Debug.LogError("[DebugRunPanel] startButton is null. Button 컴포넌트를 연결하거나 같은 오브젝트에 붙여주세요.");
            return;
        }

        // bootstrapper가 인스펙터에 없으면 씬에서 찾기
        if (bootstrapper == null)
            bootstrapper = FindObjectOfType<GameRunBootstrapper>(true);

        startButton.onClick.AddListener(() =>
        {
            if (_clicked) return;
            _clicked = true;

            StartRun().Forget();
        });
    }

    private async UniTaskVoid StartRun()
    {
        if (bootstrapper == null)
            bootstrapper = FindObjectOfType<GameRunBootstrapper>(true);

        if (bootstrapper == null)
        {
            Debug.LogError("[DebugRunPanel] GameRunBootstrapper not found. Managers 자동생성(Ensure...)이 동작하는지 확인하세요.");
            return;
        }

        await bootstrapper.StartRunAsync(chapter);
    

    }

}
