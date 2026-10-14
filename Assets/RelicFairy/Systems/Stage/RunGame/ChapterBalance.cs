/// <summary>
/// 챕터별 몬스터 배율 — 체력 · 공격 · 마릿수를 <b>나눠</b> 완만하게(10-02 레벨디자인 설계서 §2 · 사용자 결정 D1).
/// <para>예전엔 <c>ChapterDataSO.difficultyScale</c>(1 / 1.5 / 2.2 / 3.0) 하나가 체력 · 공격을 같이 올렸는데, 그 배율을 읽는
/// 길(<c>GameRunSession.BindChapterRegistry</c>)이 끊겨 Ch1 ~ Ch4 몬스터 수치가 같았다. 레지스트리를 다시 묶으면 챕터 테마까지
/// 방 팔레트를 덮어써서(10-01 D1 ①로 막아 둔 길) 배율만 이 표 한 곳에 둔다 — 시기 배율 <see cref="EraBalance"/>와 같은 결.</para>
/// <para>10-11 체력은 <b>층별</b>(일반방 긴장감 · 엘리트 설계 §3-1 · 결정 D2) — 잡몹(Common)은 끝까지 1~2타가 되게 완만하게,
/// 위협(Rare) · 정예(Elite)는 챕터마다 다시 버거워지게(방 처치 톱니). 공격은 층 구분 없이 하나.</para>
/// 보스는 이 배율에서 빠진다(<c>MonsterBase.ResolveDifficultyScale</c> — 보스 체력은 보스 데이터 · 페이지가 맡는다).
/// 수치는 시작값 — 런 중 DPS 실측(설계 P1-0)과 자동 플레이 3런(방 처치 시간 · 사망 위치)으로 맞춘다.
/// </summary>
public static class ChapterBalance
{
    public readonly struct Entry
    {
        public readonly float CommonHp, RareHp, EliteHp, Attack, Count;
        public Entry(float commonHp, float rareHp, float eliteHp, float attack, float count)
        {
            CommonHp = commonHp; RareHp = rareHp; EliteHp = eliteHp; Attack = attack; Count = count;
        }

        /// <summary>등급(= 층)별 체력 배율. 보스는 여기 오지 않는다(호출자가 뺀다) — 오면 정예 칸.</summary>
        public float HpFor(MonsterGrade grade) => grade switch
        {
            MonsterGrade.Common => CommonHp,
            MonsterGrade.Rare   => RareHp,
            _                   => EliteHp,
        };
    }

    //                                    잡몹 · 위협 · 정예 체력      공격    마릿수
    private static readonly Entry Ch1 = new(1.0f, 1.0f, 1.0f,         1.00f, 1.0f);
    private static readonly Entry Ch2 = new(1.4f, 1.8f, 2.0f,         1.10f, 1.1f);
    private static readonly Entry Ch3 = new(2.0f, 3.0f, 3.5f,         1.20f, 1.2f);
    private static readonly Entry Ch4 = new(2.8f, 4.5f, 5.5f,         1.35f, 1.3f);

    public static Entry Of(ChapterId chapter) => chapter switch
    {
        ChapterId.Chapter2 => Ch2,
        ChapterId.Chapter3 => Ch3,
        ChapterId.Chapter4 => Ch4,
        _                  => Ch1,
    };
}
