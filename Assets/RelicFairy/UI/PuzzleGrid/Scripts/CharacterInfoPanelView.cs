using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
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
    private const float BOTTOM_BAR_PX = 0f;

    // 좌측 열 공통 좌우 여백 — 아래 속성 시너지(MerlinRuneSynergyStatusView.Gutter)와 같은 값(09-28, 섹션마다 0~25px로 제각각이었다).
    public  const float Gutter  = 12f;
    private const float NameX   = 64f;   // 초상(52) + 12 — 초상이 없으면 0으로 당긴다
    // 섹션 머리띠 — 속성 시너지 머리띠와 같은 모양(색·글자). 같은 열의 두 제목이 서로 다른 띠라 조각나 보였다.
    public static readonly Color SectionBand = new(0.14f, 0.18f, 0.26f, 0.88f);
    public static readonly Color SectionInk  = new(0.75f, 0.90f, 1.00f, 1f);
    // 머리띠 높이 — 활성 효과(≈21px)와 속성 시너지(≈44px)가 비율 앵커라 서로 달랐다(09-28). 둘 다 이 고정 높이.
    public const float SectionBandH = 32f;

    // ── Private fields ────────────────────────────────────────────
    private Image      _portraitImg;
    private GameObject _portraitBg;
    private RectTransform _nameRT, _subRT;
    private TMP_Text   _nameText;
    private RectTransform _hpFill;
    private Image      _hpFillImg;
    private TMP_Text   _hpText;
    private TMP_Text   _txtAtk, _txtDef, _txtLuck, _txtSpd, _txtCdr, _txtMaxHp;
    private ScrollRect _effectScroll;
    private Transform  _effectContent;
    private readonly List<GameObject> _effectRows = new();

    // ── Chip Tooltip ──────────────────────────────────────────────
    private GameObject _chipTooltip;
    private TMP_Text   _chipTooltipBadge;
    private TMP_Text   _chipTooltipBody;
    private Image      _chipAccentBar;

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
        {
            MerlinRuneBridge.Instance.OnSynergyActivated += OnSynergyActivated;
            MerlinRuneBridge.Instance.OnSynergiesUpdated += OnEffectsChanged;
        }
        if (_run?.CovenantHandler != null)
            _run.CovenantHandler.OnCovenantListChanged += OnEffectsChanged;
    }

    private void OnDisable()
    {
        if (_stats != null) _stats.OnChanged -= OnStatsChanged;
        if (MerlinRuneBridge.Instance != null)
        {
            MerlinRuneBridge.Instance.OnSynergyActivated -= OnSynergyActivated;
            MerlinRuneBridge.Instance.OnSynergiesUpdated -= OnEffectsChanged;
        }
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
        crt.offsetMin = new Vector2(Gutter, BOTTOM_BAR_PX + 6f);
        crt.offsetMax = new Vector2(-Gutter, -6f);

        var ct = contentGO.transform;
        BuildHeader(ct);
        BuildHPSection(ct);
        BuildStatsGrid(ct);
        MakeDivider(ct, 0.390f);
        BuildEffectsSection(ct);
        BuildChipTooltip();
    }

    // ── Header (초상화 + 이름) ──────────────────────────────────

    private void BuildHeader(Transform parent)
    {
        var go = Go("Header");
        go.transform.SetParent(parent, false);
        Anc(go.GetComponent<RectTransform>(), new Vector2(0f, 0.880f), Vector2.one);
        // (머리 바탕 판은 뺐다 — 열 전체가 한 장의 판으로 읽히게, 09-28)

        // 초상화 배경 (좌측 정사각형)
        var portraitBgGO = Go("PortraitBG");
        portraitBgGO.transform.SetParent(go.transform, false);
        var pbrt = portraitBgGO.GetComponent<RectTransform>();
        pbrt.anchorMin        = new Vector2(0f, 0f);
        pbrt.anchorMax        = new Vector2(0f, 1f);
        pbrt.pivot            = new Vector2(0f, 0.5f);
        pbrt.sizeDelta        = new Vector2(52f, -8f);
        pbrt.anchoredPosition = Vector2.zero;
        portraitBgGO.AddComponent<Image>().color = new Color(0.14f, 0.17f, 0.26f, 1f);
        _portraitBg = portraitBgGO;

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
        _nameText = Txt(go.transform, "CharName", "—", 22f, C_HDR_TXT, bold: true);   // 09-25: 18 → 22(패널 제목)
        var nrt = _nameText.GetComponent<RectTransform>();
        nrt.anchorMin = new Vector2(0f, 0.45f);
        nrt.anchorMax = new Vector2(1f, 1f);
        nrt.offsetMin = new Vector2(NameX, 0f);
        nrt.offsetMax = Vector2.zero;
        _nameText.alignment = TextAlignmentOptions.MidlineLeft;
        _nameRT = nrt;

        // 부제 (클래스 등 보조 정보)
        var subTxt = Txt(go.transform, "SubInfo", "캐릭터 정보", 16f, C_SUB_TXT);
        var srt2 = subTxt.GetComponent<RectTransform>();
        srt2.anchorMin = new Vector2(0f, 0f);
        srt2.anchorMax = new Vector2(1f, 0.52f);
        srt2.offsetMin = new Vector2(NameX, 2f);
        srt2.offsetMax = Vector2.zero;
        subTxt.alignment = TextAlignmentOptions.MidlineLeft;
        _subRT = srt2;
    }

    // ── HP Section ────────────────────────────────────────────────

    private void BuildHPSection(Transform parent)
    {
        var sec = Go("HPSection");
        sec.transform.SetParent(parent, false);
        Anc(sec.GetComponent<RectTransform>(),
            new Vector2(0f, 0.785f), new Vector2(1f, 0.878f));
        // (섹션 바탕 판은 뺐다 — 머리·HP·스탯이 한 장의 판 위에 놓인다, 09-28)

        // "HP" 레이블
        var lbl = Txt(sec.transform, "HPLabel", "HP", 16f, C_LBL);
        Anc(lbl.GetComponent<RectTransform>(),
            new Vector2(0f, 0.55f), new Vector2(0.18f, 1f));
        lbl.alignment = TextAlignmentOptions.MidlineLeft;

        // HP 수치 텍스트
        _hpText = Txt(sec.transform, "HPValue", "— / —", 16f, C_VAL, bold: true);
        Anc(_hpText.GetComponent<RectTransform>(),
            new Vector2(0.18f, 0.52f), new Vector2(1f, 1f));
        _hpText.alignment = TextAlignmentOptions.MidlineRight;

        // 바 트랙 (어두운 배경)
        var track = Go("BarTrack");
        track.transform.SetParent(sec.transform, false);
        Anc(track.GetComponent<RectTransform>(),
            new Vector2(0f, 0.14f), new Vector2(1f, 0.42f));   // 스탯 카드와 좌우 끝을 맞춘다
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

        const float L = 0f, R = 0.485f, RL = 0.515f, RR = 1f;   // 좌우 끝 = 열 여백(HP 막대·머리띠와 같은 선)
        const float R1T = 0.97f, R1B = 0.68f;
        const float R2T = 0.65f, R2B = 0.36f;
        const float R3T = 0.33f, R3B = 0.03f;

        _txtAtk   = StatCard(grid.transform, "공격력",   C_ATK,   new(L,  R1B), new(R,  R1T));
        _txtDef   = StatCard(grid.transform, "방어력",   C_DEF,   new(RL, R1B), new(RR, R1T));
        _txtLuck  = StatCard(grid.transform, "행운",     C_LUCK,  new(L,  R2B), new(R,  R2T));
        _txtSpd   = StatCard(grid.transform, "이동속도", C_SPD,   new(RL, R2B), new(RR, R2T));
        _txtCdr   = StatCard(grid.transform, "스킬 쿨감", C_CDR,   new(L,  R3B), new(R,  R3T));   // 약어(CDR) 대신 한글
        _txtMaxHp = StatCard(grid.transform, "최대 HP",  C_MAXHP, new(RL, R3B), new(RR, R3T));
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
        var lblT = Txt(card.transform, "Label", label, 16f, C_LBL);
        Anc(lblT.GetComponent<RectTransform>(),
            new Vector2(0.14f, 0.52f), new Vector2(0.98f, 0.98f));
        lblT.alignment = TextAlignmentOptions.MidlineLeft;

        // 수치 (하단, 크고 밝게)
        // 값이 카드의 주인공 — 라벨(16)보다 확실히 크게. 칸 높이(≈26px)에 22px 한 줄이 든다.
        var valT = Txt(card.transform, "Value", "—", 22f, C_VAL, bold: true);
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
        // 스탯 판 아래(0.392)에서 SectionBandH만큼 — 속성 시너지 머리띠와 같은 높이(예전 비율 0.046 ≈ 21px).
        var hrt = hdr.GetComponent<RectTransform>();
        Anc(hrt, new Vector2(0f, 0.392f), new Vector2(1f, 0.392f));
        hrt.offsetMin = new Vector2(0f, -SectionBandH);
        hdr.AddComponent<Image>().color = SectionBand;   // 속성 시너지 머리띠와 같은 띠
        var hdrTxt = Txt(hdr.transform, "Title", "◆ 활성 효과", 17f, SectionInk, bold: true);   // 속성 시너지 제목(17 굵게)과 같은 급
        Anc(hdrTxt.GetComponent<RectTransform>(),
            new Vector2(0.04f, 0f), new Vector2(1f, 1f));
        hdrTxt.alignment = TextAlignmentOptions.MidlineLeft;

        // ScrollRect 뷰포트
        var scrollGO = Go("EffectsScroll");
        scrollGO.transform.SetParent(parent, false);
        var srt = scrollGO.GetComponent<RectTransform>();
        Anc(srt, Vector2.zero, new Vector2(1f, 0.392f));
        srt.offsetMax = new Vector2(0f, -SectionBandH);
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
        vlg.spacing              = 4f;
        vlg.padding              = new RectOffset(0, 0, 6, 4);
        vlg.childControlWidth    = true;
        // 높이도 그룹이 잡아야 칩의 LayoutElement(34)가 먹는다 — false면 새 오브젝트 기본 100px이 그대로 남아
        // 서약 칩 하나가 100px 금색 상자가 됐다(09-28 사용자 캡처).
        vlg.childControlHeight   = true;
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
        // 런의 CharacterData는 공용 기본 SO(이름 "PlayerCharacter")다 — 누구로 싸우는지는 장착 유물이 정한다.
        var relic = _run?.Player?.RelicClass;
        if (_nameText != null)
            _nameText.text = relic != null && !string.IsNullOrEmpty(relic.DisplayName) ? relic.DisplayName
                           : charData != null ? charData.characterName : "—";

        var portrait = relic != null && relic.Portrait != null ? relic.Portrait : charData?.portrait;
        if (_portraitImg != null && portrait != null)
        {
            _portraitImg.sprite = portrait;
            _portraitImg.color  = Color.white;
        }
        else if (_portraitImg != null)
        {
            _portraitImg.sprite = null;
            _portraitImg.color  = new Color(0.25f, 0.30f, 0.45f, 0.6f);
        }

        // 초상이 없으면 빈 네모를 띄우지 않는다 — 이름을 왼쪽 끝으로 당긴다(09-28: 빈 칸이 고장처럼 보였다).
        bool hasPortrait = portrait != null;
        if (_portraitBg != null) _portraitBg.SetActive(hasPortrait);
        float nx = hasPortrait ? NameX : 0f;
        if (_nameRT != null) _nameRT.offsetMin = new Vector2(nx, _nameRT.offsetMin.y);
        if (_subRT  != null) _subRT.offsetMin  = new Vector2(nx, _subRT.offsetMin.y);
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

        // 현재 count 크기 기반으로 활성 시너지만 표시 (제거 시 실시간 반영)
        var zoneCounts = MerlinRuneBridge.Instance?.GetZoneOccupiedCounts();
        if (zoneCounts != null && Managers.RuneData != null)
        {
            foreach (var zoneId in ElementDef.Order)
            {
                if (!zoneCounts.TryGetValue(zoneId, out int count) || count <= 0) continue;

                var entries = Managers.RuneData.GetZoneSynergies(zoneId);
                if (entries == null) continue;

                var sorted = new List<RuneSynergyEntry>(entries);
                sorted.Sort((a, b) => a.threshold.CompareTo(b.threshold));

                foreach (var e in sorted)
                {
                    if (count < e.threshold) break;
                    Color accent = GetZoneAccent(zoneId);
                    string badge = e.trigger switch
                    {
                        "Always"  => "항상",
                        "OnHit"   => "피격시",
                        "OnKill"  => "처치시",
                        "OnLowHp" => "체력↓",
                        "OnUse"   => "사용시",
                        _         => e.trigger,
                    };
                    // 코드 이름(FireEmber 등)이 그대로 보였다 — 차트 설명의 「N단계 이름」을 쓴다(없을 때만 코드명).
                    string elemName = ElementDef.GetById(zoneId)?.Name ?? zoneId;
                    string body = $"{elemName} {TierTitle(e)}";
                    _effectRows.Add(MakeChip(body, badge, accent, e.description));
                    any = true;
                }
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
                _effectRows.Add(MakeChip(c.DisplayName, badge, C_COVENANT, c.DisplayName));
                any = true;
            }
        }

        if (!any)
            _effectRows.Add(MakeEmptyChip());

        // 콘텐츠 추가 후 스크롤 상단으로 리셋
        if (_effectScroll != null)
            _effectScroll.verticalNormalizedPosition = 1f;
    }

    // ── Chip Tooltip ──────────────────────────────────────────────

    private void BuildChipTooltip()
    {
        // UI_GridPanel 루트 하위에 생성 → 모든 패널 위에 렌더링
        Transform tooltipParent = UI_GridPanel.Instance != null
            ? UI_GridPanel.Instance.transform
            : transform;

        _chipTooltip = Go("ChipTooltip");
        _chipTooltip.transform.SetParent(tooltipParent, false);

        var rt = _chipTooltip.GetComponent<RectTransform>();
        // anchorMin=anchorMax=(0.5, 0.5) → anchoredPosition = UI_GridPanel 중앙 기준 픽셀 오프셋
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(320f, 120f);

        // 그림자 레이어 (뒤쪽에 약간 크게)
        var shadowGO = Go("Shadow");
        shadowGO.transform.SetParent(_chipTooltip.transform, false);
        var shadowRT = shadowGO.GetComponent<RectTransform>();
        shadowRT.anchorMin = Vector2.zero;
        shadowRT.anchorMax = Vector2.one;
        shadowRT.offsetMin = new Vector2(-4f, -4f);
        shadowRT.offsetMax = new Vector2(4f, 4f);
        shadowGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.60f);

        // 메인 배경
        var bg = _chipTooltip.AddComponent<Image>();
        bg.color = new Color(0.13f, 0.16f, 0.27f, 0.98f);

        // 테두리 (두껍고 밝게)
        var ol = _chipTooltip.AddComponent<Outline>();
        ol.effectColor    = new Color(0.45f, 0.70f, 1.00f, 0.90f);
        ol.effectDistance = new Vector2(2f, -2f);

        // 상단 accent 컬러 스트립 (존 색상으로 동적 변경)
        var accentBarGO = Go("AccentBar");
        accentBarGO.transform.SetParent(_chipTooltip.transform, false);
        var accentBarRT = accentBarGO.GetComponent<RectTransform>();
        accentBarRT.anchorMin        = new Vector2(0f, 1f);
        accentBarRT.anchorMax        = Vector2.one;
        accentBarRT.pivot            = new Vector2(0.5f, 1f);
        accentBarRT.sizeDelta        = new Vector2(0f, 5f);
        accentBarRT.anchoredPosition = Vector2.zero;
        _chipAccentBar = accentBarGO.AddComponent<Image>();
        _chipAccentBar.raycastTarget = false;

        // 배지 (상단 절반)
        _chipTooltipBadge = Txt(_chipTooltip.transform, "Badge", "", 16f,
            new Color(0.80f, 0.92f, 1f, 1f), bold: true);
        var brt = _chipTooltipBadge.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0f, 0.52f);
        brt.anchorMax = Vector2.one;
        brt.offsetMin = new Vector2(12f, 0f);
        brt.offsetMax = new Vector2(-12f, -6f);
        _chipTooltipBadge.alignment     = TextAlignmentOptions.MidlineLeft;
        _chipTooltipBadge.raycastTarget = false;

        // 구분선
        var divGO = Go("Divider");
        divGO.transform.SetParent(_chipTooltip.transform, false);
        var divRT = divGO.GetComponent<RectTransform>();
        divRT.anchorMin = new Vector2(0.03f, 0.50f);
        divRT.anchorMax = new Vector2(0.97f, 0.52f);
        divRT.offsetMin = divRT.offsetMax = Vector2.zero;
        divGO.AddComponent<Image>().color = new Color(0.35f, 0.48f, 0.75f, 0.50f);

        // 본문 텍스트 (하단 절반)
        _chipTooltipBody = Txt(_chipTooltip.transform, "Body", "", 16f,
            new Color(0.96f, 0.98f, 1f, 1f));
        var trt = _chipTooltipBody.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = new Vector2(1f, 0.50f);
        trt.offsetMin = new Vector2(12f, 6f);
        trt.offsetMax = new Vector2(-12f, 0f);
        _chipTooltipBody.alignment         = TextAlignmentOptions.MidlineLeft;
        _chipTooltipBody.textWrappingMode = TextWrappingModes.Normal;
        _chipTooltipBody.raycastTarget      = false;

        _chipTooltip.SetActive(false);
    }

    private void ShowChipTooltip(string badge, string body, Color accent, RectTransform chipRT)
    {
        if (_chipTooltip == null) return;

        // UI_GridPanel 루트에 붙어있지 않으면 이동 (z-order 보장)
        var rootPanel = UI_GridPanel.Instance;
        if (rootPanel != null && _chipTooltip.transform.parent != rootPanel.transform)
            _chipTooltip.transform.SetParent(rootPanel.transform, false);

        var rt = _chipTooltip.GetComponent<RectTransform>();

        // 칩 우측 중앙 월드 좌표 → UI_GridPanel 로컬 좌표 (anchorMin=anchorMax=0.5 기준)
        Vector3 chipWorldRight = chipRT.TransformPoint(
            new Vector2(chipRT.rect.xMax, chipRT.rect.center.y));
        var panelRT = rootPanel != null
            ? rootPanel.GetComponent<RectTransform>()
            : GetComponent<RectTransform>();
        Vector2 localPos = panelRT.InverseTransformPoint(chipWorldRight);
        rt.anchoredPosition = new Vector2(localPos.x + 8f, localPos.y);

        if (_chipTooltipBadge != null)
        {
            _chipTooltipBadge.text  = badge;
            _chipTooltipBadge.color = accent;
        }
        if (_chipTooltipBody != null)
            _chipTooltipBody.text = body;
        if (_chipAccentBar != null)
            _chipAccentBar.color = new Color(accent.r, accent.g, accent.b, 0.85f);

        _chipTooltip.SetActive(true);
        _chipTooltip.transform.SetAsLastSibling();
    }

    private void HideChipTooltip() => _chipTooltip?.SetActive(false);

    // ── Chip Builders ─────────────────────────────────────────────

    private const float ChipH = 34f;   // 16px 한 줄 + 위아래 여백

    /// <summary>차트 설명 "N단계 이름: 설명"의 앞머리("N단계 이름"). 설명이 없으면 코드명.</summary>
    private static string TierTitle(RuneSynergyEntry e)
    {
        if (string.IsNullOrEmpty(e.description)) return e.effect_type;
        int colon = e.description.IndexOf(':');
        return (colon > 0 ? e.description.Substring(0, colon) : e.description).Trim();
    }

    private GameObject MakeChip(string body, string badge, Color accent, string detail = null)
    {
        var go = Go("Chip");
        go.transform.SetParent(_effectContent, false);
        go.AddComponent<LayoutElement>().preferredHeight = ChipH;
        go.AddComponent<Image>().color = new Color(
            accent.r * 0.10f, accent.g * 0.10f, accent.b * 0.10f, 0.75f);

        // 좌측 악센트 스트립
        var strip = Go("Strip");
        strip.transform.SetParent(go.transform, false);
        var srt = strip.GetComponent<RectTransform>();
        srt.anchorMin        = Vector2.zero;
        srt.anchorMax        = new Vector2(0f, 1f);
        srt.sizeDelta        = new Vector2(3f, 0f);
        srt.anchoredPosition = new Vector2(1.5f, 0f);
        strip.AddComponent<Image>().color = accent;

        // 배지 (우측) — 글자 태그. 예전의 색 블록(62px)은 칩보다 무거워 「기본」 같은 부가 정보가 주인공처럼 보였다.
        var badgeGO = Go("Badge");
        badgeGO.transform.SetParent(go.transform, false);
        var brt = badgeGO.GetComponent<RectTransform>();
        brt.anchorMin        = new Vector2(1f, 0.15f);
        brt.anchorMax        = new Vector2(1f, 0.85f);
        brt.pivot            = new Vector2(1f, 0.5f);
        brt.sizeDelta        = new Vector2(62f, 0f);
        brt.anchoredPosition = new Vector2(-3f, 0f);
        badgeGO.AddComponent<Image>().color = Color.clear;
        var badgeLblGO = Go("BadgeLabel");
        badgeLblGO.transform.SetParent(badgeGO.transform, false);
        var blrt = badgeLblGO.GetComponent<RectTransform>();
        blrt.anchorMin = Vector2.zero;
        blrt.anchorMax = Vector2.one;
        blrt.offsetMin = blrt.offsetMax = Vector2.zero;
        var badgeTxt = badgeLblGO.AddComponent<TextMeshProUGUI>();
        badgeTxt.text          = badge;
        badgeTxt.fontSize      = 16f;   // 12px는 하한 아래였다
        badgeTxt.color         = new Color(accent.r, accent.g, accent.b, 0.8f);
        badgeTxt.alignment     = TextAlignmentOptions.MidlineRight;
        badgeTxt.raycastTarget = false;

        // 본문 텍스트
        var bodyGO = Go("Body");
        bodyGO.transform.SetParent(go.transform, false);
        Anc(bodyGO.GetComponent<RectTransform>(),
            new Vector2(0.04f, 0f), new Vector2(0.80f, 1f));
        var bodyTxt = bodyGO.AddComponent<TextMeshProUGUI>();
        bodyTxt.text = body;
        bodyTxt.fontSize = 16f;
        bodyTxt.color = new Color(
            Mathf.Clamp01(accent.r * 0.65f + 0.35f),
            Mathf.Clamp01(accent.g * 0.65f + 0.35f),
            Mathf.Clamp01(accent.b * 0.65f + 0.35f), 1f);
        bodyTxt.alignment          = TextAlignmentOptions.MidlineLeft;
        bodyTxt.raycastTarget      = false;
        bodyTxt.textWrappingMode = TextWrappingModes.NoWrap;
        bodyTxt.overflowMode     = TextOverflowModes.Ellipsis;   // 긴 서약 이름(「연주[실버] × 마지막 숨결[실버]」)은 태그 앞에서 말줄임 — 전문은 툴팁

        // 마우스 호버 → 툴팁 표시
        var et = go.AddComponent<EventTrigger>();
        var chipRT = go.GetComponent<RectTransform>();
        string cb = badge, cy = string.IsNullOrEmpty(detail) ? body : detail;   // 툴팁은 설명 전문
        Color ca = accent;

        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => ShowChipTooltip(cb, cy, ca, chipRT));
        et.triggers.Add(enter);

        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => HideChipTooltip());
        et.triggers.Add(exit);

        return go;
    }

    private GameObject MakeEmptyChip()
    {
        var go = Go("NoEffect");
        go.transform.SetParent(_effectContent, false);
        go.AddComponent<LayoutElement>().preferredHeight = ChipH;
        var txt = go.AddComponent<TextMeshProUGUI>();
        txt.text          = "룬을 놓으면 켜진 효과가 여기 쌓입니다";   // 「효과 없음」 대신 할 일을 한 줄로
        txt.fontSize      = 16f;                                   // 글자 하한(12px·α0.7은 판에 묻혔다)
        txt.color         = new Color(0.55f, 0.60f, 0.72f, 0.85f);
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

    private static Color GetZoneAccent(string zoneId) =>
        ElementDef.IdColor(zoneId, new Color(0.60f, 0.70f, 0.90f, 1f));

    private static GameObject Go(string name) => new(name, typeof(RectTransform));

    private static void Anc(RectTransform rt, Vector2 min, Vector2 max)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
