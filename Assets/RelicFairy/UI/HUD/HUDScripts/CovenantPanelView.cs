using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 런 중 보유 중인 서약 목록을 HUD에 표시하는 패널.
/// CovenantHandler.OnCovenantListChanged 이벤트를 받아 Refresh한다.
/// </summary>
public sealed class CovenantPanelView : MonoBehaviour
{
    [SerializeField] private UI_CovenantSlot[] _slots;

    private GridLayoutGroup _grid;        // 09-27: 세로 줄 → 격자(4칸은 2열)
    private TMP_Text        _countLabel;  // 「서약 n/N」

    [Header("서약 슬롯 스킨 (선택 — 미지정 시 기존 외형 유지)")]
    [SerializeField] private Sprite covenantFrameSprite;   // 서약 테두리
    [SerializeField] private Sprite covenantInnerSprite;   // 서약 내부

    [Header("서약 슬롯 레이아웃 (스킨 시)")]
    // 237×156 = 1.52:1 → 테두리 아트(1217×798) 비율과 정확히 일치(왜곡 0).
    // 목업 148보다 8px 여유를 둬 원인/결과 2줄이 답답하지 않게 한다.
    [Tooltip("슬롯 크기. 테두리 아트 1217×798 = 1.52:1 과 같은 비율로 두면 왜곡이 없다.")]
    [SerializeField] private Vector2 slotSize    = new Vector2(237f, 156f);
    [Tooltip("슬롯 세로 간격")]
    [SerializeField] private float   slotSpacing = 8f;
    [Tooltip("제목/설명 사이 간격")]
    [SerializeField] private float contentSpacing = 6f;

    // 톤 v2 글자(10-11 비교 리서치 A §3-4) — DNF 16 / 15(제목은 가짜 굵게까지) → 제목 Noto Medium 17 · 설명 Regular 15 · 칸 수 Medium 15.
    // 크기는 코드가 정한다: @UIRoot 프리팹에 옛 titleFontSize 16이 직렬화돼 있어 필드 기본값을 바꿔도 프리팹 값이 이겼다
    // → 직렬화 필드를 걷고 상수로 둔다(프리팹에 남은 옛 키는 더 읽히지 않는다).
    // 설명 상자 209×88에서 가장 긴 조합(「심장박동 × 결계」 4줄)이 DNF 15px로 82%를 썼다 — Noto는 줄높이가 1.448(DNF 1.286)라
    // 같은 15px로 약 92%를 쓴다. 넘치는 조합만 자동 축소(×0.75)가 받는다.
    private const float TitleFontSize = UITheme.Thin.Body;    // 17
    private const float DescFontSize  = UITheme.Thin.Small;   // 15
    private const float CountFontSize = UITheme.Thin.Small;   // 15
    private static readonly Color CountInk = UITheme.Thin.InkA(0.80f);

    // 10-15 사용자 「HUD 서약 팝업 — 디자인 · 가독성 · 위치」: 미니맵 아래(왼쪽 위)로 옮기고(HudView.LayoutCovenant) 왼쪽 정렬 목록으로.
    // 옛 칸(237 · 가운데 정렬 · 자동 축소)은 5절이면 설명이 13.9px까지 줄었고 긴 결과 줄이 테 장식에 닿았다(실측 CovHud_5).
    private const float SentenceWidth = 280f;    // 서약서 칸 폭 — 결과 줄이 15px 그대로(줄바꿈은 들여쓴 채로)
    private static readonly Vector4 ContentPad = new(24f, 14f, 24f, 16f);   // L,B,R,T — 테 안쪽 이중선(가장자리에서 약 19px) 안으로(옛 14 · 12는 선에 닿았다)
    private const float TitleH = 26f;            // 제목 한 줄(17 × 1.5) — 「서약서 ·」 머리를 떼어 한 줄에 든다

