using UnityEngine;

/// <summary>
/// 기억의 제단 공용 색. 해금 카드와 업적 행이 <b>같은 규칙</b>으로 칠해져야
/// 두 탭이 한 화면으로 읽힌다.
///
/// <para><b>단 하나의 규칙:</b> 따뜻한 황동(<see cref="Gold"/>)은 <b>지금 누를 수 있는 것</b>에만 쓴다.
/// 청록(<see cref="Essence"/>)은 재화·완료 같은 <b>정보</b>에 쓴다. 이 둘을 섞으면
/// 화면에서 무엇이 행동이고 무엇이 정보인지 구분이 사라진다.</para>
/// </summary>
public static class AltarPalette
{
    // ── 바탕 ─────────────────────────────────────────────
    public static readonly Color RowIdle      = new(0.106f, 0.098f, 0.161f, 1f);
    public static readonly Color RowClaimable = new(0.157f, 0.133f, 0.157f, 1f);   // 황동 기운이 살짝 도는 바탕
    public static readonly Color RowClaimed   = new(0.086f, 0.082f, 0.129f, 1f);

    public static readonly Color CardIdle     = new(0.106f, 0.098f, 0.161f, 1f);
    public static readonly Color CardBuyable  = new(0.145f, 0.129f, 0.176f, 1f);
    public static readonly Color CardUnlocked = new(0.078f, 0.129f, 0.125f, 1f);

    // ── 강조 ─────────────────────────────────────────────
    /// <summary>행동색 — 지금 누를 수 있는 것에만.</summary>
    public static readonly Color Gold        = new(0.890f, 0.659f, 0.298f);
    /// <summary>황동 위에 얹는 글자색.</summary>
    public static readonly Color OnGold      = new(0.059f, 0.039f, 0.020f);
    /// <summary>정보색 — 정수·완료.</summary>
    public static readonly Color Essence     = new(0.388f, 0.851f, 0.749f);

    public static readonly Color AccentLocked = new(0.180f, 0.169f, 0.243f);

    // ── 글자 ─────────────────────────────────────────────
    public static readonly Color TextPrimary = new(0.902f, 0.882f, 0.957f);
    public static readonly Color TextDim     = new(0.482f, 0.459f, 0.573f);
    public static readonly Color TextFaint   = new(0.325f, 0.310f, 0.396f);

    // ── 조용한 버튼(살 수 없음 / 수령 불가) ────────────────
    public static readonly Color BtnQuiet    = new(0.153f, 0.145f, 0.216f, 1f);

    // ── 탭 ───────────────────────────────────────────────
    public static readonly Color TabOn       = new(0.176f, 0.157f, 0.239f, 1f);
    public static readonly Color TabOff      = new(0.106f, 0.098f, 0.161f, 1f);
}
