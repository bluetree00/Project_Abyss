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
        Btn_Awakening,
    }

    // ─────────────────────────────────────────────────────────
    // Private
    // ─────────────────────────────────────────────────────────

    private CanvasGroup _titleImageCG;

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
        GetButton((int)Buttons.Btn_Awakening)?.onClick.AddListener(OnClickAwakening);

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
            AppBootstrapper.Instance?.RequestStartRun();
        }
    }

    private void OnSaveSlotPanelClosed()
    {
        SetTitleImage(true);
    }

    private void OnClickSettings()
    {
        Managers.UI.ShowPopupUI<UI_Pause>();
    }

    private void OnClickAwakening()
    {
        Managers.UI.ShowPopupUI<UI_AwakeningPanel>();
    }

    private void OnClickExit()
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

    private void SetTitleImage(bool visible)
    {
        if (_titleImageCG == null) return;
        _titleImageCG.alpha = visible ? 1f : 0f;
        _titleImageCG.blocksRaycasts = false;
    }
}
