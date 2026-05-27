using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI_GridPanel 좌측 캐릭터 정보 패널.
/// 초상화 헤더 + HP 게이지 + 2×3 스탯 카드(6번째=MaxHP) + 활성 효과 ScrollRect.
/// 하단 150px은 BottomBar와 높이 맞춤을 위한 여백.
/// UI_GridPanel.Awake()에서 코드로 생성된다.
/// </summary>
public sealed class CharacterInfoPanelView : MonoBehaviour
{
    // ── Panel Colors ──────────────────────────────────────────────
    private static readonly Color C_BG         = new(0.05f, 0.06f, 0.09f, 0.98f);
    private static readonly Color C_HEADER_BG  = new(0.07f, 0.09f, 0.14f, 1f);
    private static readonly Color C_SECTION_BG = new(0.07f, 0.08f, 0.12f, 1f);
    private static readonly Color C_CARD_BG    = new(0.09f, 0.10f, 0.15f, 0.85f);
    private static readonly Color C_DIVIDER    = new(0.18f, 0.22f, 0.32f, 0.8f);

    // ── Stat Accent Colors ────────────────────────────────────────
    private static readonly Color C_ATK   = new(1.00f, 0.50f, 0.20f, 1f);
    private static readonly Color C_DEF   = new(0.30f, 0.65f, 1.00f, 1f);
    private static readonly Color C_LUCK  = new(1.00f, 0.85f, 0.20f, 1f);
    private static readonly Color C_SPD   = new(0.25f, 0.90f, 0.50f, 1f);
    private static readonly Color C_CDR   = new(0.70f, 0.40f, 1.00f, 1f);
    private static readonly Color C_HP    = new(0.90f, 0.25f, 0.25f, 1f);
    private static readonly Color C_MAXHP = new(0.65f, 0.20f, 0.20f, 1f);

    // ── Text Colors ───────────────────────────────────────────────
    private static readonly Color C_LBL     = new(0.55f, 0.60f, 0.75f, 1f);
    private static readonly Color C_VAL     = new(0.95f, 0.97f, 1.00f, 1f);
    private static readonly Color C_HDR_TXT = new(0.75f, 0.85f, 1.00f, 1f);
    private static readonly Color C_SUB_TXT = new(0.45f, 0.52f, 0.68f, 1f);

    // ── Effect Chip Colors ────────────────────────────────────────
    private static readonly Color C_SYN_ALWAYS  = new(0.25f, 0.95f, 0.55f, 1f);
    private static readonly Color C_SYN_ONHIT   = new(0.25f, 0.85f, 1.00f, 1f);
    private static readonly Color C_SYN_LOWERHP = new(0.95f, 0.40f, 0.25f, 1f);
    private static readonly Color C_COVENANT    = new(1.00f, 0.80f, 0.25f, 1f);

    // BottomBar 높이(px). ApplyRightPanelLayout과 동일값 유지.
    private const float BOTTOM_BAR_PX = 150f;

    // ── Private fields ────────────────────────────────────────────
    private Image      _portraitImg;
    private TMP_Text   _nameText;
    private RectTransform _hpFill;
    private Image      _hpFillImg;
    private TMP_Text   _hpText;
    private TMP_Text   _txtAtk, _txtDef, _txtLuck, _txtSpd, _txtCdr, _txtMaxHp;
    private ScrollRect _effectScroll;
    private Transform  _effectContent;
    private readonly List<GameObject> _effectRows = new();

    private PlayerRuntimeStats _stats;
    private GameRunSession     _run;

    // ── Factory ───────────────────────────────────────────────────

