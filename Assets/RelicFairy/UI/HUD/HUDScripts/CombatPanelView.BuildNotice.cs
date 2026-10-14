using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 빌드 완성 신호 줄(10-14 빌드 시너지 · 83 UI 요청 3) — 아이템 알림과 같은 왼쪽 쌓이는 줄(<see cref="ShowItemEffectNotice"/>)에
/// [색 강조선 · (진화면 유물 문장) · 칩 글 · 단계 눈금 · 머리] / [하는 일] 두 줄로 선다. 수명 · 상한 · 흐려짐은 아이템 알림 목록을 같이 쓴다(한 줄기).
/// 켜짐(톤 v2 「상태 변화는 실시간」): 받침이 칩 색으로 한 번 물들었다 가라앉고 · 이번에 켜진 눈금이 커졌다 자리 잡는다(0.4초).
/// 흔들림 · 이동 없음. 글자 흐림은 줄의 CanvasGroup으로만(HUD 하드 가드가 글자 알파를 1로 되돌린다).
/// </summary>
public sealed partial class CombatPanelView
{
    // ── Constants ──
    private const float BuildNoticeHold    = 3.5f;   // 아이템 알림(2초)보다 길게 — 빌드가 한 단계 오른 순간
    private const float BuildNoticeSize    = 16f;    // UI 최소 글자
    private const float BuildNoticeAccentW = 2f;
    private const float BuildNoticeRowH    = 22f;
    private const float BuildNoticePip     = 9f;
    private const float BuildNoticePipGap  = 3f;
    private const float BuildNoticeCrest   = 22f;
    private const float BuildNoticeInSec   = 0.4f;
    private const float BuildNoticePipFrom = 1.7f;
    private const float BuildNoticeLitTint = 0.42f;   // 켜지는 순간 받침 = 칩 색 42%
    private const float BuildNoticeOffPip  = 0.45f;   // 아직 안 켜진 눈금(테만) 알파
    private const float BuildNoticeDetailA = 0.78f;   // 둘째 줄(하는 일) 글자 알파 — 머리 줄보다 한 단 흐리게

    // ── Public Methods ──

