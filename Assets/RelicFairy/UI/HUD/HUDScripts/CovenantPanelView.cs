using System.Collections.Generic;
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

    [Header("서약 슬롯 스킨 (선택 — 미지정 시 기존 외형 유지)")]
    [SerializeField] private Sprite covenantFrameSprite;   // 서약 테두리
    [SerializeField] private Sprite covenantInnerSprite;   // 서약 내부

    [Header("서약 슬롯 레이아웃 (스킨 시)")]
    [Tooltip("패널 위치 — 화면 좌하단 기준. 서약이 늘면 위로 쌓인다(버프/무기 영역 침범 방지).")]
    [SerializeField] private Vector2 panelOffset = new Vector2(101f, 337f);
    // 237×156 = 1.52:1 → 테두리 아트(1217×798) 비율과 정확히 일치(왜곡 0).
    // 목업 148보다 8px 여유를 둬 원인/결과 2줄이 답답하지 않게 한다.
    [Tooltip("슬롯 크기. 테두리 아트 1217×798 = 1.52:1 과 같은 비율로 두면 왜곡이 없다.")]
    [SerializeField] private Vector2 slotSize    = new Vector2(237f, 156f);
    [Tooltip("슬롯 세로 간격")]
    [SerializeField] private float   slotSpacing = 8f;
    [Tooltip("슬롯 내용(뱃지/아이콘/이름)을 프레임 장식 안쪽으로 들여넣는 여백 (L,B,R,T)")]
    [SerializeField] private Vector4 slotContentPadding = new Vector4(14f, 12f, 14f, 12f);
    [Tooltip("제목/설명 사이 간격")]
    [SerializeField] private float contentSpacing = 6f;
    [Tooltip("제목(서약 이름) 폰트 크기 — 한 줄에 들어가게")]
    [SerializeField] private float titleFontSize = 14f;
    [Tooltip("설명 폰트 크기")]
    [SerializeField] private float descFontSize = 11f;

    private bool HasSkin => covenantFrameSprite != null || covenantInnerSprite != null;

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

        // 패널 자체를 좌하단 기준으로 고정한다.
        // 프리팹은 anchor(0,0.6)·pivot(0,1)이라 서약이 늘수록 '아래로' 자라 버프/무기 영역을 침범했다.
        // 좌하단 피벗으로 바꿔 위로 쌓이게 한다(목업 순서: 무기 → 버프 → 서약).
        var prt = (RectTransform)transform;
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.zero;
        prt.pivot     = Vector2.zero;
        prt.anchoredPosition = panelOffset;
        prt.sizeDelta = new Vector2(slotSize.x, prt.sizeDelta.y);   // 높이는 ContentSizeFitter가 결정

        // 세로 배치: 간격만 조정하고 크기는 LayoutElement가 결정하게 한다.
        if (TryGetComponent<VerticalLayoutGroup>(out var vlg))
        {
            vlg.spacing = slotSpacing;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;
            vlg.childForceExpandWidth  = false;
            vlg.childForceExpandHeight = false;
        }

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

        vlg.padding = new RectOffset(
            (int)slotContentPadding.x, (int)slotContentPadding.z,
            (int)slotContentPadding.w, (int)slotContentPadding.y);
        vlg.spacing                = contentSpacing;
        vlg.childAlignment         = TextAnchor.MiddleCenter;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        // 목업엔 아이콘/단계 바가 없다 → 숨김(참조는 유지)
        if (slot.Icon       != null) slot.Icon.gameObject.SetActive(false);
        if (slot.StageBadge != null) slot.StageBadge.gameObject.SetActive(false);

        // 제목 — 굵게, 가운데. 이름이 길면(조립 서약: "원인[티어] × 결과[티어]") 줄바꿈 + 자동 축소로 잘리지 않게.
        var name = slot.NameText;
        if (name != null)
        {
            name.alignment = TextAlignmentOptions.Center;
            name.fontStyle = FontStyles.Bold;
            name.enableWordWrapping = true;
            name.fontSize    = titleFontSize;                 // 기준값 — 자동축소가 안 걸려도 이 크기로 나온다
            name.enableAutoSizing = true;
            name.fontSizeMin = titleFontSize * 0.75f;
            name.fontSizeMax = titleFontSize;
            SetTextLayout(name.gameObject, titleFontSize * 2.4f, 0f);   // 두 줄까지 허용한 고정 높이
            name.transform.SetSiblingIndex(0);
        }

        // 설명 — 없으면 생성(제목 스타일 복제), 이탤릭·가운데
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
        desc.alignment = TextAlignmentOptions.Center;
        desc.color     = new Color(0.88f, 0.86f, 0.80f, 1f);
        desc.enableWordWrapping = true;
        // 원인/결과 2줄 + 화살표라 문구가 길어질 수 있다 → 칸을 넘기지 않도록 자동 축소(하한까지 줄바꿈으로 흡수).
        desc.fontSize    = descFontSize;                      // 기준값 — 자동축소가 안 걸려도 이 크기로 나온다
        desc.enableAutoSizing = true;
        desc.fontSizeMin = descFontSize * 0.75f;
        desc.fontSizeMax = descFontSize;
        desc.lineSpacing = -5f;                               // 원인/↓/결과 3줄이 세로로 뜨지 않게 살짝 조임
        SetTextLayout(desc.gameObject, 0f, 1f);               // 제목을 뺀 남은 높이를 전부 차지(TMP에게 묻지 않음)
        desc.transform.SetSiblingIndex(1);
        // fontStyle(인용체 여부)은 UI_CovenantSlot.Bind가 내용에 따라 정한다.

        slot.AttachDescText(desc);
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

        foreach (var slot in _slots)
        {
            if (slot == null) continue;
            AddSkinLayer(slot.transform, "SkinBg",    covenantInnerSprite, false);
            AddSkinLayer(slot.transform, "SkinFrame", covenantFrameSprite, true);
        }
    }

    private static void AddSkinLayer(Transform parent, string name, Sprite sprite, bool onTop)
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
        img.color         = Color.white;
        img.raycastTarget = false;

        if (onTop) rt.SetAsLastSibling();
        else       rt.SetAsFirstSibling();
    }

    public void Refresh(IReadOnlyList<CovenantBase> covenants)
    {
        if (_slots == null) return;

        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] == null) continue;

            if (i < covenants.Count)
                _slots[i].Bind(covenants[i]);
            else
                _slots[i].SetEmpty();
        }
    }

    public void Clear()
    {
        if (_slots == null) return;
        foreach (var slot in _slots)
            slot?.SetEmpty();
    }
}
