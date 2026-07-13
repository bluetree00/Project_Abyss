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
    [Tooltip("슬롯 크기. 테두리 아트가 1217×798 ≈ 1.53:1. 제목이 한 줄에 들어가도록 넉넉히.")]
    [SerializeField] private Vector2 slotSize    = new Vector2(330f, 216f);
    [Tooltip("슬롯 세로 간격")]
    [SerializeField] private float   slotSpacing = 8f;
    [Tooltip("슬롯 내용(뱃지/아이콘/이름)을 프레임 장식 안쪽으로 들여넣는 여백 (L,B,R,T)")]
    [SerializeField] private Vector4 slotContentPadding = new Vector4(18f, 16f, 18f, 16f);
    [Tooltip("제목/설명 사이 간격")]
    [SerializeField] private float contentSpacing = 8f;
    [Tooltip("제목(서약 이름) 폰트 크기 — 한 줄에 들어가게")]
    [SerializeField] private float titleFontSize = 18f;
    [Tooltip("설명 폰트 크기")]
    [SerializeField] private float descFontSize = 14f;

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

        // 제목 — 굵게, 가운데, 한 줄
        var name = slot.NameText;
        if (name != null)
        {
            name.alignment = TextAlignmentOptions.Center;
            name.fontStyle = FontStyles.Bold;
            name.fontSize  = titleFontSize;
            name.enableWordWrapping = true;
            SetFlexible(name.gameObject, 0f);
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
        desc.fontStyle = FontStyles.Italic;
        desc.fontSize  = descFontSize;
        desc.color     = new Color(0.88f, 0.86f, 0.80f, 1f);
        desc.enableWordWrapping = true;
        SetFlexible(desc.gameObject, 1f);   // 남는 세로 공간을 설명이 차지
        desc.transform.SetSiblingIndex(1);

        slot.AttachDescText(desc);
    }

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
