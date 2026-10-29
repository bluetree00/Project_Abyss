/// <summary>
/// 챕터별 몬스터 배율 — 체력 · 공격 · 마릿수를 <b>나눠</b> 완만하게(10-02 레벨디자인 설계서 §2 · 사용자 결정 D1).
/// <para>예전엔 <c>ChapterDataSO.difficultyScale</c>(1 / 1.5 / 2.2 / 3.0) 하나가 체력 · 공격을 같이 올렸는데, 그 배율을 읽는
/// 길(<c>GameRunSession.BindChapterRegistry</c>)이 끊겨 Ch1 ~ Ch4 몬스터 수치가 같았다. 레지스트리를 다시 묶으면 챕터 테마까지
/// 방 팔레트를 덮어써서(10-01 D1 ①로 막아 둔 길) 배율만 이 표 한 곳에 둔다 — 시기 배율 <see cref="EraBalance"/>와 같은 결.</para>
/// <para>10-11 체력은 <b>층별</b>(일반방 긴장감 · 엘리트 설계 §3-1 · 결정 D2) — 잡몹(Common)은 끝까지 1~2타가 되게 완만하게,
/// 위협(Rare) · 정예(Elite)는 챕터마다 다시 버거워지게(방 처치 톱니). 공격은 챕터 배율 하나에 10-21부터 등급 배율을 곱한다.</para>
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

        /// <summary>등급별 공격 배율 — 챕터 공격 × 전체 배율(10-23) × 등급 배율(10-21). 보스는 여기 오지 않는다(호출자가 뺀다) — 오면 정예 칸.</summary>
        public float AttackFor(MonsterGrade grade) => Attack * AllAttackMul * grade switch
        {
            MonsterGrade.Common => 1f,
            MonsterGrade.Rare   => RareAttackMul,
            _                   => EliteAttackMul,
        };

        /// <summary>등급(= 층)별 체력 배율 × 전체 배율(10-23). 보스는 여기 오지 않는다(호출자가 뺀다) — 오면 정예 칸.</summary>
        public float HpFor(MonsterGrade grade) => AllHpMul * grade switch
        {
            MonsterGrade.Common => CommonHp,
            MonsterGrade.Rare   => RareHp,
            _                   => EliteHp,
        };
    }

    // 10-21 사용자 「몬스터 전체 체력이 때릴 맛이 나게」 — 체력을 플레이어 화력 곡선에 맞춘다(기획/RelicFairy_일반방위협·몬스터개편_20261021.md §1).
    //   중간 빌드 보스 앞 단일 DPS ≈ 165 · 580 · 950 · 1,200(10-15~16 실측)인데 체력은 ×1 → ×2.8이라 Ch2부터 모이면 0.2~0.3초에 녹았다
    //   (공격 확약 · 포이즈 · 두 번째 파도가 쓸 틈이 없었다). 목표 = 챕터 중간 힘에서 잡몹 ≈0.6초 · 위협 ≈1.6초 · 정예 ≈5초.
    //   예전 값: 잡몹 1 · 1.4 · 2.0 · 2.8 / 위협 1 · 1.8 · 3.0 · 4.5 / 정예 1 · 2.0 · 3.5 · 5.5.
    //   10-21 b Ch4 26 · 23 · 26 → 22 · 19.5 · 22 — 1차 실측 Ch4 일반방 44.7초(목표 12~40초).
    //                                    잡몹 · 위협 · 정예 체력      공격    마릿수
    private static readonly Entry Ch1 = new(1.8f,  1.5f,  1.8f,       1.00f, 1.0f);
    private static readonly Entry Ch2 = new(8.0f,  7.0f,  8.0f,       1.10f, 1.1f);
    private static readonly Entry Ch3 = new(18.0f, 16.0f, 18.0f,      1.20f, 1.2f);
    private static readonly Entry Ch4 = new(22.0f, 19.5f, 22.0f,      1.35f, 1.3f);

    // 10-21 b 위협 · 정예 공격 배율 — 1차 실측 정예방 받는 피해 0%(Ch1) · 일반방 4~6%(목표 일반 5~15% · 정예방 15~35%).
    //   잡몹은 그대로(무리로 위협), 챕터 공격 배율에 곱한다.
    private const float RareAttackMul  = 1.2f;
    private const float EliteAttackMul = 1.3f;

    // 10-23 B5 사용자 「몬스터 공격력 전반 올리기 · 체력 조금 더」 — 위 챕터 · 등급 표에 한 번 곱한다(보스 제외 · 중간 보스 황소는 정예 칸이라 포함).
    //   공격은 체감되게, 체력은 조금만(「체력으로 어렵게 하지 않는다」) — 예고(≥ 0.45초)를 보고 피할 수 있는 공격이 더 아프게.
    private const float AllAttackMul = 1.25f;
    private const float AllHpMul     = 1.10f;

    public static Entry Of(ChapterId chapter) => chapter switch
    {
        ChapterId.Chapter2 => Ch2,
        ChapterId.Chapter3 => Ch3,
        ChapterId.Chapter4 => Ch4,
        _                  => Ch1,
    };
}
