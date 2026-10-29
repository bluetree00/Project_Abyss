using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보호막 표시(10-23 사용자 「보호막이 적용되는지 · UI에도 적용되는지」) — 보호막은 피해를 먼저 받아 내고 있었지만
/// HUD 어디에도 그리지 않아 쌓였는지 · 깨졌는지 알 수 없었다(체력 옆 「🛡」 숫자는 방어력이다).
/// <list type="bullet">
/// <item>체력 바 위쪽에 얇은 은청색 띠 — 길이 = 보호막 ÷ 최대 체력(상한 = 최대 체력 × 0.3이라 바의 30%까지).</item>
/// <item>체력 숫자 뒤에 「+N」(같은 은청색).</item>
/// </list>
/// HudPresenter.RefreshStats(PlayerRuntimeStats.OnChanged — 보호막이 쌓이고 깎일 때마다 불린다)가 갱신한다.
/// </summary>
public sealed partial class CombatPanelView
{
    // ── Constants ───────────────────────────────────────────
    private const float ShieldBandFrom = 0.55f;   // 바 높이의 위쪽 45% — 체력 색을 가리지 않는 얇은 띠
    private static readonly Color ShieldInk = new(0.78f, 0.9f, 1f, 0.95f);
    private const string ShieldHex = "#C7E5FF";

    // ── Private ─────────────────────────────────────────────
    private Image _shieldBand;
    private int _shield;
    private int _hpShown = -1, _hpMaxShown = -1;

    // ── Public Methods ──────────────────────────────────────
    public void SetShield(int shield, int maxHp)
    {
        shield = Mathf.Max(0, shield);
        if (shield == _shield && _shieldBand != null) return;
        _shield = shield;

        EnsureShieldBand();
        if (_shieldBand != null)
        {
            float w = maxHp > 0 ? Mathf.Clamp01((float)shield / maxHp) : 0f;
            var rt = _shieldBand.rectTransform;
            rt.anchorMax = new Vector2(w, 1f);
            _shieldBand.enabled = shield > 0;
        }
        if (_hpShown >= 0) WriteHpText(_hpShown, _hpMaxShown);
    }

    // ── Private Methods ─────────────────────────────────────
    /// <summary>체력 숫자 — 보호막이 있으면 「+N」을 은청색으로 덧붙인다.</summary>
    private void WriteHpText(int hp, int maxHp)
    {
        _hpShown = hp;
        _hpMaxShown = maxHp;
        if (hpText == null) return;
        hpText.text = _shield > 0
            ? $"{hp} / {maxHp}  <color={ShieldHex}>+{_shield}</color>"
            : $"{hp} / {maxHp}";
    }

    private void EnsureShieldBand()
    {
        if (_shieldBand != null) return;
        var fillArea = hpSlider != null && hpSlider.fillRect != null ? hpSlider.fillRect.parent as RectTransform : null;
        var host = fillArea != null ? fillArea : _hpBar;
        if (host == null) return;
        _shieldBand = ShopUIStyle.MakeImage(host, "ShieldBand", ShieldInk);
        _shieldBand.sprite = UIThinShapes.Solid;
        var rt = _shieldBand.rectTransform;
        rt.anchorMin = new Vector2(0f, ShieldBandFrom);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot     = new Vector2(0f, 0.5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        _shieldBand.transform.SetAsLastSibling();   // 체력 채움 · 잔상 · 회복 띠 위
        _shieldBand.enabled = false;
    }
}
