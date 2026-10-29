using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 톤 v2 룬판(10-08) — 사용자 시안 3판 확정 · 「실제로 구현되어야 해」. 바탕화면 기획/룬판_톤v2_20261008/.
// 장식은 지우지 않고 얇게(등급 보석 테 = 선 깎고 0.62배) · 판 외곽 1px 금선 · 베벨 판 → 평평한 글래스 ·
// 버튼 = 얇은 테(완료만 금) · 왼쪽 = 캐릭터 정보(위) · 속성 시너지(가운데) · 활성 효과(아래)를 얇은 구획 테로 나눈다(10-10 순서) ·
// 「놓을 자리」 배지 대신 시너지 머리띠에 「룬 n / 20」(RuneBlockBudget, 10-10 칸 → 개수). 글자는 Noto 300/400/500 · 굵게 없음.
// ThinGrid = false면 예전 스킨 경로 그대로.
public sealed partial class UI_GridPanel
{
    // ── Constants ──
    /// <summary>톤 v2 룬판 스위치(되돌리기 한 줄).</summary>
    internal static readonly bool ThinGrid = true;
    /// <summary>등급 보석 테 조각 배율 — 사용자 「리소스는 쓰되 얇게 조금」.</summary>
    internal const float ThinGemScale = 0.62f;

    private const float ThinTextPassSec = 0.2f;   // 열려 있는 동안 새로 생긴 글자(보관함 카드 · 효과 줄 · 토스트 · 확인 창)를 Noto로
    private const float SectionInset    = 4f;     // 구획 테는 사방 4px 안쪽 — 맞붙은 구획 사이에 8px 틈

    // ── Private ──
    private bool _thinChromeApplied;
    private float _thinTextPassAt;
    private readonly List<TMP_Text> _thinTexts = new();

    // ── Lifecycle ──
    private void Update()
    {
        HandleRotateInput();   // 10-25 룬 돌리기(E) — UI_GridPanel.cs
        if (!ThinGrid || !_isOpen || Time.unscaledTime < _thinTextPassAt) return;
        _thinTextPassAt = Time.unscaledTime + ThinTextPassSec;
        ApplyThinTexts();
    }

    // ── Private Methods ──

    /// <summary>열 때 한 번(스킨 · 레이아웃이 갖춰진 뒤) — 머리띠 · 버튼 · 보관함 띠 · 왼쪽 구획.</summary>
    private void ApplyThinChrome()
    {
        if (_thinChromeApplied) return;
        _thinChromeApplied = true;
        Canvas.ForceUpdateCanvases();   // 버튼 테는 버튼 크기로 짓는다

        if (_headerBandImg != null) Glass(_headerBandImg);

        ThinButton(_confirmButton,           primary: true);
        ThinButton(_resetButton,             primary: false);
        ThinButton(_salvageButton,           primary: false);
        ThinButton(_rotateButton,            primary: false);   // 10-26 정보판 [돌리기 E]
        ThinButton(_confirmDialogDiscardBtn, primary: true);
        ThinButton(_confirmDialogKeepBtn,    primary: false);
        ThinButton(_resetDialogYes,          primary: true);
        ThinButton(_resetDialogNo,           primary: false);

        // 보관함 띠 — 남색 판 → 글래스, 위 경계는 1px 금선
        if (_footerRT != null && _footerRT.TryGetComponent<Image>(out var footerBg)) Glass(footerBg);
        if (_footerRT != null && _footerRT.Find("TopLine") is RectTransform line && line.TryGetComponent<Image>(out var lineImg))
        {
            lineImg.color  = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.35f);
            line.sizeDelta = new Vector2(line.sizeDelta.x, UITheme.Thin.Stroke);
        }

