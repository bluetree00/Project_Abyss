using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ESC 북 팝업 — PAUSE / SKILL / INVENTORY 탭
/// 필요할 때만 활성화, ESC 감지는 EscKeyListener에서 처리
/// </summary>
public sealed class EscBookPopup : UI_Popup
{
    [Header("Book Background")]
    [SerializeField] private Image bookImage;
    [SerializeField] private Sprite[] pageFlipSprites; // Page1~Page10

    [Header("Pages")]
    [SerializeField] private GameObject pagePause;
    [SerializeField] private GameObject pageSkill;
    [SerializeField] private GameObject pageInventory;

    [Header("Tab Buttons")]
    [SerializeField] private Button tabPause;
    [SerializeField] private Button tabSkill;
    [SerializeField] private Button tabInventory;

    [Header("Pause Buttons")]
    [SerializeField] private Button btnResume;
    [SerializeField] private Button btnOption;
    [SerializeField] private Button btnLobby;
    [SerializeField] private Button btnQuit;

    [Header("Animation")]
    [SerializeField] private float flipDuration = 0.4f;

    private int _currentTab;
    private bool _isFlipping;

    private void Awake()
    {
        tabPause?.onClick.AddListener(() => RequestSwitchTab(0));
        tabSkill?.onClick.AddListener(() => RequestSwitchTab(1));
        tabInventory?.onClick.AddListener(() => RequestSwitchTab(2));

        btnResume?.onClick.AddListener(ClosePopup);
        btnOption?.onClick.AddListener(OnOption);
        btnLobby?.onClick.AddListener(OnLobby);
        btnQuit?.onClick.AddListener(OnQuit);
    }

    public void OpenPopup()
    {
        gameObject.SetActive(true);
        EnsureInventoryView();
        EnsureCharacterInfoView();
        _currentTab = 0;
        SetPageImmediate(0);
        TimeScaleArbiter.Acquire(this, 0f, TimeScaleArbiter.Priority.Pause);
    }

    private void EnsureInventoryView()
    {
        if (pageInventory == null) return;
        if (pageInventory.GetComponent<InventoryPageView>() == null)
            pageInventory.AddComponent<InventoryPageView>();
    }

    private void EnsureCharacterInfoView()
    {
        if (pageSkill == null) return;
        if (pageSkill.GetComponent<CharacterInfoPageView>() == null)
            pageSkill.AddComponent<CharacterInfoPageView>();
    }

    public void ClosePopup()
    {
        StopAllCoroutines();
        _isFlipping = false;
        gameObject.SetActive(false);
        TimeScaleArbiter.Release(this);
    }

    public override void ClosePopupUI()
    {
        ClosePopup();
    }

    private void RequestSwitchTab(int tab)
    {
        if (tab == _currentTab || _isFlipping) return;
        bool forward = tab > _currentTab; // 다음 탭 = 정방향, 이전 탭 = 역방향
        StartCoroutine(FlipAndSwitch(tab, forward));
    }

    private IEnumerator FlipAndSwitch(int targetTab, bool forward)
    {
        _isFlipping = true;

        // 현재 페이지 콘텐츠 숨기기
        SetAllPagesOff();

        // 페이지 넘김 애니메이션
        if (pageFlipSprites != null && pageFlipSprites.Length > 0 && bookImage != null)
        {
            int count = pageFlipSprites.Length;
            float interval = flipDuration / count;

            if (forward)
            {
                // 정방향: Page1 → Page10
                for (int i = 0; i < count; i++)
                {
                    bookImage.sprite = pageFlipSprites[i];
                    yield return new WaitForSecondsRealtime(interval);
                }
            }
            else
            {
                // 역방향: Page10 → Page1
                for (int i = count - 1; i >= 0; i--)
                {
                    bookImage.sprite = pageFlipSprites[i];
                    yield return new WaitForSecondsRealtime(interval);
                }
            }

            // 펼친 책으로 복귀
            bookImage.sprite = pageFlipSprites[0];
        }

        // 새 페이지 표시
        _currentTab = targetTab;
        SetPageImmediate(targetTab);
        _isFlipping = false;
    }

    private void SetPageImmediate(int tab)
    {
        if (pagePause != null) pagePause.SetActive(tab == 0);
        if (pageSkill != null) pageSkill.SetActive(tab == 1);
        if (pageInventory != null) pageInventory.SetActive(tab == 2);

        // 책 배경을 펼친 상태로
        if (bookImage != null && pageFlipSprites != null && pageFlipSprites.Length > 0)
            bookImage.sprite = pageFlipSprites[0];
    }

    private void SetAllPagesOff()
    {
        if (pagePause != null) pagePause.SetActive(false);
        if (pageSkill != null) pageSkill.SetActive(false);
        if (pageInventory != null) pageInventory.SetActive(false);
    }

    private void OnOption()
    {
        Debug.Log("[EscBookPopup] Option clicked");
    }

    private void OnLobby()
    {
        Debug.Log("[EscBookPopup] Lobby clicked");
        ClosePopup();
    }

    private void OnQuit()
    {
        // 강제 닫힘 대비 timeScale 복원 후 종료(멱등 Release).
        TimeScaleArbiter.Release(this);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();   // 메서드 하이딩(CS0114) 해소 — UI_Popup의 차단 잠금 통지를 가리지 않는다
        TimeScaleArbiter.Release(this);
    }
}
