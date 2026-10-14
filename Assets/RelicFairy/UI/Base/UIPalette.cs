using UnityEngine;

/// <summary>
/// 화면을 가로지르는 공용 강조색. 상점·제단·팝업·월드 프롬프트가 <b>같은 금색</b>으로
/// 칠해져야 "지금 누를 수 있는 것"이 한 눈에 읽힌다.
///
/// <para>값은 기존 정본이던 <see cref="ShopUIStyle.Gold"/>를 그대로 승계한다 —
/// 흩어져 있던 금색 9종 중 7종이 이 값 언저리였다. 여기 두는 이유는 월드 프롬프트·챌린지처럼
/// 상점과 무관한 코드가 상점 스타일 클래스를 참조하지 않게 하기 위해서다.</para>
///
/// <para><b>예외로 남긴 금색</b>(의도적으로 다른 톤이므로 여기로 수렴시키지 않는다):
/// <c>HudView.GoldFrameTint</c>(재화 프레임 아트 톤), <c>UI_CovenantAssemble.GoldColor</c>(서약 <i>등급</i> 이름),
/// <c>UI_RelicInfoPopup.AccentSkill</c>(패시브·상태·스킬 3색 분류축).</para>
/// </summary>
public static class UIPalette
{
    /// <summary>행동/강조 금색 — 누를 수 있는 것, 집어들 수 있는 것에만.</summary>
    public static readonly Color Gold = new(1f, 0.82f, 0.28f, 1f);

    /// <summary>RichText <c>&lt;color&gt;</c> 태그용. <see cref="Gold"/>와 같은 색이다.</summary>
    public const string GoldHex = "#FFD147";

    // ── 재화 색(10-13 · 재화 문서 D2) — 납품 HUD 아이콘 색에 맞춘다. 예전엔 강화재료 주황 · 원석 하늘색/청록 · 정수 청록이라
    //    아이콘(푸른 결정 · 초록 시험관 · 남보라 캡슐)과 어긋났다. 알림 · 분해 표기 글자가 이 셋을 쓴다(HUD 칸 테두리는 HudView가 같은 색조로).
    /// <summary>강화재료(푸른 결정) — 재련소.</summary>
    public const string EnhanceHex = "#6FA8FF";
    /// <summary>원석(초록 시험관) — 정제소.</summary>
    public const string RuneOreHex = "#6FD69A";
    /// <summary>심연의 정수(남보라 캡슐) — 기억의 제단.</summary>
    public const string EssenceHex = "#A99BF2";
}