        // 왼쪽 세 구획 — 캐릭터 정보(위) · 속성 시너지(가운데) · 활성 효과(아래, 10-10). 머리띠 판은 걷고 구획 테가 묶음을 맡는다.
        var lineInk = UITheme.Thin.InkA(0.35f);
        var tick    = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.7f);
        var fill    = new Color(1f, 1f, 1f, 0.025f);
        var charRT  = _charInfoView != null ? (RectTransform)_charInfoView.transform : null;
        UIThinBox.Create(charRT,             "ThinSection", Vector2.zero, Vector2.one, SectionInset, lineInk, tick, fill);
        UIThinBox.Create(_synergyStatusRoot, "ThinSection", Vector2.zero, Vector2.one, SectionInset, lineInk, tick, fill);
        UIThinBox.Create(_effectsRoot,       "ThinSection", Vector2.zero, Vector2.one, SectionInset, lineInk, tick, fill);
        ClearBand(_effectsRoot != null ? ShopUIStyle.FindDeep(_effectsRoot, "EffectsHdr") : null);
        ClearBand(_synergyStatusRoot != null ? ShopUIStyle.FindDeep(_synergyStatusRoot, "GuideHeader") : null);

        ApplyThinTexts();
    }

    /// <summary>
    /// 패널 안 글자 = Noto(큰 글자 300 · 긴 본문 400 · 라벨 · 숫자 500), 굵게 없음, 소프트 그림자. 이미 Noto인 글자는 건너뛴다.
    /// Noto는 줄 높이가 DNF보다 커서(≈1.45em) 칸 높이에 맞춘 한 줄 글자가 잘림(Ellipsis · Truncate)으로 통째 사라진다 — 칸을 늘린다.
    /// </summary>
    private void ApplyThinTexts()
    {
        var light   = UITheme.Font(UIWeight.Light);
        var regular = UITheme.Font(UIWeight.Regular);
        var medium  = UITheme.Font(UIWeight.Medium);
        if (light == null || regular == null || medium == null || UISkin.ThemeFonts == null) return;   // 글꼴이 아직 안 올라왔다 — 다음 주기에

        GetComponentsInChildren(true, _thinTexts);
        foreach (var t in _thinTexts)
        {
            if (t == null || t.font == light || t.font == regular || t.font == medium) continue;
            var weight = t.fontSize >= 26f ? UIWeight.Light
                       : (t.text != null && t.text.Length >= 12 && t.fontSize <= 19f) ? UIWeight.Regular
                       : UIWeight.Medium;
            UITheme.StyleThinText(t, weight, t.fontSize, t.color, shadow: true);
            if (t.overflowMode != TextOverflowModes.Ellipsis && t.overflowMode != TextOverflowModes.Truncate) continue;
            float need = t.fontSize * 1.5f, h = t.rectTransform.rect.height;
            if (h > 0f && h < need) t.rectTransform.sizeDelta += new Vector2(0f, need - h);
        }
    }

    /// <summary>베벨 버튼 → 글래스 + 얇은 테(주 = 금 · 준비됨, 보조 = 금 55%) · 누르면 눌림. 안쪽 판 · 장식은 끈다.</summary>
    private static void ThinButton(Button btn, bool primary)
    {
        if (btn == null) return;
        var rt = (RectTransform)btn.transform;
        foreach (var img in btn.GetComponentsInChildren<Image>(true))
        {
            if (img.transform == btn.transform) Glass(img);
            else img.enabled = false;
        }
        var frame = UIThinFrame.Create(rt, ThinShape.Square, rt.rect.size);
        frame.SetState(primary ? ThinFrameState.Ready : ThinFrameState.Active, animate: false);
        btn.onClick.AddListener(frame.PlayPress);
        foreach (var t in btn.GetComponentsInChildren<TMP_Text>(true))
            UITheme.StyleThinText(t, UIWeight.Medium, primary ? 22f : 20f, primary ? UITheme.Thin.Gold : UITheme.Thin.Ink);
    }

    private static void Glass(Image img)
    {
        img.sprite = null;
        img.type   = Image.Type.Simple;
        img.color  = UITheme.Thin.GlassFill;
    }

    private static void ClearBand(Transform band)
    {
        if (band != null && band.TryGetComponent<Image>(out var img)) img.color = Color.clear;
    }
}
