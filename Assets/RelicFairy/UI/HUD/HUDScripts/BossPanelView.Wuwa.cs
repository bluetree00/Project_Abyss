using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 명조형 보스바(10-08) — 사용자 「HUD도 명조 UX와 비슷하게」 · 시안 「위 가운데 보스 체력바도 같은 결」(권장안 그대로 진행).
// 장식 테 없이 길고 얇은 한 줄 · 이름은 위(Noto) · 수치는 바 아래 오른쪽. 페이즈 줄 · 깨짐 · 차오름 · 무적/무방비 연출은 그대로
// (덮개는 모두 같은 내부 창 · 채움 폭을 따르므로 창 높이만 줄이면 함께 얇아진다).
public sealed partial class BossPanelView
{
    // ── Constants ──
    /// <summary>얇은 한 줄이 되는 내부 창 비율(가로, 세로) — 패널 높이(≈85px, 테두리 아트 비율)의 10% ≈ 8.5px(4.5%는 3.8px로 너무 얇았다).</summary>
    private static readonly Vector2 WuwaInnerWindow = new(0.97f, 0.10f);
    private static readonly Color WuwaTrack = new(0.03f, 0.03f, 0.05f, 0.72f);
    private static readonly Color WuwaFill  = new(0.86f, 0.32f, 0.30f, 1f);
    private const float WuwaNameFontSize = 20f;
    // 수치 「6437 / 42000」 = 3순위(10-11 비교 리서치 A §2-4 P3 — 비교 6게임 중 4게임은 보스 체력 숫자를 안 보인다).
    // 14 · 알파 0.95로 이름과 비슷하게 밝았다 → 13 · 알파 0.6. 이름(20 · 1.0) > 바 > 수치 순으로 읽힌다.
    private const float WuwaHpFontSize = 13f;
    private const float WuwaHpAlpha    = 0.6f;
    private static readonly Color WuwaHpInk = new(0.93f, 0.90f, 0.84f, WuwaHpAlpha);   // HpInk 색 그대로(부분 클래스끼리 정적 초기화 순서가 정해져 있지 않아 값으로 적는다)

    // ── Private ──
    private bool _wuwaApplied;
    private bool _wuwaFontsReady;   // 톤 v2 글꼴로 발랐는가 — 패널이 글꼴(Addressables)보다 먼저 생기면 Init이 한 번 더 바른다

    // ── Private Methods ──

    /// <summary>EnsureWired(스킨) 뒤 한 번 — 테를 걷고 트랙 · 채움을 단색 한 줄로, 글자를 Noto로.</summary>
    private void ApplyWuwaBossBar()
    {
        if (_wuwaApplied) return;
        _wuwaApplied = true;
        ApplyWuwaFonts();

        if (transform.Find("BossBarFrame") is Transform frame && frame.TryGetComponent<Image>(out var frameImg))
            frameImg.enabled = false;   // 장식 테 없이 얇은 한 줄

        if (hpSlider != null && hpSlider.transform.Find("Background") is Transform bg && bg.TryGetComponent<Image>(out var bgImg))
        {
            bgImg.sprite = UIThinShapes.Solid;
            bgImg.type   = Image.Type.Simple;
            bgImg.color  = WuwaTrack;
        }
        if (hpFillImage != null)
        {
            hpFillImage.sprite = UIThinShapes.Solid;   // Filled · Horizontal 그대로(채움 비율로 그린다)
            hpFillImage.color  = WuwaFill;
        }

        if (nameText != null)
        {
            // 이름은 바 바로 위(명조) — 예전 자리는 두꺼운 장식 테 기준이라 얇은 바에서 50px 떠 있었다(10-08 리치 실측)
            float top = Mathf.Clamp(innerWindowCenterY, innerWindowRatio.y * 0.5f, 1f - innerWindowRatio.y * 0.5f) + innerWindowRatio.y * 0.5f;
            var nrt = nameText.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.5f, top);
            nrt.pivot            = new Vector2(0.5f, 0f);
            nrt.anchoredPosition = new Vector2(0f, 4f);
            nrt.sizeDelta        = new Vector2(nrt.sizeDelta.x, 30f);
            nameText.alignment   = TextAlignmentOptions.Bottom;
            if (_nameBand != null)
            {
                _nameBand.anchorMin = nrt.anchorMin; _nameBand.anchorMax = nrt.anchorMax; _nameBand.pivot = nrt.pivot;
                if (_nameBand.TryGetComponent<Image>(out var band)) band.color = new Color(0.02f, 0.02f, 0.035f, 0.35f);
            }
            FitNameBand();
        }

        if (hpText != null)   // 수치 = 바 아래 오른쪽(얇은 바 안에는 글자가 들어가지 않는다)
        {
            var rt = hpText.rectTransform;
            FitInnerWindow(rt);
            rt.anchorMax = new Vector2(rt.anchorMax.x, rt.anchorMin.y);
            rt.pivot     = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(0f, -24f);
            rt.offsetMax = new Vector2(0f, -4f);
            hpText.alignment = TextAlignmentOptions.TopRight;
        }
    }

    /// <summary>이름(Medium 20) · 수치(Medium 13 · 알파 0.6) 글꼴 — 가짜 굵게 없음 · 부드러운 그림자.</summary>
    private void ApplyWuwaFonts()
    {
        if (nameText != null)
        {
            UITheme.StyleThinText(nameText, UIWeight.Medium, WuwaNameFontSize, NameInk, shadow: true);
            nameText.characterSpacing = 2f;
        }
        if (hpText != null) UITheme.StyleThinText(hpText, UIWeight.Medium, WuwaHpFontSize, WuwaHpInk, shadow: true);
        _wuwaFontsReady = UISkin.ThemeFonts != null;
    }

    /// <summary>Init마다 — 명조형 바를 깐 뒤에 글꼴이 올라왔으면 이제 한 번 바른다(이름이 DNF로 남지 않게).</summary>
    private void EnsureWuwaFonts()
    {
        if (!_wuwaApplied || _wuwaFontsReady || UISkin.ThemeFonts == null) return;
        ApplyWuwaFonts();
    }
}
