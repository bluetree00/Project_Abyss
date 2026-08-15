using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 원거리 파츠 <b>테스트 전용</b> 패널. 획득 경제(상점·제작)가 정해지기 전에
/// 5종 효과를 곧바로 켜고 끄며 확인하기 위한 도구다.
///
/// 실제 게임 흐름(재련소 탭2)과 <b>같은 상태</b>(<see cref="RangedPartsState.Current"/>)를 만지므로,
/// 여기서 낀 파츠가 그대로 전투에 반영된다.
/// UI는 런타임 절차 생성 — 프리팹/Addressable이 필요 없다(월드 제단·챌린지 HUD와 동일 방식).
/// </summary>
public sealed class UI_RangedPartsTestPanel : MonoBehaviour
{
    private const int   SortOrder  = UISortingOrder.SystemModal;
    private const float RowH       = 46f;
    private const float PanelW     = 720f;

    private static readonly Color PanelBg = new(0.06f, 0.07f, 0.10f, 0.96f);
    private static readonly Color Gold    = new(1f, 0.82f, 0.35f);
    private static readonly Color Dim     = new(0.70f, 0.74f, 0.82f);

    private static UI_RangedPartsTestPanel _instance;

    private readonly List<TMP_Text> _rowLabels = new();
    private readonly List<string>   _rowIds    = new();
    private TMP_Text _summary;
    private TMP_Text _slotInfo;

