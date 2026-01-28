using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

public sealed class DebugRunPanel : MonoBehaviour
{
    [SerializeField] private Button startButton;
    [SerializeField] private ChapterId chapter = ChapterId.Chapter1;

    private bool _clicked;

    private void Awake()
    {
        if (startButton == null)
            startButton = GetComponent<Button>();

        startButton.onClick.AddListener(() =>
        {
            if (_clicked) return;   // ✅ 연타 방지
            _clicked = true;

            StartRun().Forget();
        });
    }

    private async UniTaskVoid StartRun()
    {
        await Managers.GameRun.StartNewRunAsync(chapter);
    }
}
