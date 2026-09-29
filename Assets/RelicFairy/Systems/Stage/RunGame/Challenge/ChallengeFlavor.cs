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
/// </summary>
public static class ChallengeFlavor
{
    // 인덱스 0=숲(Ch1) 1=용암(Ch2) 2=성역(Ch3) 3=성채(Ch4)
    private static readonly string[] VaultTitle  = { "잊힌 은닉처", "용암에 잠긴 금고", "성물 보관소", "기사단 보물고" };
    private static readonly string[] VaultChest  = { "이끼 낀 궤짝", "그을린 궤짝",     "봉헌함",      "병기고 궤짝" };
    private static readonly string[] GambleBox   = { "수상한 그루터기", "갈라진 화로",   "봉인된 성물함", "노획품 상자" };
    private static readonly string[] Altar       = { "이끼 낀 제단",   "잿더미 제단",   "봉헌 제단",    "맹세의 제단" };
    private static readonly string[] OracleTitle = { "갈림길의 고목",  "세 갈래 용암길", "세 개의 성문",  "세 갈래 회랑" };

    // ── Public Methods ────────────────────────────────────
    public static string VaultName(GameRunSession run)  => VaultTitle[Index(run)];
    public static string ChestName(GameRunSession run)  => VaultChest[Index(run)];
    public static string GambleName(GameRunSession run) => GambleBox[Index(run)];
    public static string AltarName(GameRunSession run)  => Altar[Index(run)];
    public static string OracleName(GameRunSession run) => OracleTitle[Index(run)];

    // ── Private Methods ───────────────────────────────────
    /// <summary>챕터 → 테마 인덱스. 세션이 없거나 범위를 벗어나면 숲(0).</summary>
    private static int Index(GameRunSession run)
    {
        int i = (run != null ? (int)run.CurrentChapter : 1) - 1;
        return Mathf.Clamp(i, 0, 3);
    }
}