    /// <summary>이미 열려 있으면 닫고, 아니면 연다(제단 F 토글).</summary>
    public static void Toggle()
    {
        if (_instance != null) { Destroy(_instance.gameObject); _instance = null; return; }

        var go = new GameObject("UI_RangedPartsTestPanel", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortOrder;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        _instance = go.AddComponent<UI_RangedPartsTestPanel>();
        _instance.Build();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void Build()
    {
        var root = (RectTransform)transform;

        var panel = MakeImage(root, "Panel", PanelBg);
        Anchor(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(PanelW, 560f), Vector2.zero);

        var title = MakeText(panel.transform, "Title", 26f, Gold, TextAlignmentOptions.Center);
        title.text = "원거리 파츠 테스트";
        Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(PanelW - 40f, 34f), new Vector2(0f, -22f));

        _slotInfo = MakeText(panel.transform, "SlotInfo", 16f, Dim, TextAlignmentOptions.Center);
        Anchor(_slotInfo.rectTransform, new Vector2(0.5f, 1f), new Vector2(PanelW - 40f, 24f), new Vector2(0f, -56f));

        // 파츠 행 — 정의된 전 파츠를 나열하고 장착/레벨을 직접 조작한다.
        var all = Managers.WeaponParts?.All;
        float y = -92f;
        if (all == null || all.Count == 0)
        {
            var warn = MakeText(panel.transform, "Warn", 18f, new Color(1f, 0.5f, 0.4f), TextAlignmentOptions.Center);
            warn.text = "WEAPON_PARTS_DATA 로드 실패 — 차트/폴백 JSON을 확인하세요";
            Anchor(warn.rectTransform, new Vector2(0.5f, 1f), new Vector2(PanelW - 40f, 30f), new Vector2(0f, y));
        }
        else
        {
            for (int i = 0; i < all.Count; i++)
            {
                BuildRow(panel.transform, all[i], y);
                y -= RowH;
            }
        }

        // 현재 발사 요청 미리보기 — 파츠가 실제로 무엇을 바꾸는지 수치로 보여준다.
        _summary = MakeText(panel.transform, "Summary", 16f, Dim, TextAlignmentOptions.TopLeft);
        Anchor(_summary.rectTransform, new Vector2(0.5f, 1f), new Vector2(PanelW - 60f, 150f), new Vector2(0f, y - 12f));

        MakeButton(panel.transform, "Clear", "전부 해제", new Vector2(0.5f, 0f), new Vector2(140f, 40f),
                   new Vector2(-95f, 22f), ClearAll);
        MakeButton(panel.transform, "Close", "닫기 [F]", new Vector2(0.5f, 0f), new Vector2(140f, 40f),
                   new Vector2(95f, 22f), () => Toggle());

        Refresh();
    }

    private void BuildRow(Transform parent, WeaponPartEntry def, float y)
    {
        var label = MakeText(parent, $"Row_{def.part_id}", 17f, Color.white, TextAlignmentOptions.Left);
        Anchor(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(400f, RowH - 6f), new Vector2(-130f, y));
        _rowLabels.Add(label);
        _rowIds.Add(def.part_id);

        string id = def.part_id;   // 클로저 캡처
        MakeButton(parent, $"Eq_{id}",  "장착/해제", new Vector2(0.5f, 1f), new Vector2(110f, 32f), new Vector2(140f, y), () => ToggleEquip(id));
        MakeButton(parent, $"Up_{id}",  "＋",        new Vector2(0.5f, 1f), new Vector2(44f, 32f),  new Vector2(228f, y), () => Level(id,  1));
        MakeButton(parent, $"Dn_{id}",  "－",        new Vector2(0.5f, 1f), new Vector2(44f, 32f),  new Vector2(278f, y), () => Level(id, -1));
    }

    // ── 조작 ────────────────────────────────────────────────

    private void ToggleEquip(string partId)
    {
        var st = RangedPartsState.Current;
        if (st.LevelOf(partId) > 0) st.Unequip(partId);
        else if (!st.Equip(partId))
            Debug.Log($"[파츠테스트] 슬롯이 가득 찼습니다 (상한 {RangedPartsState.MaxSlots}칸) — 하나 해제 후 장착하세요");
        Refresh();
    }

    private void Level(string partId, int delta)
    {
        var st = RangedPartsState.Current;
        if (st.LevelOf(partId) == 0) return;

        if (delta > 0) st.LevelUp(partId, delta);
        else
        {
            // 내리기는 재장착으로 처리 — LevelUp만 있는 상태에서 테스트 편의를 위한 우회.
            int cur = st.LevelOf(partId);
            st.Unequip(partId);
            st.Equip(partId, Mathf.Max(1, cur - 1));
        }
        Refresh();
    }

    private void ClearAll()
    {
        var st = RangedPartsState.Current;
        while (st.Equipped_.Count > 0) st.Unequip(st.Equipped_[0].partId);
        RangedPartsState.ClearTestCarry();
        Refresh();
    }

    // ── 표시 ────────────────────────────────────────────────

    private void Refresh()
    {
        var st   = RangedPartsState.Current;
        var data = Managers.WeaponParts;

        // 테스트 편의: 투자액을 최상위 임계 이상으로 올려 슬롯을 전부 열어둔다.
        // (실제 런에서는 재련소에서 원거리 무기에 재료를 부어야 열린다.)
        if (st.UnlockedSlots < RangedPartsState.MaxSlots)
            st.AddInvestment(RangedPartsState.ThresholdAt(RangedPartsState.MaxSlots - 1) - st.Invested);

        // 던전 진입 시 파츠 상태가 새로 만들어지므로, 지금 구성을 이월 대상으로 등록해 둔다.
        RangedPartsState.SetTestCarry(st.Equipped_, st.Invested);

        if (_slotInfo != null)
            _slotInfo.text = $"슬롯 {st.Equipped_.Count} / {st.UnlockedSlots}   ·   투자 {st.Invested}   ·   "
                           + (RangedPartsState.HasTestCarry
                              ? "<color=#7FE3FF>던전 진입 시 이월됨</color>"
                              : "<color=#9AA3B5>이월 없음</color>");

        for (int i = 0; i < _rowLabels.Count; i++)
        {
            var def = data?.GetById(_rowIds[i]);
            int lv  = st.LevelOf(_rowIds[i]);
            string nm = def != null ? def.part_name : _rowIds[i];
            // 테스트 패널은 재료를 받지 않는다 — 다만 재련소에서 얼마가 드는지는 같이 보여준다.
            string val = def != null
                ? $"효과값 {def.ValueAt(Mathf.Max(1, lv)):0.##} · 다음 강화 {def.CostAt(Mathf.Max(1, lv))}재료"
                : "";
            _rowLabels[i].text = lv > 0
                ? $"<color=#FFD24D>●</color> {nm}  Lv.{lv}   <color=#9AA3B5>{val}</color>"
                : $"<color=#5A6072>○</color> {nm}  <color=#9AA3B5>미장착</color>";
        }

        if (_summary != null) _summary.text = BuildPreview();
    }

    /// <summary>
    /// 실제 퍼널과 <b>같은 계산</b>을 돌려 결과를 보여준다 — 표시용 수식을 따로 두면
    /// 코드가 바뀔 때 화면만 옛날 값을 말하게 된다.
    /// </summary>
    private static string BuildPreview()
    {
        var req = ProjectileRequest.Create("preview", Vector3.zero, Vector3.forward, 100f, null,
                                           RangedParts.RangedSlot);
        RangedParts.Apply(ref req);

        var sb = new StringBuilder();
        sb.AppendLine("<color=#FFD24D>발사 결과 미리보기</color> (기본 피해 100 기준)");
        sb.AppendLine($"  투사체 {req.count}발 · 확산 {(req.count > 1 ? 30 : 0)}°");
        sb.AppendLine($"  관통 {req.pierce} · 피해배율 ×{req.damageMult:0.##} · 크기 ×{req.sizeMult:0.##}");
        sb.AppendLine($"  폭발반경 {req.explodeRadius:0.##} · 유도 {req.homingStrength:0}°/s"
                      + (req.returnOnPierce ? " · <color=#7FE3FF>관통 후 복귀</color>" : ""));
        sb.Append("  ※ 2번 키(원거리)로 쏠 때만 적용 — 1번 키(근접)에선 걸리지 않아야 정상");
        return sb.ToString();
    }

    // ── UI 헬퍼(절차 생성) ──────────────────────────────────

    private static void Anchor(RectTransform rt, Vector2 pivotAnchor, Vector2 size, Vector2 pos)
    {
        rt.anchorMin = rt.anchorMax = pivotAnchor;
        rt.pivot     = pivotAnchor;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    private static Image MakeImage(Transform parent, string name, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = c;
        return img;
    }

    private static TMP_Text MakeText(Transform parent, string name, float size, Color c, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.color    = c;
        t.alignment = align;
        t.raycastTarget = false;
        return t;
    }

    private static void MakeButton(Transform parent, string name, string label, Vector2 pivotAnchor,
                                   Vector2 size, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        var img = MakeImage(parent, name, new Color(0.18f, 0.20f, 0.26f, 1f));
        Anchor(img.rectTransform, pivotAnchor, size, pos);

        var btn = img.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(onClick);

        var t = MakeText(img.transform, "Label", 16f, Color.white, TextAlignmentOptions.Center);
        t.text = label;
        var rt = t.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }
}
