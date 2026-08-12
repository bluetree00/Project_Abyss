//============================================================
// HudView.cs
// - Section 플래그에 따라 루트 표시 스왑
// - 각 패널은 Presenter가 패널별 View로 직접 데이터 전달
//============================================================
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class HudView : MonoBehaviour
{
    // 런 재화(강화재료·원석) 임시 아이콘 색 — 전용 아이콘 에셋 나오면 교체.
    private static readonly Color EnhanceMatIcon = new(0.90f, 0.55f, 0.20f); // 강화재료(주황)
    private static readonly Color RuneOreIcon    = new(0.35f, 0.70f, 0.95f); // 원석(청록)

    // 재화별 테두리 틴트 — 같은 테두리 아트를 색으로만 구분한다(재화당 아트를 따로 받지 않기 위함).
    // 소비처와 색을 1:1로 묶어 "이 색 = 이 방에서 쓰는 것"이 학습되게 한다.
    private static readonly Color GoldFrameTint    = new(0.85f, 0.70f, 0.29f); // 골드   → 상점
    private static readonly Color EnhanceFrameTint = new(0.90f, 0.55f, 0.20f); // 강화재료 → 재련소
    private static readonly Color RuneOreFrameTint = new(0.35f, 0.70f, 0.95f); // 원석    → 정제소

    // 툴팁 문구 — 소비처 + "런이 끝나면 사라진다"(하드리셋 규칙을 재화 단에서 알린다).
    private const string TipGoldTitle    = "골드";
    private const string TipGoldBody     = "상점에서 물건과 서비스를 산다.\n런이 끝나면 사라진다.";
    private const string TipEnhanceTitle = "강화재료";
    private const string TipEnhanceBody  = "재련소에서 무기를 강화·진화한다.\n런이 끝나면 사라진다.";
    private const string TipRuneOreTitle = "원석";
    private const string TipRuneOreBody  = "정제소에서 존핵 룬을 벼린다.\n런이 끝나면 사라진다.";

    [Header("Sections")]
    [SerializeField] private GameObject topBarRoot;
    [SerializeField] private CombatPanelView combatPanel;
    [SerializeField] private GameObject gridPanel;
    [SerializeField] private BossPanelView bossPanelView;
    [SerializeField] private GameObject systemNoticesRoot;
    [SerializeField] private GameObject minimapPanel;
    [SerializeField] private CovenantPanelView covenantPanel;

    [Header("TopBar Info")]
    [SerializeField] private TMP_Text nicknameText;
    [SerializeField] private TMP_Text goldText;

    [Header("재화 라인 (골드·강화재료·원석 — 한 줄, 같은 배경 공유)")]
    [Tooltip("골드바 테두리 — ! 아트에 금색 코인이 박혀 있어 골드 전용. 다른 재화엔 못 씀.")]
    [SerializeField] private Sprite goldFrameSprite;
    [Tooltip("골드바 내부 — 코인이 없어 모든 재화가 공유하는 공통 플레이트 배경.")]
    [SerializeField] private Sprite goldInnerSprite;
    [Tooltip("코인 없는 공용 테두리(추가 리소스). 지정하면 모든 재화가 동일 테두리를 쓴다.")]
    [SerializeField] private Sprite currencyFrameSprite;

    [SerializeField] private Vector2 currencyPillSize    = new Vector2(150f, 40f);
    [SerializeField] private float   currencyPillSpacing = 8f;
    [Tooltip("재화 라인 위치 — 화면 우상단 코너 기준 오프셋(음수=안쪽). 목업은 코너에 붙지 않고 안쪽으로 들어와 있다.")]
    [SerializeField] private Vector2 currencyRowOffset   = new Vector2(-56f, -74f);
    [Tooltip("pill 내용(아이콘+수치)을 테두리 안쪽으로 들여넣는 여백 (L,B,R,T)")]
    [SerializeField] private Vector4 currencyPillPadding = new Vector4(14f, 8f, 14f, 8f);
    /// <summary>테두리 아트에 코인이 그려져 있어 기존 GoldIcon과 중복될 때 숨긴다.</summary>
    [SerializeField] private bool hideLegacyGoldIcon = true;
    [Tooltip("래거시 상단바 배경판(Panel_TopBar의 Image) 투명화 — 재화 pill이 자체 배경을 가져 이중이 된다.")]
    [SerializeField] private bool hideLegacyTopBarBg = true;

    // 재화 라인 — 골드/강화재료/원석이 같은 줄에 같은 배경으로. 0이면 연료 pill 숨김.
    private RectTransform _currencyRow;
    private GameObject    _goldSlot;
    private GameObject    _enhanceMatSlot;
    private GameObject    _runeOreSlot;
    private TMP_Text      _enhanceMatText;
    private TMP_Text      _runeOreText;
    private bool          _currencyBuilt;

    // 재화 표시 정책 — "실제로 얻은 재화만 보인다".
    // 한 번 획득하면 그 런 동안은 계속 보인다(0으로 써 버렸다고 칸이 사라지면 깜빡여 읽기 나쁘다).
    // 로비 복귀·새 런 시작에서 ResetCurrencyVisibility()로 되돌린다.
    private bool _goldSeen, _enhanceMatSeen, _runeOreSeen;

    public CombatPanelView   CombatPanel   => combatPanel;
    public BossPanelView     BossPanel     => bossPanelView;
    public CovenantPanelView CovenantPanel => covenantPanel;
    public MinimapView MinimapView { get; private set; }

    private void Awake()
    {
        combatPanel    ??= GetComponentInChildren<CombatPanelView>(true);
        bossPanelView  ??= GetComponentInChildren<BossPanelView>(true);
        covenantPanel  ??= GetComponentInChildren<CovenantPanelView>(true);

        if (bossPanelView == null)
        {
            var bossRoot = FindChildRecursive(transform, "Panel_boss");
            if (bossRoot != null)
                bossPanelView = bossRoot.GetComponent<BossPanelView>() ?? bossRoot.gameObject.AddComponent<BossPanelView>();
        }

        if (minimapPanel == null)
        {
            var mapRoot = FindChildRecursive(transform, "Panel_Minimap");
            if (mapRoot != null)
                minimapPanel = mapRoot.gameObject;
        }

        if (minimapPanel != null)
            MinimapView = minimapPanel.GetComponentInChildren<MinimapView>(true);

        EnsureCurrencyRow();

        // 닉네임은 로비(BindLobby)에서만 바인딩된다 → 기본은 숨김.
        // 실제 값이 들어오면 SetNickname이 켠다 (프리팹 플레이스홀더 "Nickname" 노출 방지).
        if (nicknameText != null)
            nicknameText.gameObject.SetActive(false);
    }

    /// <summary>
    /// 재화 라인 1줄 생성 — 골드 · 강화재료 · 원석이 <b>같은 줄</b>에 <b>같은 배경(골드바 내부)</b>을 공유한다.
    /// 각 재화는 pill(배경 + 아이콘 + 수치) 하나. 추후 재화도 pill만 추가하면 된다.
    ///
    /// ⚠️ '골드바 테두리'는 아트에 <b>금색 코인이 박혀 있어</b> 골드 전용이다.
    ///    코인 없는 공용 테두리(currencyFrameSprite)를 주면 모든 재화가 동일 테두리를 쓴다.
    /// 스프라이트 미지정 시 아무 것도 하지 않아 기존 표시가 유지된다(회귀 0).
    /// </summary>
    private void EnsureCurrencyRow()
    {
        if (_currencyBuilt || goldText == null) return;
        if (goldInnerSprite == null && goldFrameSprite == null && currencyFrameSprite == null) return;

        var parent = goldText.transform.parent;
        if (parent == null) return;
        _currencyBuilt = true;

        var rowGo = new GameObject("CurrencyRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        _currencyRow = (RectTransform)rowGo.transform;
        _currencyRow.SetParent(parent, false);

        // 우상단 고정(피벗 우상단) — goldText의 앵커/피벗을 그대로 물려받으면 넓어진 행이
        // 화면 밖으로 밀려나 골드가 안 보인다. 명시적으로 코너에 핀한다.
        _currencyRow.anchorMin = new Vector2(1f, 1f);
        _currencyRow.anchorMax = new Vector2(1f, 1f);
        _currencyRow.pivot     = new Vector2(1f, 1f);
        _currencyRow.anchoredPosition = currencyRowOffset;
        _currencyRow.sizeDelta = new Vector2(currencyPillSize.x * 3f + currencyPillSpacing * 2f, currencyPillSize.y);

        var hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing            = currencyPillSpacing;
        hlg.childAlignment     = TextAnchor.MiddleRight;
        hlg.childControlWidth  = true;  hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        // 골드 — 기존 goldText를 pill 안으로 이동(래거시 위치 정리). 테두리엔 코인이 포함됨.
        _goldSlot = MakeCurrencyPill("Pill_Gold", goldFrameSprite != null ? goldFrameSprite : currencyFrameSprite,
                                     iconColor: null, GoldFrameTint, TipGoldTitle, TipGoldBody, out _, reuseText: goldText);

        // 강화재료 / 원석 — 코인 없는 공용 테두리가 있으면 그걸, 없으면 배경(내부)만.
        _enhanceMatSlot = MakeCurrencyPill("Pill_EnhanceMat", currencyFrameSprite, EnhanceMatIcon,
                                           EnhanceFrameTint, TipEnhanceTitle, TipEnhanceBody, out _enhanceMatText);
        _runeOreSlot    = MakeCurrencyPill("Pill_RuneOre",    currencyFrameSprite, RuneOreIcon,
                                           RuneOreFrameTint, TipRuneOreTitle, TipRuneOreBody, out _runeOreText);
        // 아직 아무것도 얻지 않은 상태로 시작 — 획득 시 setter가 켠다.
        _goldSlot?.SetActive(_goldSeen);
        _enhanceMatSlot.SetActive(false);
        _runeOreSlot.SetActive(false);

        // 래거시 골드 아이콘 — 테두리 아트에 코인이 있어 중복이므로 숨김.
        if (hideLegacyGoldIcon && goldFrameSprite != null)
        {
            var legacy = FindChildRecursive(transform, "GoldIcon");
            if (legacy != null && legacy.gameObject.activeSelf)
                legacy.gameObject.SetActive(false);
        }

        // 래거시 상단바 배경판(Panel_TopBar의 Image) — 재화 pill이 자체 배경(골드바 내부)을 가지므로
        // 뒤에 남으면 이중 배경으로 보인다. 스킨 시 투명화(오브젝트는 유지 — 자식 레이아웃 보존).
        if (hideLegacyTopBarBg && parent.TryGetComponent<Image>(out var topBarBg))
            topBarBg.color = new Color(0f, 0f, 0f, 0f);
    }

    /// <summary>재화 pill 1개: 공통 배경(골드바 내부) + 테두리(선택) + 아이콘(선택) + 수치.</summary>
    private GameObject MakeCurrencyPill(string name, Sprite frame, Color? iconColor,
                                        Color frameTint, string tipTitle, string tipBody,
                                        out TMP_Text valueText, TMP_Text reuseText = null)
    {
        var pill = new GameObject(name, typeof(RectTransform));
        var prt  = (RectTransform)pill.transform;
        prt.SetParent(_currencyRow, false);

        var le = pill.AddComponent<LayoutElement>();
        le.preferredWidth = currencyPillSize.x; le.preferredHeight = currencyPillSize.y;
        le.minWidth       = currencyPillSize.x; le.minHeight       = currencyPillSize.y;

        // 호버 판정면 — pill 자체 이미지는 전부 raycastTarget=false라 이게 없으면 툴팁이 안 뜬다.
        var hit = prt.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;

        AddStretchedImage(prt, "Inner", goldInnerSprite);            // 모든 재화 공통 배경
        AddStretchedImage(prt, "Frame", frame, frameTint);           // 테두리는 재화색으로 틴트
        UITooltipTrigger.Attach(prt.gameObject, tipTitle, tipBody, frameTint);

        // 내용(아이콘 + 수치) — 테두리 안쪽으로 인셋
        var content = new GameObject("Content", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        var crt = (RectTransform)content.transform;
        crt.SetParent(prt, false);
        crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
        crt.offsetMin = new Vector2(currencyPillPadding.x, currencyPillPadding.y);
        crt.offsetMax = new Vector2(-currencyPillPadding.z, -currencyPillPadding.w);

        var chlg = content.GetComponent<HorizontalLayoutGroup>();
        chlg.spacing            = 6f;
        chlg.childAlignment     = TextAnchor.MiddleLeft;
        chlg.childControlWidth  = true;  chlg.childControlHeight = true;
        chlg.childForceExpandWidth = false; chlg.childForceExpandHeight = false;

        if (iconColor.HasValue)   // 골드는 테두리 아트에 코인이 있어 아이콘 생략
        {
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(crt, false);
            iconGo.GetComponent<Image>().color = iconColor.Value;
            var ile = iconGo.AddComponent<LayoutElement>();
            ile.preferredWidth = 20f; ile.preferredHeight = 20f;
        }

        if (reuseText != null)
        {
            reuseText.rectTransform.SetParent(crt, false);
            valueText = reuseText;
        }
        else
        {
            var txtGo = new GameObject("Value", typeof(RectTransform));
            txtGo.transform.SetParent(crt, false);
            valueText = txtGo.AddComponent<TextMeshProUGUI>();
            if (goldText != null) { valueText.font = goldText.font; valueText.fontSize = goldText.fontSize; }
            valueText.text = "0";
        }
        valueText.color     = new Color(0.93f, 0.91f, 0.85f, 1f);
        valueText.alignment = TextAlignmentOptions.MidlineRight;
        if (!valueText.TryGetComponent<LayoutElement>(out var tle))
            tle = valueText.gameObject.AddComponent<LayoutElement>();
        tle.flexibleWidth = 1f;

        return pill;
    }

    private static void AddStretchedImage(Transform parent, string name, Sprite sprite, Color? tint = null)
    {
        if (sprite == null) return;

        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.GetComponent<Image>();
        img.sprite        = sprite;
        img.type          = Image.Type.Sliced;
        img.color         = tint ?? Color.white;
        img.raycastTarget = false;
    }

    public void SetSections(HUDIds.Section sections)
    {
        bool showCombat = (sections & HUDIds.Section.CombatPanel) != 0;

        SetActiveSafe(topBarRoot,       (sections & HUDIds.Section.TopBar)        != 0);
        SetActiveSafe(combatPanel,      showCombat);
        SetActiveSafe(gridPanel,        (sections & HUDIds.Section.GridPanel)      != 0);
        SetActiveSafe(bossPanelView,    (sections & HUDIds.Section.BossPanel)      != 0);
        SetActiveSafe(systemNoticesRoot,(sections & HUDIds.Section.SystemNotices)  != 0);
        bool showMinimap = (sections & HUDIds.Section.Minimap) != 0;
        SetActiveSafe(minimapPanel,     showMinimap);
        SetActiveSafe(covenantPanel,    (sections & HUDIds.Section.CovenantPanel)  != 0);

        // TopBar의 맵 아이콘은 미니맵과 한 세트다. 로비엔 맵이 없는데 TopBar만 켜져
        // 아이콘이 홀로 남아 있었다 — 미니맵 섹션에 묶어 함께 켜고 끈다.
        SetMapIconVisible(showMinimap);

        if (showCombat)
            EnsureCombatPanelVisible();
    }

    private Transform _mapIcon;
    private bool      _mapIconSearched;

    /// <summary>TopBar 맵 아이콘 표시. 참조가 없어 이름으로 1회만 찾아 캐시한다(GoldIcon과 동일 패턴).</summary>
    private void SetMapIconVisible(bool on)
    {
        if (!_mapIconSearched)
        {
            _mapIconSearched = true;
            _mapIcon = FindChildRecursive(transform, "HUD_Map");
        }
        if (_mapIcon != null && _mapIcon.gameObject.activeSelf != on)
            _mapIcon.gameObject.SetActive(on);
    }

    public void EnsureCombatPanelVisible()
    {
        SetActiveSafe(combatPanel, true);

        var panelCombat = FindChildRecursive(transform, "Panel_Combat");
        if (panelCombat != null && !panelCombat.gameObject.activeSelf)
            panelCombat.gameObject.SetActive(true);
    }

    /// <summary>닉네임 표시. 로비(BindLobby)에서만 호출되므로, 값이 들어올 때만 켠다.
    /// (인게임에선 호출되지 않아 프리팹 기본값 "Nickname" 플레이스홀더가 남던 문제 해소)</summary>
    public void SetNickname(string nickname)
    {
        if (nicknameText == null) return;

        bool has = !string.IsNullOrWhiteSpace(nickname);
        nicknameText.text = has ? nickname : string.Empty;
        if (nicknameText.gameObject.activeSelf != has)
            nicknameText.gameObject.SetActive(has);
    }

    public void SetGold(int gold)
    {
        EnsureCurrencyRow();
        if (goldText != null) goldText.text = gold.ToString();

        if (gold > 0) _goldSeen = true;
        ApplyCurrencyVisibility();
    }

    public void SetEnhanceMaterial(int amount)
    {
        EnsureCurrencyRow();
        if (_enhanceMatText != null) _enhanceMatText.text = amount.ToString();

        if (amount > 0) _enhanceMatSeen = true;
        ApplyCurrencyVisibility();
    }

    public void SetRuneOre(int amount)
    {
        EnsureCurrencyRow();
        if (_runeOreText != null) _runeOreText.text = amount.ToString();

        if (amount > 0) _runeOreSeen = true;
        ApplyCurrencyVisibility();
    }

    /// <summary>
    /// 재화 칸 표시 갱신 — <b>얻은 적 있는 재화만</b> 보인다.
    /// 아직 못 얻은 재화 칸을 미리 띄우면 "쓸 수 없는 자원"이 화면을 차지해 정보 밀도만 올린다.
    /// </summary>
    private void ApplyCurrencyVisibility()
    {
        // pill이 만들어지지 않은 스킨(스프라이트 미지정)에서는 goldText 자체를 켜고 끈다.
        var goldTarget = _goldSlot != null ? _goldSlot : (goldText != null ? goldText.gameObject : null);
        if (goldTarget != null && goldTarget.activeSelf != _goldSeen) goldTarget.SetActive(_goldSeen);

        if (_enhanceMatSlot != null && _enhanceMatSlot.activeSelf != _enhanceMatSeen)
            _enhanceMatSlot.SetActive(_enhanceMatSeen);
        if (_runeOreSlot != null && _runeOreSlot.activeSelf != _runeOreSeen)
            _runeOreSlot.SetActive(_runeOreSeen);
    }

    /// <summary>
    /// 재화 표시를 "아직 아무것도 못 얻음"으로 되돌린다. 로비 진입·새 런 시작에서 호출.
    /// (런 재화는 런 스코프라 로비에서 지난 런의 골드가 남아 보이면 안 된다.)
    /// </summary>
    public void ResetCurrencyVisibility()
    {
        _goldSeen = _enhanceMatSeen = _runeOreSeen = false;
        if (goldText != null) goldText.text = "0";
        if (_enhanceMatText != null) _enhanceMatText.text = "0";
        if (_runeOreText != null) _runeOreText.text = "0";
        ApplyCurrencyVisibility();
    }

    private static void SetActiveSafe(MonoBehaviour comp, bool on)
    {
        if (comp == null) return;
        SetActiveSafe(comp.gameObject, on);
    }

    private static void SetActiveSafe(GameObject go, bool on)
    {
        if (go == null) return;
        if (go.activeSelf == on) return;
        go.SetActive(on);
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;

        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindChildRecursive(root.GetChild(i), name);
            if (found != null) return found;
        }

        return null;
    }
}
