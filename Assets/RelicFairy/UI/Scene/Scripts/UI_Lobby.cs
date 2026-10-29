using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class UI_Lobby : UI_Scene
{
    // ─────────────────────────────────────────────────────────
    // SerializeField
    // ─────────────────────────────────────────────────────────

    [Header("서브 패널")]
    [SerializeField] private UI_SaveSlotPanel saveSlotPanel;

    // ─────────────────────────────────────────────────────────
    // Enums
    // ─────────────────────────────────────────────────────────

    private enum Buttons
    {
        Btn_StartRun,
        Btn_Settings,
        Btn_Exit,
    }

    // ─────────────────────────────────────────────────────────
    // Private
    // ─────────────────────────────────────────────────────────

    private CanvasGroup _titleImageCG;
    private System.Threading.CancellationTokenSource _titleCts;

    // ─────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────

    public override void Init()
    {
        base.Init();

        Bind<Button>(typeof(Buttons));

        GetButton((int)Buttons.Btn_StartRun).onClick.AddListener(OnClickStartRun);
        GetButton((int)Buttons.Btn_Settings).onClick.AddListener(OnClickSettings);
        GetButton((int)Buttons.Btn_Exit).onClick.AddListener(OnClickExit);

        var titleTr = transform.Find("TitleImage");
        _titleImageCG = titleTr?.GetComponent<CanvasGroup>();

        if (saveSlotPanel != null)
        {
            saveSlotPanel.Init();
            saveSlotPanel.OnClosed += OnSaveSlotPanelClosed;
        }
    }

    /// <summary>
    /// 표시될 때마다 되돌려야 하는 상태. Init은 이제 1회만 도므로(리스너 중복 방지) 여기로 옮겼다.
    /// 런을 시작할 땐 슬롯 패널이 열린 채 로비를 떠나므로, 돌아왔을 때 그대로면 타이틀이 가려진다.
    /// </summary>
    private void OnEnable()
    {
        saveSlotPanel?.Close();
        SetTitleImage(true);
    }

    // ─────────────────────────────────────────────────────────
    // Event Handlers
    // ─────────────────────────────────────────────────────────

    private void OnClickStartRun()
    {
        if (saveSlotPanel != null)
        {
            SetTitleImage(false);
            saveSlotPanel.Open();
        }
        else
        {
            Managers.Sound?.PlayUiEvent(SoundEvent.UiClick);   // 10-29 UI 소리 — 누름(슬롯 창이 있으면 창 열기 소리가 대신한다)
            AppBootstrapper.Instance?.RequestStartRun();
        }
    }

    private void OnSaveSlotPanelClosed()
    {
        SetTitleImage(true);
    }

    /// <summary>
    /// 예전엔 UI_Pause(챕터/무기/인벤 탭)를 열었다 — 로비에서 일시정지 화면을 여는 배선 자체가 잘못이었고,
    /// 그 탭들은 갱신 로직이 전부 주석 처리된 자리표시자라 볼 것도 없었다. 실제 설정 화면으로 돌린다.
    /// </summary>
    private void OnClickSettings()
    {
        UI_Settings.Open();
    }

    /// <summary>EXIT — 한 번 묻고 끈다(10-16 사용자 「exit나 게임종료 버튼에 종료하겠냐고 물어봐야해」).</summary>
    private void OnClickExit()
        => UI_ConfirmDialog.Show("게임 종료", "게임을 종료하시겠습니까?", "종료", "취소", QuitApp, danger: true);

    private static void QuitApp()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ─────────────────────────────────────────────────────────
    // Private Methods
    // ─────────────────────────────────────────────────────────

    /// <summary>타이틀 그림 — 슬롯 패널과 교차 페이드(예전엔 알파가 0/1로 순간 전환, 09-28 UI 톤 통일).</summary>
    private void SetTitleImage(bool visible)
    {
        if (_titleImageCG == null) return;
        _titleImageCG.blocksRaycasts = false;
        _titleCts?.Cancel();
        _titleCts?.Dispose();
        _titleCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        FadeTitleAsync(visible ? 1f : 0f, visible ? UIFader.OpenSec : UIFader.CloseSec, _titleCts.Token).Forget();
    }

    private async UniTaskVoid FadeTitleAsync(float target, float dur, System.Threading.CancellationToken ct)
    {
        try
        {
            while (_titleImageCG != null && !Mathf.Approximately(_titleImageCG.alpha, target))
            {
                _titleImageCG.alpha = Mathf.MoveTowards(_titleImageCG.alpha, target, Time.unscaledDeltaTime / dur);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (System.OperationCanceledException) { }
    }
}
