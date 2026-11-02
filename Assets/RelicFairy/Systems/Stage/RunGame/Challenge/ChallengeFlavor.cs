using UnityEngine;

/// <summary>
/// 챌린지 표기의 <b>챕터 테마 변주</b>. 같은 규칙이라도 숲·용암·성역·성채에서 사물 이름이 달라야
/// "같은 방을 또 본다"는 느낌이 줄어든다.
///
/// ■ 원칙 — <b>기능을 알려주는 표기는 건드리지 않는다.</b>
///   프롬프트("[F] 개봉", "[F] 물약을 바친다")와 신탁의 문 이름(연료문/보물문/운명문)은
///   플레이어가 무엇을 고르는지 알려주는 정보라 테마화 대상이 아니다. 바뀌는 것은 사물의 이름뿐.
///
/// ■ 등급 산식·보상은 전혀 손대지 않는다. 표기 레이어다.
///
/// ■ 10-30 방 이름(보물 창고 · 신탁의 갈림길 · 제물 제단)은 테마화하지 않는다 — 문 배지(EventRewards.Title)와 방 안 안내가
///   다른 이름이라 같은 방인지 몰랐다(구현설계/글_쉬운말_점검_20261030.md V-01 · O-01 · S-04). 테마 이름은 상자 같은 사물 이름표에만 남긴다.
/// </summary>
public static class ChallengeFlavor
{
    // 인덱스 0=숲(Ch1) 1=용암(Ch2) 2=성역(Ch3) 3=성채(Ch4)
    private static readonly string[] VaultChest  = { "이끼 낀 궤짝", "그을린 궤짝",     "봉헌함",      "병기고 궤짝" };
    private static readonly string[] GambleBox   = { "수상한 그루터기", "갈라진 화로",   "봉인된 성물함", "노획품 상자" };

    // ── Public Methods ────────────────────────────────────
    public static string ChestName(GameRunSession run)  => VaultChest[Index(run)];
    public static string GambleName(GameRunSession run) => GambleBox[Index(run)];

    // ── Private Methods ───────────────────────────────────
    /// <summary>챕터 → 테마 인덱스. 세션이 없거나 범위를 벗어나면 숲(0).</summary>
    private static int Index(GameRunSession run)
    {
        int i = (run != null ? (int)run.CurrentChapter : 1) - 1;
        return Mathf.Clamp(i, 0, 3);
    }
}
