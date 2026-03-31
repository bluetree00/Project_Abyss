//============================================================
// BossPanelView.cs
// - 보스 HP 슬라이더/텍스트
// - 보스 이름 텍스트
// - HP% 색상 그라디언트 (초록 → 황금 → 빨강)
// HudView.bossPanelView 슬롯에 할당, HudPresenter.BindBoss()로 제어
//============================================================
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class BossPanelView : MonoBehaviour
{
    [Header("Boss HP")]
    [SerializeField] private Slider   hpSlider;
    [SerializeField] private Image    hpFillImage;  // hpSlider Fill Image (색상 그라디언트용)
    [SerializeField] private TMP_Text hpText;

    [Header("Boss Name")]
    [SerializeField] private TMP_Text nameText;

    [Header("HP Color Gradient")]
    [SerializeField] private Color colorHigh = new Color(0.25f, 0.90f, 0.35f);  // 초록
    [SerializeField] private Color colorMid  = new Color(1.00f, 0.80f, 0.10f);  // 황금
    [SerializeField] private Color colorLow  = new Color(0.95f, 0.18f, 0.10f);  // 빨강

    private int _maxHp;

    // ─────────────────────────────────────────────────────────
    public void Init(int maxHp, string bossName)
    {
        _maxHp = Mathf.Max(1, maxHp);

        if (hpSlider != null) { hpSlider.maxValue = _maxHp; hpSlider.value = _maxHp; }
        if (hpText   != null) hpText.text   = $"{_maxHp} / {_maxHp}";
        if (nameText != null) nameText.text  = bossName ?? string.Empty;

        ApplyFillColor(1f);
    }

    public void SetHP(int hp, int maxHp)
    {
        _maxHp = Mathf.Max(1, maxHp);
        int clamped = Mathf.Clamp(hp, 0, _maxHp);

        if (hpSlider != null) { hpSlider.maxValue = _maxHp; hpSlider.value = clamped; }
        if (hpText   != null) hpText.text = $"{Mathf.Max(0, hp)} / {_maxHp}";

        ApplyFillColor((float)clamped / _maxHp);
    }

    // ─────────────────────────────────────────────────────────
    private void ApplyFillColor(float pct)
    {
        if (hpFillImage == null) return;

        Color c;
        if (pct >= 0.75f)
            c = colorHigh;
        else if (pct >= 0.40f)
            c = Color.Lerp(colorMid, colorHigh, (pct - 0.40f) / 0.35f);
        else
            c = Color.Lerp(colorLow, colorMid, pct / 0.40f);

        hpFillImage.color = c;
    }
}
