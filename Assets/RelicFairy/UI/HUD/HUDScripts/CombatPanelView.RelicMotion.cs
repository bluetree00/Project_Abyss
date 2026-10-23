using UnityEngine;
using UnityEngine.UI;

// 랜슬롯 칼 게이지 움직임(10-18 · 사용자 「진행해 보자」 — 참고 영상 ㉒ 랜슬롯에서 옮김 · 기획/RelicFairy_아이덴티티게이지_리서치_20261018.md §7).
//   ① 적중 계단 — 광기가 오를 때마다 칼날 채움 끝에 밝은 칸이 0.25초 켜졌다 사라진다(예전엔 2.5%씩 소리 없이 늘어 적중 박자가 안 보였다).
//   ② 광란 점화 — 광란에 드는 순간 칼끝에 보라 불씨가 붙고 0.4초 동안 보라가 칼끝 → 칼자루로 번진다(그 아래 붉은 칼이 남아 있다가 덮인다).
// 그 뒤는 그대로(광란 = 남은 시간만큼 칼끝부터 줄어듦). 맥동 없음 — 둘 다 한 번 켜지고 사라진다.
public sealed partial class CombatPanelView
{
    // ── Constants ──
    private const float LHitTickSec = 0.25f;
    private const float LIgniteSec  = 0.40f;
    private const float LSparkSize  = 34f;
    private static readonly Vector2 LHitTickSize = new(5f, 12f);
    private static readonly Color   LHitTickInk  = new(1f, 0.86f, 0.80f, 1f);
    private static readonly Color   LFrenzyInk   = new(0.78f, 0.30f, 0.98f, 1f);   // MadnessStack.BarColor 광란 보라

    // ── Private ──
    private Image _lHitTick, _lIgniteUnder, _lSpark;
    private float _lHitAt = -1f, _lIgniteAt = -1f, _lLastFill;
    private bool  _lWasFrenzy;

    // ── Private Methods ──

    /// <summary>Update(유물 바 갈래)에서 채움을 정한 뒤 — 랜슬롯 칼 스킨일 때만. 힙 할당 없음.</summary>
    private void TickLancelotMotion(MadnessStack madness, float fill)
    {
        if (_relicGaugeFill == null || _relicBar == null) return;
        EnsureLancelotMotion();
        float now    = Time.unscaledTime;
        bool  frenzy = madness.IsFrenzy;

        // ① 적중 계단
        if (!frenzy && fill > _lLastFill + 0.001f) _lHitAt = now;
        _lLastFill = fill;
        float h = _lHitAt < 0f ? 1f : (now - _lHitAt) / LHitTickSec;
        bool  tick = !frenzy && h < 1f;
        _lHitTick.enabled = tick;
        if (tick)
        {
            _lHitTick.rectTransform.anchoredPosition = new Vector2((fill - 0.5f) * _relicBar.rect.width, 0f);
            _lHitTick.color = UITheme.Thin.WithA(LHitTickInk, 0.95f * (1f - h * h));
        }

        // ② 광란 점화
        if (frenzy && !_lWasFrenzy) _lIgniteAt = now;
        _lWasFrenzy = frenzy;
        float k = _lIgniteAt < 0f ? 1f : (now - _lIgniteAt) / LIgniteSec;
        bool  igniting = frenzy && k < 1f;
        _lIgniteUnder.enabled      = igniting;
        _relicGaugeFill.fillOrigin = igniting ? (int)Image.OriginHorizontal.Right : (int)Image.OriginHorizontal.Left;
        if (igniting) _relicGaugeFill.fillAmount = UIMotion.EaseOut(k);
        _lSpark.color = UITheme.Thin.WithA(LFrenzyInk, igniting ? 0.9f * (1f - k) : 0f);
    }

    /// <summary>처음 한 번 — 점화 밑 붉은 칼(채움 바로 아래) · 적중 칸 · 칼끝 불씨(채움 위).</summary>
    private void EnsureLancelotMotion()
    {
        if (_lHitTick != null) return;
        _lIgniteUnder = MotionImage("IgniteUnder", relicGaugeReadySprite != null ? relicGaugeReadySprite : relicGaugeFillSprite);
        _lIgniteUnder.type       = Image.Type.Filled;
        _lIgniteUnder.fillMethod = Image.FillMethod.Horizontal;
        _lIgniteUnder.fillOrigin = (int)Image.OriginHorizontal.Left;
        _lIgniteUnder.fillAmount = 1f;
        _lIgniteUnder.color      = Color.white;
        var urt = _lIgniteUnder.rectTransform;
        urt.anchorMin = Vector2.zero; urt.anchorMax = Vector2.one;
        urt.offsetMin = urt.offsetMax = Vector2.zero;
        _lIgniteUnder.transform.SetSiblingIndex(_relicGaugeFill.transform.GetSiblingIndex());   // 채움 바로 아래
        _lIgniteUnder.enabled = false;

        _lHitTick = MotionImage("HitTick", UIThinShapes.Solid);
        var hrt = _lHitTick.rectTransform;
        hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0.5f, 0.5f);
        hrt.sizeDelta = LHitTickSize;
        _lHitTick.enabled = false;

        _lSpark = MotionImage("IgniteSpark", UI_RuneSelectPopup.SoftDot);
        var srt = _lSpark.rectTransform;
        srt.anchorMin = srt.anchorMax = new Vector2(1f, 0.5f);
        srt.pivot            = new Vector2(0.5f, 0.5f);
        srt.anchoredPosition = new Vector2(-6f, 0f);
        srt.sizeDelta        = Vector2.one * LSparkSize;
        _lSpark.color = Color.clear;
    }

    private Image MotionImage(string name, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(_relicBar, false);
        var img = go.AddComponent<Image>();
        img.sprite        = sprite;
        img.raycastTarget = false;
        return img;
    }
}
