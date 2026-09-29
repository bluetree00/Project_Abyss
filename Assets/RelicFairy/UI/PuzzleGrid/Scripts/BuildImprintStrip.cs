using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// 룬판 「◆ 활성 효과」 머리띠 오른쪽 한 줄 — 이번 런의 <b>발동 계열 각인</b>(09-29 빌드 컨셉).
/// 「연격 3/4 · 기동 1/2」처럼 쥔 계열과 다음 단계 역치를 보여 준다. 단계를 이룬 계열은 ◆ 수로.
/// </summary>
[DisallowMultipleComponent]
public sealed class BuildImprintStrip : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────
    private const float FontSize = 16f;

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
        rt.anchorMin = new Vector2(0.40f, 0f); rt.anchorMax = new Vector2(0.97f, 1f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        _text.fontSize = FontSize;
        _text.enableAutoSizing = true; _text.fontSizeMax = FontSize; _text.fontSizeMin = 13f;
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
    }
}