    // 테 · 판 그림은 9분할 여백이 0이라 서약서 높이가 바뀌면 모서리 장식이 늘어나 찌그러졌다 → 실행 중에 여백을 준 사본(그림 파일 · 메타는 그대로).
    private const float FrameBorderPx   = 130f;  // 테 그림(1217×798) 모서리 장식이 다 들어가는 폭(그림 픽셀)
    private const float PlateBorderPx   = 70f;   // 판 그림(1068×761) 깎인 모서리(≈60)
    private const float SkinDesignWidth = 237f;  // 그림을 이 폭에 맞췄을 때의 모서리 크기를 지킨다(옛 칸 폭)
    private static readonly Color FrameTint = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.6f);   // 흰 선 → 얇은 금선(톤 v2)
    private static Sprite s_frameSliced, s_plateSliced;

    private static CovenantPanelView s_shown;    // 켜진 서약 칸 — 출구 · 보스 나침반 배지가 비킨다
    private static readonly Vector3[] s_corners = new Vector3[4];

    private bool HasSkin => covenantFrameSprite != null || covenantInnerSprite != null;
    private bool _thinFontsApplied;   // 톤 v2 글꼴로 한 번 다 발랐는가(패널은 앱 부트 때 생겨 Awake엔 글꼴이 아직 없다)

    private void Awake()
    {
        if (_slots == null || _slots.Length == 0)
            _slots = GetComponentsInChildren<UI_CovenantSlot>(true);

        LayoutSlots();     // 프리팹이 슬롯 4개를 같은 자리(높이 40)에 겹쳐놔서 아트가 뭉개짐 → 재배치
        ApplySlotSkin();
    }

    /// <summary>
    /// 슬롯을 아트 비율 크기로 키운다.
    /// ⚠️ Panel_Covenant엔 <b>VerticalLayoutGroup + ContentSizeFitter</b>가, 슬롯엔 HorizontalLayoutGroup이
    /// 붙어 있어 RectTransform을 직접 지정하면 매 레이아웃 패스에 덮어써진다(= 이전 시도가 무효였던 이유).
    /// 따라서 <b>LayoutElement로 '원하는 크기'를 알려주는</b> 방식으로 처리한다.
    /// 스킨 미지정 시엔 기존 레이아웃 유지(회귀 0).
    /// </summary>
    private void LayoutSlots()
    {
        if (!HasSkin || _slots == null || _slots.Length == 0) return;

        // 패널 = 왼쪽 위 기준 · 아래로 자람(10-15 미니맵 아래). 자리는 HudView.LayoutCovenant가 매 프레임 맞춘다.
        // (예전: 왼쪽 아래 기준으로 버프 · 패시브 줄 위에 쌓였다 — 패시브가 늘면 밀려 올라가고 발동 알림 줄과 붙었다)
        var prt = (RectTransform)transform;
        prt.anchorMin = new Vector2(0f, 1f);
        prt.anchorMax = new Vector2(0f, 1f);
        prt.pivot     = new Vector2(0f, 1f);
        prt.anchoredPosition = new Vector2(16f, -190f);
        prt.sizeDelta = new Vector2(slotSize.x, prt.sizeDelta.y);   // 높이는 ContentSizeFitter가 결정

        // 09-27: 세로 줄(VLG) → 격자. 「서약 슬롯 +1」로 4칸이 되면 세로로 쌓은 윗변(y 993)이 미니맵(아랫변 928)을 덮었다 —
        // 4칸은 2열 2줄로 놓는다(윗변 ≈ 670). 1~3칸은 지금처럼 한 줄 세로. 순서도 같다(위 → 아래, 왼 → 오).
        var pad = new RectOffset(4, 4, 4, 4);
        if (TryGetComponent<VerticalLayoutGroup>(out var vlg)) { pad = vlg.padding; DestroyImmediate(vlg); }
        if (!TryGetComponent(out _grid)) _grid = gameObject.AddComponent<GridLayoutGroup>();
        _grid.padding         = pad;
        _grid.cellSize        = slotSize;
        _grid.spacing         = new Vector2(slotSpacing, slotSpacing);
        _grid.startCorner     = GridLayoutGroup.Corner.UpperLeft;
        _grid.startAxis       = GridLayoutGroup.Axis.Horizontal;
        _grid.childAlignment  = TextAnchor.UpperLeft;
        _grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        _grid.constraintCount = 1;
        if (TryGetComponent<ContentSizeFitter>(out var fitter))
        {
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        }
        EnsureCountLabel();

        foreach (var slot in _slots)
        {
            if (slot == null) continue;

            // 슬롯 크기 = 테두리 아트 비율
            if (!slot.TryGetComponent<LayoutElement>(out var le))
                le = slot.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth  = slotSize.x;
            le.preferredHeight = slotSize.y;
            le.minWidth        = slotSize.x;
            le.minHeight       = slotSize.y;

            BuildSlotContent(slot);
        }
    }

    /// <summary>
    /// 슬롯 내용을 목업 구조로 재구성: <b>[제목] 위 / [설명] 아래 (세로)</b>.
    /// 프리팹 원본은 [뱃지|아이콘|이름] 가로(HorizontalLayoutGroup)라 제목이 좁게 줄바꿈됐다.
    /// → 가로 그룹을 세로 그룹으로 바꾸고, 아이콘·뱃지는 숨긴다(목업에 없음. 단계는 이름의 [실버]/[골드]로 표기됨).
    /// </summary>
    private void BuildSlotContent(UI_CovenantSlot slot)
    {
        var slotT = slot.transform;

        // 부모(CovenantPanelView)의 Awake가 자식(UI_CovenantSlot)의 Awake보다 먼저 돌 수 있어
        // 슬롯 내부 참조가 아직 null일 수 있다 → 먼저 강제 해석.
        slot.EnsureRefs();

        // 가로 → 세로 레이아웃 교체.
        // ⚠️ Destroy()는 프레임 끝까지 '지연'되는데 Unity의 LayoutGroup은 [DisallowMultipleComponent]다.
        //    → HLG가 남아있는 상태로 AddComponent<VerticalLayoutGroup>()을 하면 null이 반환된다.
        //    반드시 DestroyImmediate로 먼저 제거해야 한다.
        if (slotT.TryGetComponent<HorizontalLayoutGroup>(out var hlg))
            DestroyImmediate(hlg);

        if (!slotT.TryGetComponent<VerticalLayoutGroup>(out var vlg))
            vlg = slot.gameObject.AddComponent<VerticalLayoutGroup>();
        if (vlg == null) return;   // 방어: 레이아웃 그룹 확보 실패 시 스킵(기존 외형 유지)

        vlg.padding = new RectOffset((int)ContentPad.x, (int)ContentPad.z, (int)ContentPad.w, (int)ContentPad.y);
        vlg.spacing                = contentSpacing;
        vlg.childAlignment         = TextAnchor.UpperLeft;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        // 목업엔 아이콘/단계 바가 없다 → 숨김(참조는 유지)
        if (slot.Icon       != null) slot.Icon.gameObject.SetActive(false);
        if (slot.StageBadge != null) slot.StageBadge.gameObject.SetActive(false);

        // 제목 — 왼쪽 한 줄(10-15 · 굵기는 글꼴 Medium · 가짜 굵게 없음). 「연격  ●●○」처럼 진척이 붙어도 한 줄에 들고, 넘치면 자동 축소.
        var name = slot.NameText;
        if (name != null)
        {
            name.alignment = TextAlignmentOptions.MidlineLeft;
            name.fontStyle = FontStyles.Normal;
            name.enableWordWrapping = false;
            name.fontSize    = TitleFontSize;                 // 기준값 — 자동축소가 안 걸려도 이 크기로 나온다
            name.enableAutoSizing = true;
            name.fontSizeMin = TitleFontSize * 0.75f;
            name.fontSizeMax = TitleFontSize;
            SetTextLayout(name.gameObject, TitleH, 0f);
            name.transform.SetSiblingIndex(0);
        }

        // 설명 — 없으면 생성(제목 스타일 복제), 가운데
        var descT = slotT.Find("DescText");
        TMP_Text desc;
        if (descT != null) desc = descT.GetComponent<TMP_Text>();
        else
        {
            var go = new GameObject("DescText", typeof(RectTransform));
            go.transform.SetParent(slotT, false);
            desc = go.AddComponent<TextMeshProUGUI>();
            if (name != null) desc.font = name.font;
        }
        // 꺼진 슬롯(@UIRoot 프리팹에서 비활성) 안의 TMP는 Awake 전이라 원근 카메라로 여겨 높이를 0.1배로 잰다 —
        // 설명이 4 px로 나와 틀이 제목 줄만 덮고 아래 줄이 틀 밖으로 나갔다(10-16 사용자 「텍스트에 맞는 배경이 적용되지 않는다」).
        desc.isOrthographic = true;
        // 설명 — 왼쪽 정렬 목록(10-15). 15px 고정(자동 축소 없음 — 칸이 내용만큼 자란다) · 원인 줄은 한 단 옅게(UI_CovenantSlot.BindSentence)
        desc.alignment = TextAlignmentOptions.TopLeft;
        desc.color     = UITheme.Thin.Ink;
        desc.enableWordWrapping = true;
        desc.enableAutoSizing = false;
        desc.fontSize    = DescFontSize;
        desc.fontSizeMin = DescFontSize;
        desc.fontSizeMax = DescFontSize;
        desc.lineSpacing = -5f;                               // 줄바꿈된 한 결과 줄은 붙여서
        desc.paragraphSpacing = 4f;                           // 결과 줄끼리는 살짝 떼어 한 줄 = 한 절이 보이게
        SetTextLayout(desc.gameObject, 0f, 1f);               // 제목을 뺀 남은 높이를 전부 차지(TMP에게 묻지 않음)
        desc.transform.SetSiblingIndex(1);

        slot.AttachDescText(desc);
    }

    /// <summary>
    /// 톤 v2 글꼴(제목 Medium 17 · 설명 Regular 15 · 칸 수 Medium 15) — 패널은 앱 부트 때 생겨 Awake엔 글꼴(Addressables)이 아직 없다.
    /// Refresh마다 보고, 글꼴이 올라온 뒤 한 번만 바른다.
    /// </summary>
    private void EnsureThinFonts()
    {
        if (_thinFontsApplied || UISkin.ThemeFonts == null || _slots == null) return;
        _thinFontsApplied = true;
        foreach (var slot in _slots)
            slot?.StyleThin(TitleFontSize, UITheme.Thin.Ink, DescFontSize, UITheme.Thin.Ink);
        if (_countLabel != null) UITheme.StyleThinText(_countLabel, UIWeight.Medium, CountFontSize, CountInk, shadow: true);
    }

    /// <summary>
    /// 텍스트 자식의 높이를 <b>LayoutElement로 확정</b>한다.
    /// ⚠️ preferredHeight를 비워두면 LayoutGroup이 TMP에게 높이를 묻고, TMP 자동축소는 rect 높이를 알아야
    ///    글자 크기를 정하므로 <b>서로를 기다리는 순환</b>이 생긴다. 이때 TMP는 fontSizeMax를 무시하고
    ///    기본 fontSize로 그려버린다(설명이 제목보다 몇 배 커지던 원인).
    ///    → 높이를 먼저 못 박아 순환을 끊는다.
    /// </summary>
    private static void SetTextLayout(GameObject go, float preferredH, float flexH)
    {
        if (!go.TryGetComponent<LayoutElement>(out var le))
            le = go.AddComponent<LayoutElement>();
        le.preferredHeight = preferredH;
        le.flexibleHeight  = flexH;
    }

    /// <summary>
    /// 남는 세로 공간의 배분 비중만 지정한다(preferredHeight는 건드리지 않음).
    /// 설명 텍스트처럼 '높이를 못 박지 않고 남는 만큼 차지해야 하는' 요소에 쓴다.
    /// </summary>
    private static void SetFlexible(GameObject go, float flexH)
    {
        if (!go.TryGetComponent<LayoutElement>(out var le))
            le = go.AddComponent<LayoutElement>();
        le.flexibleHeight = flexH;
    }

    /// <summary>LayoutGroup이 구동하는 자식의 크기를 LayoutElement로 지정.</summary>
    private static void SetChildLayout(Transform parent, string childName,
                                       float prefW, float prefH, float flexW)
    {
        var t = parent.Find(childName);
        if (t == null) return;

        if (!t.TryGetComponent<LayoutElement>(out var le))
            le = t.gameObject.AddComponent<LayoutElement>();

        if (prefW > 0f) { le.preferredWidth  = prefW; le.minWidth  = prefW; }
        if (prefH > 0f) { le.preferredHeight = prefH; le.minHeight = prefH; }
        le.flexibleWidth = flexW;
    }

    /// <summary>각 서약 슬롯에 내부 플레이트(뒤) + 테두리(앞) 아트를 얹는다.</summary>
    private void ApplySlotSkin()
    {
        if (_slots == null) return;
        if (covenantFrameSprite == null && covenantInnerSprite == null) return;

        var plate = Sliced(covenantInnerSprite, PlateBorderPx, ref s_plateSliced);
        var frame = Sliced(covenantFrameSprite, FrameBorderPx, ref s_frameSliced);
        foreach (var slot in _slots)
        {
            if (slot == null) continue;
            // 판 두 겹 — 판 그림이 반투명(알파 0.70)이라 밝은 벽 · 횃불 위에서 글자가 묻혔다. 같은 모양을 한 겹 더 깔아 0.91로(10-15).
            AddSkinLayer(slot.transform, "SkinBg2",   plate, false, Color.white, PpuMul(covenantInnerSprite));
            AddSkinLayer(slot.transform, "SkinBg",    plate, false, Color.white, PpuMul(covenantInnerSprite));
            AddSkinLayer(slot.transform, "SkinFrame", frame, true,  FrameTint,   PpuMul(covenantFrameSprite));
        }
    }

    /// <summary>그림 사본에 9분할 여백(그림 픽셀)을 준다 — 높이가 바뀌어도 모서리 장식은 그대로, 가운데 띠만 늘어난다.</summary>
    private static Sprite Sliced(Sprite src, float borderPx, ref Sprite cache)
    {
        if (src == null) return null;
        if (cache != null) return cache;
        cache = Sprite.Create(src.texture, src.rect, new Vector2(0.5f, 0.5f), src.pixelsPerUnit, 0, SpriteMeshType.FullRect,
                              new Vector4(borderPx, borderPx, borderPx, borderPx));
        cache.name = src.name + "_Sliced";
        return cache;
    }

    /// <summary>모서리를 옛 칸 폭(237)에 맞춘 크기로 — 그림 폭 / 237.</summary>
    private static float PpuMul(Sprite src) => src != null ? src.rect.width / SkinDesignWidth : 1f;

    /// <summary>켜진 서약서 칸의 화면 사각형(스크린 좌표 · 아래에서 위) — 출구 · 보스 나침반 배지가 이 위에 얹히지 않게 비킨다(10-15).</summary>
    public static bool TryGetScreenRect(out Rect r)
    {
        r = default;
        var p = s_shown;
        if (p == null || !p.isActiveAndEnabled || p._slots == null || p._slots.Length == 0 || p._slots[0] == null
            || !p._slots[0].gameObject.activeInHierarchy) return false;
        var rt = (RectTransform)p.transform;
        rt.GetWorldCorners(s_corners);
        var canvas = p.GetComponentInParent<Canvas>()?.rootCanvas;
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, s_corners[0]);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, s_corners[2]);
        r = Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        return r.width > 1f && r.height > 1f;
    }

    private static void AddSkinLayer(Transform parent, string name, Sprite sprite, bool onTop, Color color, float ppuMul)
    {
        if (parent == null || sprite == null) return;
        if (parent.Find(name) != null) return;

        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // ⚠️ 슬롯엔 HorizontalLayoutGroup이 붙어 있어, 그냥 두면 이 스킨 레이어가 '행 아이템'으로
        //    끼어들어 얇게 찌그러진다. 레이아웃에서 제외해야 배경/테두리로 깔린다.
        go.AddComponent<LayoutElement>().ignoreLayout = true;

        var img = go.GetComponent<Image>();
        img.sprite        = sprite;
        img.type          = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = ppuMul;
        img.color         = color;
        img.raycastTarget = false;

        if (onTop) rt.SetAsLastSibling();
        else       rt.SetAsFirstSibling();
    }

    // ── 서약서(문장) — 좌하단 서약 칸 4장 → 서약서 한 장(10-02 설계서 §4) ──
    private const float ChainStagger  = 0.08f; // 연쇄 표식이 차례로 켜지는 간격
    private const float ChainPitchUp  = 0.12f; // 절마다 소리가 한 음씩 올라간다
    // 등급 소리(10-07 U2 · 개편기획 ① — 실버 맑은 종 · 골드 겹종 · 루비 낮은 울림 + 짧은 화면 효과). 흔들림은 늘리지 않는다.
    private const float GoldEchoSec    = 0.07f;
    private const float GoldEchoVolume = 0.7f;   // 메아리는 본음의 0.7배(예전 0.32 / 0.45)
    private const float RubyPulsePeak  = 0.22f;
    private const float RubyPulseSec   = 0.25f;
    // 출시용 발동 글자 — 이 서약서의 첫 발동 · 끝 절까지 이어진 연쇄(너무 잦지 않게 간격)
    private const float ChainTextGap   = 2.5f;
    private static readonly Color TriggerTextColor = new(1f, 0.84f, 0.45f, 1f);
    private bool  _firstFireShown;
    private float _lastChainTextAt = -99f;
    private CovenantSentence _sentence;
    // Refresh가 마지막으로 넘긴 서약서 — 컷신 · 보스 페이지 전환으로 꺼졌다 켜지면 다시 듣는다.
    // 다시 묶는 곳이 Refresh뿐이라 서약이 바뀔 때까지 연쇄 표식 · 등급 소리 · 「발동」 글자가 멎었다(10-09 출시 점검).
    private CovenantSentence _lastSentence;
    private float _chainBase;                  // 지금 연쇄의 첫 표식 시각(같은 순간 들어온 절들을 차례로 펼친다)
    private int   _chainLast = -1;

    private void OnEnable()
    {
        s_shown = this;
        EnsureThinFonts();
        BindSentenceEvents(_lastSentence);
        // 꺼진 동안(로비 · 컷신 · 대기방) 잰 높이는 틀릴 수 있다 — 켜질 때 지금 문장으로 칸을 다시 맞춘다(10-16)
        if (_lastSentence != null) FitSentence(_lastSentence);
    }
    private void OnDisable()
    {
        if (s_shown == this) s_shown = null;
        BindSentenceEvents(null);
    }

    /// <summary>서약서 한 장의 칸 높이 = 여백 + 제목 한 줄 + 간격 + 설명(실제 줄바꿈까지 잰 높이) — 1절이 휑하지 않고 5절이 줄어들지 않는다(10-15).
    /// Refresh와 켜질 때(OnEnable) 같이 부른다.</summary>
    private void FitSentence(CovenantSentence sentence)
    {
        if (_slots == null || _slots.Length == 0 || _slots[0] == null) return;
        float descH = _slots[0].BindSentence(sentence, SentenceWidth - ContentPad.x - ContentPad.z);
        float h = Mathf.Ceil(ContentPad.w + TitleH + contentSpacing + descH + ContentPad.y);
        if (_grid != null) { _grid.constraintCount = 1; _grid.cellSize = new Vector2(SentenceWidth, h); }
        if (_slots[0].TryGetComponent<LayoutElement>(out var le))
        { le.preferredWidth = SentenceWidth; le.minWidth = SentenceWidth; le.preferredHeight = h; le.minHeight = h; }
    }

    /// <summary>문장이 바뀌면(이어 쓰기 · 고쳐 쓰기 = 새 인스턴스) 절 사건을 다시 듣는다.</summary>
    private void BindSentenceEvents(CovenantSentence s)
    {
        if (_sentence == s) return;
        if (_sentence != null) _sentence.ClauseFired -= OnClauseFired;
        _sentence = s;
        if (_sentence != null) _sentence.ClauseFired += OnClauseFired;
    }

    private void OnClauseFired(int index)
    {
        if (_slots == null || _slots.Length == 0 || _slots[0] == null) return;
        float now = Time.unscaledTime;
        // 한 연쇄 안(앞 표식보다 뒤 절) = 같은 박자에 이어 붙임 · 아니면 새 연쇄
        if (index <= _chainLast || now - _chainBase > 0.6f) _chainBase = now - index * ChainStagger;
        _chainLast = index;
        float delay = Mathf.Max(0f, _chainBase + index * ChainStagger - now);
        _slots[0].FlashPip(index, delay);

        // 처음 끝 절까지 닿은 순간 — 런당 한 번, 그 맹세의 모양을 한 마디로(설계서 §4)
        var handler = GameRunBootstrapper.Instance?.Run?.CovenantHandler;
        if (_sentence != null && _sentence.ResultCount >= 2 && index == _sentence.ResultCount - 1
            && handler != null && !handler.ChainEndSaid)
        {
            handler.ChainEndSaid = true;
            CovenantVoice.Say(CovenantVoice.ChainEnd);
        }
        // 10-19 사용자 「서약 조건이 완료될 때마다 나오는 사운드가 오히려 방해」 — 소리는 글자가 뜨는 두 순간(첫 발동 · 끝 절 연쇄)에만.
        // 절마다의 확인은 서약서 칸의 점 깜빡임(FlashPip)이 맡는다.
        if (ShowTriggerText(index)) PlayClauseSound(index);
    }

    /// <summary>등급마다 다른 소리 — 실버 = 높고 맑게 한 번 · 골드 = 겹종(한 번 더 높게) · 루비 = 낮게 + 짧은 화면 효과. 절이 이어질수록 한 음씩 오른다.</summary>
    private void PlayClauseSound(int index)
    {
        var tier = _sentence != null && index < _sentence.ResultCount ? _sentence.ResultTier(index) : CovenantTier.Silver;
        float step = ChainPitchUp * index;
        switch (tier)
        {
            case CovenantTier.Ruby:
                Managers.Sound?.PlayUiEvent(SoundEvent.ClauseRuby, 1f + step);
                VolumePulseService.Pulse(RubyPulsePeak, RubyPulseSec);
                break;
            case CovenantTier.Gold:
                Managers.Sound?.PlayUiEvent(SoundEvent.ClauseGold, 1f + step);
                GoldEchoAsync(1.25f + step).Forget();
                break;
            default:
                Managers.Sound?.PlayUiEvent(SoundEvent.ClauseSilver, 1f + step);
                break;
        }
    }

    private async UniTaskVoid GoldEchoAsync(float pitch)
    {
        try { await UniTask.Delay(TimeSpan.FromSeconds(GoldEchoSec), DelayType.UnscaledDeltaTime, cancellationToken: destroyCancellationToken); }
        catch (OperationCanceledException) { return; }
        Managers.Sound?.PlayUiEvent(SoundEvent.ClauseGold, pitch, GoldEchoVolume);   // 메아리 = 같은 파일을 높게 · 작게
    }

    /// <summary>
    /// 출시용 발동 글자(개편기획 ① — 개발 빌드 전용 토스트가 걷힌 뒤 출시 빌드에선 발동이 글자로 보이지 않았다):
    /// 이 서약서의 첫 발동 「서약 발동」 · 끝 절까지 이어진 연쇄 「연쇄 ×N」(<see cref="ChainTextGap"/>초 간격). 플레이어 머리 위 얇은 글자.
    /// </summary>
    private bool ShowTriggerText(int index)
    {
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null || _sentence == null) return false;
        string text = null;
        if (!_firstFireShown) { _firstFireShown = true; text = "서약 발동"; }
        else if (_sentence.ResultCount >= 2 && index == _sentence.ResultCount - 1
                 && Time.unscaledTime - _lastChainTextAt >= ChainTextGap)
        {
            _lastChainTextAt = Time.unscaledTime;
            text = "연쇄 ×" + _sentence.ResultCount;
        }
        if (text != null) RelicFloatText.Show(player.transform.position, text, TriggerTextColor);   // 발밑 — 높이는 RelicFloatText가 더한다
        return text != null;
    }

    public void Refresh(IReadOnlyList<CovenantBase> covenants)
    {
        if (_slots == null) return;
        EnsureThinFonts();

        // 서약서가 있으면 한 장으로 — 칸 개념(서약 n/N)은 끝났다
        CovenantSentence sentence = null;
        if (covenants != null)
            foreach (var c in covenants) if (c is CovenantSentence s) { sentence = s; break; }
        // 새 서약서(이어 쓰기 · 고쳐 쓰기)의 첫 발동을 다시 알린다 — 꺼졌다 켜진 것만으론 되풀이하지 않는다(10-09 출시 점검)
        if (sentence != _lastSentence) _firstFireShown = false;
        _lastSentence = sentence;
        BindSentenceEvents(sentence);
        if (sentence != null)
        {
            if (_countLabel != null)
            {
                _countLabel.gameObject.SetActive(true);
                _countLabel.text = $"서약서 {sentence.ResultCount}절";
            }
            FitSentence(sentence);
            for (int i = 1; i < _slots.Length; i++) _slots[i]?.SetEmpty();
            return;
        }
        if (_grid != null) _grid.cellSize = slotSize;
        if (_slots[0] != null && _slots[0].TryGetComponent<LayoutElement>(out var le0))
        { le0.preferredWidth = slotSize.x; le0.minWidth = slotSize.x; le0.preferredHeight = slotSize.y; le0.minHeight = slotSize.y; }

        int count = covenants != null ? covenants.Count : 0;
        if (_grid != null) _grid.constraintCount = count > 3 ? 2 : 1;
        if (_countLabel != null)
        {
            _countLabel.gameObject.SetActive(count > 0);
            _countLabel.text = $"서약 {count}/{Mathf.Max(count, CovenantHandler.Capacity)}";   // 상한 전에 맺어 둔 세이브 복원은 넘칠 수 있다
        }

        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] == null) continue;

            if (i < covenants.Count)
                _slots[i].Bind(covenants[i]);
            else
                _slots[i].SetEmpty();
        }
    }

    /// <summary>「서약 n/N」 — 패널 위에 떠서 격자 배치에 끼지 않는다. 한도는 제단 「서약 슬롯 +1」로 3 → 4.</summary>
    private void EnsureCountLabel()
    {
        if (_countLabel != null) return;
        var go = new GameObject("CountLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(LayoutElement));
        go.transform.SetParent(transform, false);
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot     = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(6f, 2f);
        rt.sizeDelta = new Vector2(200f, 22f);
        _countLabel = go.AddComponent<TextMeshProUGUI>();
        _countLabel.fontSize = CountFontSize;
        _countLabel.fontStyle = FontStyles.Normal;
        _countLabel.alignment = TextAlignmentOptions.BottomLeft;
        _countLabel.color = CountInk;   // 글꼴(Medium)은 EnsureThinFonts가 글꼴이 올라온 뒤 바른다
        _countLabel.raycastTarget = false;
        TMPOutlineHelper.ApplySoftShadow(_countLabel);
        go.SetActive(false);
    }

    public void Clear()
    {
        _lastSentence = null;   // 런이 끝난 서약서를 다시 켜질 때 붙잡지 않게(10-09 출시 점검)
        BindSentenceEvents(null);
        if (_countLabel != null) _countLabel.gameObject.SetActive(false);
        if (_slots == null) return;
        foreach (var slot in _slots)
            slot?.SetEmpty();
    }
}