    public static CharacterInfoPanelView Create(Transform parent)
    {
        var go = new GameObject("CharacterInfoPanel", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        // 전체 높이 span — BottomBar 여백은 내부 컨텐츠 레이아웃으로 처리
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = new Vector2(0.30f, 1f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var view = go.AddComponent<CharacterInfoPanelView>();
        view.BuildUI();
        return view;
    }

    // ── Lifecycle ─────────────────────────────────────────────────

    private void OnEnable()
    {
        if (_stats != null) _stats.OnChanged += OnStatsChanged;
        if (MerlinRuneBridge.Instance != null)
            MerlinRuneBridge.Instance.OnSynergyActivated += OnSynergyActivated;
        if (_run?.CovenantHandler != null)
            _run.CovenantHandler.OnCovenantListChanged += OnEffectsChanged;
    }

    private void OnDisable()
    {
        if (_stats != null) _stats.OnChanged -= OnStatsChanged;
        if (MerlinRuneBridge.Instance != null)
            MerlinRuneBridge.Instance.OnSynergyActivated -= OnSynergyActivated;
        if (_run?.CovenantHandler != null)
            _run.CovenantHandler.OnCovenantListChanged -= OnEffectsChanged;
    }

    // ── Public API ────────────────────────────────────────────────

    public void SetContext(PlayerRuntimeStats stats, GameRunSession run)
    {
        if (_stats != null) _stats.OnChanged -= OnStatsChanged;
        if (_run?.CovenantHandler != null)
            _run.CovenantHandler.OnCovenantListChanged -= OnEffectsChanged;

        _stats = stats;
        _run   = run;

        if (isActiveAndEnabled)
        {
            if (_stats != null) _stats.OnChanged += OnStatsChanged;
            if (_run?.CovenantHandler != null)
                _run.CovenantHandler.OnCovenantListChanged += OnEffectsChanged;
        }

        RefreshHeader();
        Refresh();
    }

    public void Refresh()
    {
        RefreshStats();
        RefreshEffects();
    }

    // ── BuildUI ───────────────────────────────────────────────────

    private void BuildUI()
    {
        gameObject.AddComponent<Image>().color = C_BG;

        // 컨텐츠 영역: 전체에서 하단 BOTTOM_BAR_PX를 제외
        // anchorMin.y=0이므로 offsetMin.y로 여백 처리
        var contentGO = Go("Content");
        contentGO.transform.SetParent(transform, false);
        var crt = contentGO.GetComponent<RectTransform>();
        crt.anchorMin = Vector2.zero;
        crt.anchorMax = Vector2.one;
        crt.offsetMin = new Vector2(0f, BOTTOM_BAR_PX);
        crt.offsetMax = Vector2.zero;

        var ct = contentGO.transform;
        BuildHeader(ct);
        BuildHPSection(ct);
        BuildStatsGrid(ct);
        MakeDivider(ct, 0.390f);
        BuildEffectsSection(ct);
    }

    // ── Header (초상화 + 이름) ──────────────────────────────────

    private void BuildHeader(Transform parent)
    {
        var go = Go("Header");
        go.transform.SetParent(parent, false);
        Anc(go.GetComponent<RectTransform>(), new Vector2(0f, 0.880f), Vector2.one);
        go.AddComponent<Image>().color = C_HEADER_BG;

        // 초상화 배경 (좌측 정사각형)
        var portraitBgGO = Go("PortraitBG");
        portraitBgGO.transform.SetParent(go.transform, false);
        var pbrt = portraitBgGO.GetComponent<RectTransform>();
        pbrt.anchorMin        = new Vector2(0f, 0f);
        pbrt.anchorMax        = new Vector2(0f, 1f);
        pbrt.pivot            = new Vector2(0f, 0.5f);
        pbrt.sizeDelta        = new Vector2(52f, -8f);
        pbrt.anchoredPosition = new Vector2(8f, 0f);
        portraitBgGO.AddComponent<Image>().color = new Color(0.14f, 0.17f, 0.26f, 1f);

        // 초상화 이미지 (별도 자식 GO)
        var portraitImgGO = Go("PortraitImg");
        portraitImgGO.transform.SetParent(portraitBgGO.transform, false);
        var prt = portraitImgGO.GetComponent<RectTransform>();
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        _portraitImg = portraitImgGO.AddComponent<Image>();
        _portraitImg.color          = new Color(0.9f, 0.9f, 0.9f, 1f);
        _portraitImg.preserveAspect = true;

        // 캐릭터 이름 (굵게)
        _nameText = Txt(go.transform, "CharName", "—", 14f, C_HDR_TXT, bold: true);
        var nrt = _nameText.GetComponent<RectTransform>();
        nrt.anchorMin = new Vector2(0f, 0.45f);
        nrt.anchorMax = new Vector2(1f, 1f);
        nrt.offsetMin = new Vector2(68f, 0f);
        nrt.offsetMax = new Vector2(-6f, 0f);
        _nameText.alignment = TextAlignmentOptions.MidlineLeft;

        // 부제 (클래스 등 보조 정보)
        var subTxt = Txt(go.transform, "SubInfo", "캐릭터 정보", 9.5f, C_SUB_TXT);
        var srt2 = subTxt.GetComponent<RectTransform>();
        srt2.anchorMin = new Vector2(0f, 0f);
        srt2.anchorMax = new Vector2(1f, 0.52f);
        srt2.offsetMin = new Vector2(68f, 2f);
        srt2.offsetMax = new Vector2(-6f, 0f);
        subTxt.alignment = TextAlignmentOptions.MidlineLeft;
    }

    // ── HP Section ────────────────────────────────────────────────

    private void BuildHPSection(Transform parent)
    {
        var sec = Go("HPSection");
        sec.transform.SetParent(parent, false);
        Anc(sec.GetComponent<RectTransform>(),
            new Vector2(0f, 0.785f), new Vector2(1f, 0.878f));
        sec.AddComponent<Image>().color = C_SECTION_BG;

        // "HP" 레이블
        var lbl = Txt(sec.transform, "HPLabel", "HP", 9f, C_LBL);
        Anc(lbl.GetComponent<RectTransform>(),
            new Vector2(0.04f, 0.55f), new Vector2(0.18f, 1f));
        lbl.alignment = TextAlignmentOptions.MidlineLeft;

        // HP 수치 텍스트
        _hpText = Txt(sec.transform, "HPValue", "— / —", 10.5f, C_VAL, bold: true);
        Anc(_hpText.GetComponent<RectTransform>(),
            new Vector2(0.18f, 0.52f), new Vector2(0.97f, 1f));
        _hpText.alignment = TextAlignmentOptions.MidlineRight;

        // 바 트랙 (어두운 배경)
        var track = Go("BarTrack");
        track.transform.SetParent(sec.transform, false);
        Anc(track.GetComponent<RectTransform>(),
            new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.50f));
        track.AddComponent<Image>().color = new Color(0.15f, 0.08f, 0.08f, 0.9f);

        // 채움 바
        var fill = Go("Fill");
        fill.transform.SetParent(track.transform, false);
        _hpFill = fill.GetComponent<RectTransform>();
        _hpFill.anchorMin = Vector2.zero;
        _hpFill.anchorMax = Vector2.one;
        _hpFill.offsetMin = _hpFill.offsetMax = Vector2.zero;
        _hpFillImg = fill.AddComponent<Image>();
        _hpFillImg.color = C_SPD;
    }

    // ── Stats Grid (2×3, 6번째 = MaxHP) ──────────────────────────

    private void BuildStatsGrid(Transform parent)
    {
        var grid = Go("StatsGrid");
        grid.transform.SetParent(parent, false);
        Anc(grid.GetComponent<RectTransform>(),
            new Vector2(0f, 0.395f), new Vector2(1f, 0.782f));

        const float L = 0.02f, R = 0.49f, RL = 0.51f, RR = 0.98f;
        const float R1T = 0.97f, R1B = 0.68f;
        const float R2T = 0.65f, R2B = 0.36f;
        const float R3T = 0.33f, R3B = 0.03f;

        _txtAtk   = StatCard(grid.transform, "공격력",   C_ATK,   new(L,  R1B), new(R,  R1T));
        _txtDef   = StatCard(grid.transform, "방어력",   C_DEF,   new(RL, R1B), new(RR, R1T));
        _txtLuck  = StatCard(grid.transform, "행운",     C_LUCK,  new(L,  R2B), new(R,  R2T));
        _txtSpd   = StatCard(grid.transform, "이동속도", C_SPD,   new(RL, R2B), new(RR, R2T));
        _txtCdr   = StatCard(grid.transform, "스킬CDR",  C_CDR,   new(L,  R3B), new(R,  R3T));
        _txtMaxHp = StatCard(grid.transform, "최대HP",   C_MAXHP, new(RL, R3B), new(RR, R3T));
    }

    private TMP_Text StatCard(Transform parent, string label, Color accent,
        Vector2 aMin, Vector2 aMax)
    {
        var card = Go($"Card_{label}");
        card.transform.SetParent(parent, false);
        Anc(card.GetComponent<RectTransform>(), aMin, aMax);
        card.AddComponent<Image>().color = C_CARD_BG;

        // 좌측 악센트 스트립 (4px)
        var strip = Go("Strip");
        strip.transform.SetParent(card.transform, false);
        var srt = strip.GetComponent<RectTransform>();
        srt.anchorMin        = Vector2.zero;
        srt.anchorMax        = new Vector2(0f, 1f);
        srt.sizeDelta        = new Vector2(4f, 0f);
        srt.anchoredPosition = new Vector2(2f, 0f);
        strip.AddComponent<Image>().color = accent;

        // 레이블 (상단, 흐릿하게)
        var lblT = Txt(card.transform, "Label", label, 9f, C_LBL);
        Anc(lblT.GetComponent<RectTransform>(),
            new Vector2(0.14f, 0.52f), new Vector2(0.98f, 0.98f));
        lblT.alignment = TextAlignmentOptions.MidlineLeft;

        // 수치 (하단, 크고 밝게)
        var valT = Txt(card.transform, "Value", "—", 15f, C_VAL, bold: true);
        Anc(valT.GetComponent<RectTransform>(),
            new Vector2(0.12f, 0.04f), new Vector2(0.98f, 0.56f));
        valT.alignment = TextAlignmentOptions.MidlineLeft;

        return valT;
    }

    // ── Divider ───────────────────────────────────────────────────

    private void MakeDivider(Transform parent, float y)
    {
        var go = new GameObject("Divider", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Anc(go.GetComponent<RectTransform>(),
            new Vector2(0f, y), new Vector2(1f, y + 0.002f));
        go.GetComponent<Image>().color = C_DIVIDER;
    }

    // ── Effects Section (ScrollRect) ─────────────────────────────

    private void BuildEffectsSection(Transform parent)
    {
        // 헤더 바
        var hdr = Go("EffectsHdr");
        hdr.transform.SetParent(parent, false);
        Anc(hdr.GetComponent<RectTransform>(),
            new Vector2(0f, 0.356f), new Vector2(1f, 0.388f));
        hdr.AddComponent<Image>().color = C_SECTION_BG;
        var hdrTxt = Txt(hdr.transform, "Title", "✦ 활성 효과", 10f, C_HDR_TXT);
        Anc(hdrTxt.GetComponent<RectTransform>(),
            new Vector2(0.04f, 0f), new Vector2(1f, 1f));
        hdrTxt.alignment = TextAlignmentOptions.MidlineLeft;

        // ScrollRect 뷰포트
        var scrollGO = Go("EffectsScroll");
        scrollGO.transform.SetParent(parent, false);
        Anc(scrollGO.GetComponent<RectTransform>(),
            Vector2.zero, new Vector2(1f, 0.354f));
        scrollGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.06f);
        var mask = scrollGO.AddComponent<RectMask2D>();
        mask.padding = new Vector4(0f, 4f, 0f, 4f);

        _effectScroll = scrollGO.AddComponent<ScrollRect>();
        _effectScroll.horizontal         = false;
        _effectScroll.vertical           = true;
        _effectScroll.scrollSensitivity  = 20f;
        _effectScroll.movementType       = ScrollRect.MovementType.Clamped;
        _effectScroll.inertia            = false;

        // 콘텐츠 컨테이너
        var content = Go("Content");
        content.transform.SetParent(scrollGO.transform, false);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 1f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot     = new Vector2(0.5f, 1f);
        crt.offsetMin = crt.offsetMax = Vector2.zero;

        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment       = TextAnchor.UpperLeft;
        vlg.spacing              = 3f;
        vlg.padding              = new RectOffset(4, 4, 4, 4);
        vlg.childControlWidth    = true;
        vlg.childControlHeight   = false;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        content.AddComponent<ContentSizeFitter>().verticalFit =
            ContentSizeFitter.FitMode.PreferredSize;

        _effectScroll.content = crt;
        _effectContent = content.transform;
    }

    // ── Refresh ───────────────────────────────────────────────────

    private void RefreshHeader()
    {
        var charData = _run?.Player?.CharacterData;
        if (_nameText != null)
            _nameText.text = charData != null ? charData.characterName : "—";

        if (_portraitImg != null && charData?.portrait != null)
        {
            _portraitImg.sprite = charData.portrait;
            _portraitImg.color  = Color.white;
        }
        else if (_portraitImg != null)
        {
            _portraitImg.sprite = null;
            _portraitImg.color  = new Color(0.25f, 0.30f, 0.45f, 0.6f);
        }
    }

    private void RefreshStats()
    {
        if (_stats == null) return;

        float ratio = _stats.MaxHp > 0 ? (float)_stats.Hp / _stats.MaxHp : 0f;
        ratio = Mathf.Clamp01(ratio);

        if (_hpFill != null)
            _hpFill.anchorMax = new Vector2(ratio, 1f);
        if (_hpFillImg != null)
            _hpFillImg.color = Color.Lerp(C_HP, C_SPD, ratio);

        _hpText?.SetText($"{_stats.Hp} / {_stats.MaxHp}");

        _txtAtk?.SetText(_stats.AttackPower.ToString());
        _txtDef?.SetText(_stats.Defense.ToString());
        _txtLuck?.SetText(_stats.Luck.ToString());
        _txtSpd?.SetText($"{_stats.MoveSpeedMultiplier * 100f:F0}%");

        float cdr = _stats.SkillCooldownReduction * 100f;
        _txtCdr?.SetText(cdr > 0f ? $"-{cdr:F0}%" : "—");
        _txtMaxHp?.SetText(_stats.MaxHp.ToString());
    }

    private void RefreshEffects()
    {
        foreach (var r in _effectRows)
            if (r != null) Destroy(r);
        _effectRows.Clear();
        if (_effectContent == null) return;

        bool any = false;

        var synList = _run?.AppliedSynergies;
        if (synList != null)
        {
            foreach (var s in synList)
            {
                Color accent = s.trigger switch
                {
                    "Always"  => C_SYN_ALWAYS,
                    "OnHit"   => C_SYN_ONHIT,
                    "OnLowHp" => C_SYN_LOWERHP,
                    _         => C_SYN_ALWAYS,
                };
                string badge = s.trigger switch
                {
                    "Always"  => "항상",
                    "OnHit"   => "피격시",
                    "OnKill"  => "처치시",
                    "OnLowHp" => "체력↓",
                    "OnUse"   => "사용시",
                    _         => s.trigger,
                };
                float pct = s.value * 100f;
                string body = $"{s.effectType}  {(pct >= 0 ? "+" : "")}{pct:F0}%";
                _effectRows.Add(MakeChip(body, badge, accent));
                any = true;
            }
        }

        var covList = _run?.CovenantHandler?.Covenants;
        if (covList != null)
        {
            foreach (var c in covList)
            {
                string badge = c.Stage switch
                {
                    CovenantStage.Enhanced => "강화",
                    CovenantStage.Evolved  => "진화",
                    _                      => "기본",
                };
                _effectRows.Add(MakeChip(c.DisplayName, badge, C_COVENANT));
                any = true;
            }
        }

        if (!any)
            _effectRows.Add(MakeEmptyChip());

        // 콘텐츠 추가 후 스크롤 상단으로 리셋
        if (_effectScroll != null)
            _effectScroll.verticalNormalizedPosition = 1f;
    }

    // ── Chip Builders ─────────────────────────────────────────────

    private GameObject MakeChip(string body, string badge, Color accent)
    {
        var go = Go("Chip");
        go.transform.SetParent(_effectContent, false);
        go.AddComponent<LayoutElement>().preferredHeight = 30f;
        go.AddComponent<Image>().color = new Color(
            accent.r * 0.12f, accent.g * 0.12f, accent.b * 0.12f, 0.80f);

        // 좌측 악센트 스트립
        var strip = Go("Strip");
        strip.transform.SetParent(go.transform, false);
        var srt = strip.GetComponent<RectTransform>();
        srt.anchorMin        = Vector2.zero;
        srt.anchorMax        = new Vector2(0f, 1f);
        srt.sizeDelta        = new Vector2(4f, 0f);
        srt.anchoredPosition = new Vector2(2f, 0f);
        strip.AddComponent<Image>().color = accent;

        // 배지 (우측)
        var badgeGO = Go("Badge");
        badgeGO.transform.SetParent(go.transform, false);
        var brt = badgeGO.GetComponent<RectTransform>();
        brt.anchorMin        = new Vector2(1f, 0.15f);
        brt.anchorMax        = new Vector2(1f, 0.85f);
        brt.pivot            = new Vector2(1f, 0.5f);
        brt.sizeDelta        = new Vector2(46f, 0f);
        brt.anchoredPosition = new Vector2(-3f, 0f);
        badgeGO.AddComponent<Image>().color = new Color(
            accent.r * 0.30f, accent.g * 0.30f, accent.b * 0.30f, 0.9f);
        var badgeLblGO = Go("BadgeLabel");
        badgeLblGO.transform.SetParent(badgeGO.transform, false);
        var blrt = badgeLblGO.GetComponent<RectTransform>();
        blrt.anchorMin = Vector2.zero;
        blrt.anchorMax = Vector2.one;
        blrt.offsetMin = blrt.offsetMax = Vector2.zero;
        var badgeTxt = badgeLblGO.AddComponent<TextMeshProUGUI>();
        badgeTxt.text          = badge;
        badgeTxt.fontSize      = 8.5f;
        badgeTxt.color         = accent;
        badgeTxt.alignment     = TextAlignmentOptions.Center;
        badgeTxt.raycastTarget = false;

        // 본문 텍스트
        var bodyGO = Go("Body");
        bodyGO.transform.SetParent(go.transform, false);
        Anc(bodyGO.GetComponent<RectTransform>(),
            new Vector2(0.08f, 0f), new Vector2(0.72f, 1f));
        var bodyTxt = bodyGO.AddComponent<TextMeshProUGUI>();
        bodyTxt.text = body;
        bodyTxt.fontSize = 9.5f;
        bodyTxt.color = new Color(
            Mathf.Clamp01(accent.r * 0.65f + 0.35f),
            Mathf.Clamp01(accent.g * 0.65f + 0.35f),
            Mathf.Clamp01(accent.b * 0.65f + 0.35f), 1f);
        bodyTxt.alignment          = TextAlignmentOptions.MidlineLeft;
        bodyTxt.raycastTarget      = false;
        bodyTxt.enableWordWrapping = false;

        return go;
    }

    private GameObject MakeEmptyChip()
    {
        var go = Go("NoEffect");
        go.transform.SetParent(_effectContent, false);
        go.AddComponent<LayoutElement>().preferredHeight = 32f;
        var txt = go.AddComponent<TextMeshProUGUI>();
        txt.text          = "효과 없음";
        txt.fontSize      = 10f;
        txt.color         = new Color(0.38f, 0.42f, 0.52f, 0.7f);
        txt.alignment     = TextAlignmentOptions.Center;
        txt.raycastTarget = false;
        return go;
    }

    // ── Event Handlers ────────────────────────────────────────────

    private void OnStatsChanged()              => RefreshStats();
    private void OnSynergyActivated(string _)  => RefreshEffects();
    private void OnEffectsChanged()            => RefreshEffects();

    // ── Static Helpers ────────────────────────────────────────────

    private TMP_Text Txt(Transform parent, string name, string text,
        float size, Color color, bool bold = false)
    {
        var go = Go(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text      = text;
        t.fontSize  = size;
        t.color     = color;
        t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        t.raycastTarget = false;
        return t;
    }

    private static GameObject Go(string name) => new(name, typeof(RectTransform));

    private static void Anc(RectTransform rt, Vector2 min, Vector2 max)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