    /// <summary>
    /// 빌드 완성 신호(HudPresenter가 보류 · 순서를 정한 뒤 부른다). 두 줄 — [칩 · 눈금 · 머리] / [하는 일]. 왼쪽 강조선은 두 줄 높이.
    /// 줄 폭은 알림 칸(420) 안 — 하는 일이 길면 그 줄만 접힌다(10-14 실측 SYN_4: 한 줄이 칸보다 넓어 글이 65~97px 넘쳤다).
    /// </summary>
    public void ShowBuildNotice(in BuildNotice n)
    {
        EnsureItemNoticeRoot();
        if (_itemNoticeRoot == null || string.IsNullOrEmpty(n.Head)) return;

        if (_itemNotices.Count >= MaxItemNotices)
        {
            if (_itemNotices[0].go != null) Destroy(_itemNotices[0].go);
            _itemNotices.RemoveAt(0);
        }

        // 줄 이름은 「ItemNotice」로 시작한다 — 일시 알림 계약(HudBootstrapper.IsSelfManagedNotice)과 같은 이름 줄기.
        var go = new GameObject($"ItemNotice_Build_{n.Key}", typeof(RectTransform));
        go.transform.SetParent(_itemNoticeRoot, false);
        var plate = go.AddComponent<Image>();
        plate.color = ItemNoticePlate;
        plate.raycastTarget = false;
        var row = go.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(0, 12, 4, 5);   // 강조선이 받침 왼쪽 끝에 붙는다
        row.spacing = 8f;
        row.childAlignment = TextAnchor.UpperLeft;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = true;            // 강조선이 두 줄 높이로 선다
        var group = go.AddComponent<CanvasGroup>();
        group.blocksRaycasts = group.interactable = false;

        NoticeImage(go.transform, "Accent", n.Tint, null, BuildNoticeAccentW, BuildNoticeRowH);
        var lines = NoticeLayout(go.transform, "Lines", vertical: true, 1f, TextAnchor.UpperLeft);
        var head  = NoticeLayout(lines, "HeadLine", vertical: false, 8f, TextAnchor.MiddleLeft);
        if (n.What == BuildNotice.Kind.Evolution)
        {
            var crest = RelicCrest();
            if (crest != null) NoticeImage(head, "Crest", n.Tint, crest, BuildNoticeCrest, BuildNoticeCrest).preserveAspect = true;
        }
        NoticeText(head, "Chip", n.Chip, UIWeight.Medium, n.Tint, wrap: false);
        Image lit = n.MaxTier > 0 ? BuildNoticePips(head, n) : null;
        NoticeText(head, "Text", n.Head, UIWeight.Medium, UITheme.Thin.InkA(0.95f), wrap: false);
        if (!string.IsNullOrEmpty(n.Detail))
            NoticeText(lines, "Detail", n.Detail, UIWeight.Regular, UITheme.Thin.InkA(BuildNoticeDetailA), wrap: true);

        _itemNotices.Add(new ItemNoticeEntry
        {
            go = go,
            group = group,
            hideAt = Time.unscaledTime + BuildNoticeHold + ItemNoticeFadeTime,
        });
        BuildNoticeInAsync(plate, n.Tint, lit, go.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>
    /// 명조형 HUD — 알림 줄 바닥이 넘지 말아야 할 높이(무기 원 · 형 알약 윗변, 화면 단위). 명조형이 아니면 NaN.
    /// 무기 원이 왼쪽 기둥 오른쪽 아래에 있어 기둥 바닥부터 쌓던 알림 줄이 무기 원을 덮었다(10-14 SYN_4) — HudView가 이 위로 올린다.
    /// </summary>
    public float NoticeFloorWorld()
    {
        if (!WuwaHud) return float.NaN;
        float top = float.NaN;
        for (int i = 0; i < _weaponSlotRoot.Length; i++)
        {
            var root = _weaponSlotRoot[i];
            if (root == null || !root.gameObject.activeInHierarchy) continue;
            root.GetWorldCorners(_columnCorners);
            top = float.IsNaN(top) ? _columnCorners[1].y : Mathf.Max(top, _columnCorners[1].y);
        }
        if (_formPill != null && _formPill.gameObject.activeInHierarchy)
        {
            _formPill.GetWorldCorners(_columnCorners);
            top = float.IsNaN(top) ? _columnCorners[1].y : Mathf.Max(top, _columnCorners[1].y);
        }
        return top;
    }

    // ── Private Methods ──

    /// <summary>단계 눈금 — 마름모 MaxTier개(켜진 것 = 채움 · 아닌 것 = 테). 이번에 켜진 칸(맨 끝 켜진 것)을 돌려준다.</summary>
    private static Image BuildNoticePips(Transform parent, in BuildNotice n)
    {
        var box = new GameObject("Pips", typeof(RectTransform)).transform;
        box.SetParent(parent, false);
        var h = box.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = BuildNoticePipGap;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;

        var on  = UIThinShapes.Fill(ThinShape.Diamond, BuildNoticePip, BuildNoticePip);
        var off = UIThinShapes.Outline(ThinShape.Diamond, BuildNoticePip, BuildNoticePip);
        Image newest = null;
        for (int i = 1; i <= n.MaxTier; i++)
        {
            bool lit = i <= n.Tier;
            var col  = lit ? n.Tint : UITheme.Thin.WithA(n.Tint, BuildNoticeOffPip);
            var img  = NoticeImage(box, lit ? $"PipOn{i}" : $"PipOff{i}", col, lit ? on : off, BuildNoticePip, BuildNoticePip);
            if (lit) newest = img;
        }
        return newest;
    }

    private static Transform NoticeLayout(Transform parent, string name, bool vertical, float spacing, TextAnchor align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        HorizontalOrVerticalLayoutGroup g = vertical ? go.AddComponent<VerticalLayoutGroup>() : go.AddComponent<HorizontalLayoutGroup>();
        g.spacing = spacing;
        g.childAlignment = align;
        g.childControlWidth = g.childControlHeight = true;
        g.childForceExpandWidth = g.childForceExpandHeight = false;
        return go.transform;
    }

    private static Image NoticeImage(Transform parent, string name, Color color, Sprite sprite, float w, float h)
    {
        var img = ShopUIStyle.MakeImage(parent, name, color);
        img.sprite = sprite;
        var le = img.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = w;
        le.minHeight = le.preferredHeight = h;
        return img;
    }

    private static TMP_Text NoticeText(Transform parent, string name, string text, UIWeight weight, Color color, bool wrap)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        UITheme.StyleThinText(t, weight, BuildNoticeSize, color, shadow: true);
        t.enableAutoSizing = false;
        t.alignment        = TextAlignmentOptions.MidlineLeft;
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        if (!wrap)
        {
            // 머리 줄 글자는 줄지 않는다 — 칸이 좁아도 글 폭을 최소 폭으로 잡아 둔다(SYN_4: 칩 글이 5~7px 넘쳤다)
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = t.GetPreferredValues(text).x + 1f;
        }
        t.raycastTarget    = false;
        t.text = text;
        return t;
    }

    /// <summary>지금 런 유물의 문장(흰 선화 — 칠해 쓴다). 없으면 null.</summary>
    private static Sprite RelicCrest()
    {
        var relic = AppBootstrapper.Instance != null ? AppBootstrapper.Instance.Loadout?.Relic : null;
        return relic != null ? relic.Crest : null;
    }

    /// <summary>켜짐 — 받침이 칩 색으로 물들었다 가라앉고, 이번에 켜진 눈금이 1.7배에서 자리 잡는다.</summary>
    private static async UniTaskVoid BuildNoticeInAsync(Image plate, Color tint, Image pip, CancellationToken ct)
    {
        var from = new Color(tint.r * BuildNoticeLitTint, tint.g * BuildNoticeLitTint, tint.b * BuildNoticeLitTint, 0.9f);
        try
        {
            await UIMotion.Tween(BuildNoticeInSec, k =>
            {
                if (plate != null) plate.color = Color.Lerp(from, ItemNoticePlate, k);
                if (pip != null) pip.rectTransform.localScale = Vector3.one * Mathf.Lerp(BuildNoticePipFrom, 1f, k);
            }, ct);
        }
        catch (OperationCanceledException) { }
    }
}
