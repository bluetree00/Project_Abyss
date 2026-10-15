using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// 룬판 「◆ 활성 효과」 머리띠 오른쪽 한 줄 — 이번 런의 <b>발동 계열 각인</b>(09-29 빌드 컨셉).
/// 「연격 3/4 · 기동 1/2」처럼 쥔 계열과 다음 단계 역치를 보여 준다. 단계를 이룬 계열은 ◆ 수로.
/// 머리띠 오른쪽에 다 안 들어가면(계열이 많은 빌드) 머리띠 <b>아래</b>로 내려 줄바꿈하고 효과 목록(EffectsScroll)을 그만큼 내린다 —
/// 오른쪽 한 줄에 우겨 넣으면 왼쪽으로 넘쳐 「◆ 활성 효과」 제목을 덮고 판 밖까지 나갔다(10-15 겹침 점검).
/// </summary>
[DisallowMultipleComponent]
public sealed class BuildImprintStrip : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────
    private const float FontSize = 16f;
    private const float InlineMin = 0.40f, InlineMax = 0.97f, BelowMin = 0.04f;   // 머리띠 안 오른쪽 · 아래로 내릴 때 왼쪽 끝

    // ── Private ──────────────────────────────────────────
    private TMP_Text _text;
    private readonly StringBuilder _sb = new(96);

    // ── Lifecycle ────────────────────────────────────────
    private void Awake()
    {
        var go = new GameObject("Imprints", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        _text = go.AddComponent<TextMeshProUGUI>();   // transform 캐시는 AddComponent 뒤(가짜 null 함정)
        var rt = _text.rectTransform;
        rt.anchorMin = new Vector2(InlineMin, 0f); rt.anchorMax = new Vector2(InlineMax, 1f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        _text.fontSize = FontSize;
        _text.enableAutoSizing = true; _text.fontSizeMax = FontSize; _text.fontSizeMin = 15f;   // 글자 최소 15(10-15 — 13이었다)
        _text.alignment = TextAlignmentOptions.MidlineRight;
        _text.textWrappingMode = TextWrappingModes.NoWrap;
        _text.raycastTarget = false;
        _text.richText = true;
        TMPOutlineHelper.ApplySoftShadow(_text);
    }

    private void OnEnable()
    {
        BuildImprint.Changed += Refresh;
        Refresh();
    }

    private void OnDisable() => BuildImprint.Changed -= Refresh;

    // 머리띠가 옮겨지거나(DetachEffects) 폭이 정해지면 다시 맞춘다
    private void OnRectTransformDimensionsChange()
    {
        if (_text != null && isActiveAndEnabled) FitRow();
    }

    // ── Private Methods ──────────────────────────────────

    private void Refresh()
    {
        if (_text == null) return;
        _sb.Clear();
        foreach (var f in BuildFamilyRules.All)
        {
            int c = BuildImprint.Count(f);
            if (c <= 0) continue;
            if (_sb.Length > 0) _sb.Append("   ");
            int next  = BuildFamilyRules.NextThreshold(c);
            int stage = BuildFamilyRules.StageOf(c);
            _sb.Append("<color=").Append(BuildFamilyRules.Hex(f)).Append("><b>").Append(BuildFamilyRules.Label(f)).Append("</b></color> ");
            if (stage > 0) _sb.Append("<color=#E8BA54>").Append('◆', stage).Append("</color>");
            _sb.Append(c);
            if (next > 0) _sb.Append("<color=#8A8594>/").Append(next).Append("</color>");
        }
        _text.text = _sb.Length > 0 ? _sb.ToString() : "<color=#8A8594>발동 계열 없음</color>";
        FitRow();
    }

    /// <summary>머리띠 오른쪽(한 줄 · 16 → 15 자동 크기)에 들어가면 그대로, 아니면 머리띠 아래 줄바꿈(16 고정) + 효과 목록을 그 높이만큼 내린다.</summary>
    private void FitRow()
    {
        if (!(transform is RectTransform hdr)) return;
        float w = hdr.rect.width;
        if (w <= 1f) return;
        var rt = _text.rectTransform;
        _text.enableAutoSizing = false;
        _text.fontSize = _text.fontSizeMin;
        bool below = _text.GetPreferredValues(_text.text, 0f, 0f).x > w * (InlineMax - InlineMin);
        float h = 0f;
        if (below)
        {
            _text.fontSize = FontSize;
            _text.textWrappingMode = TextWrappingModes.Normal;
            _text.alignment = TextAlignmentOptions.TopLeft;
            h = Mathf.Ceil(_text.GetPreferredValues(_text.text, w * (InlineMax - BelowMin), 0f).y) + 8f;
            rt.anchorMin = new Vector2(BelowMin, 0f);
            rt.anchorMax = new Vector2(InlineMax, 0f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -4f);
            rt.sizeDelta = new Vector2(0f, h - 4f);
        }
        else
        {
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _text.alignment = TextAlignmentOptions.MidlineRight;
            rt.anchorMin = new Vector2(InlineMin, 0f);
            rt.anchorMax = new Vector2(InlineMax, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _text.enableAutoSizing = true;
            _text.fontSize = FontSize;
        }
        // 효과 목록 위끝 = 머리띠 아래(+ 내린 줄 높이) — 두 배치(분리 · 원래) 모두 위끝이 머리띠 높이만큼 내려와 있다
        if (transform.parent != null && transform.parent.Find("EffectsScroll") is RectTransform scroll)
            scroll.offsetMax = new Vector2(scroll.offsetMax.x, -(hdr.rect.height + h));
    }
}
