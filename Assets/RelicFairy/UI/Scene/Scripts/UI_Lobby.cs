using System;
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
        OpenAltarReadOnlyAsync().Forget();
    }

    /// <summary>
    /// 로비에서는 기억의 제단을 <b>조회 전용</b>으로 연다.
    /// <para>여기서 해금까지 되면 거점에 돌아올 이유가 사라지고, 「죽음 → 복귀 동선 → 재출발」이라는
    /// 전체 설계가 무의미해진다. 정수를 쓰는 것은 거점 제단에서만.</para>
    /// </summary>
    private async UniTaskVoid OpenAltarReadOnlyAsync()
    {
        try
        {
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_AwakeningPanel>();
            if (panel != null) panel.ReadOnly = true;
        }
        catch (OperationCanceledException) { }
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
