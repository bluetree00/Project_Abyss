/// <summary>
/// 챕터별 몬스터 배율 — 체력 · 공격 · 마릿수를 <b>나눠</b> 완만하게(10-02 레벨디자인 설계서 §2 · 사용자 결정 D1).
/// <para>예전엔 <c>ChapterDataSO.difficultyScale</c>(1 / 1.5 / 2.2 / 3.0) 하나가 체력 · 공격을 같이 올렸는데, 그 배율을 읽는
/// 길(<c>GameRunSession.BindChapterRegistry</c>)이 끊겨 Ch1 ~ Ch4 몬스터 수치가 같았다. 레지스트리를 다시 묶으면 챕터 테마까지
/// 방 팔레트를 덮어써서(10-01 D1 ①로 막아 둔 길) 배율만 이 표 한 곳에 둔다 — 시기 배율 <see cref="EraBalance"/>와 같은 결.</para>
/// 보스는 이 배율에서 빠진다(<c>MonsterBase.ResolveDifficultyScale</c> — 보스 체력은 보스 데이터 · 페이지가 맡는다).
/// 수치는 시작값 — 자동 플레이 3런(방 처치 시간 · 사망 위치)으로 맞춘다.
/// </summary>
public static class ChapterBalance
{
    public readonly struct Entry
    {
        public readonly float Hp, Attack, Count;
        public Entry(float hp, float attack, float count) { Hp = hp; Attack = attack; Count = count; }
    }

    private static readonly Entry Ch1 = new(1.0f, 1.00f, 1.0f);
    private static readonly Entry Ch2 = new(1.3f, 1.05f, 1.1f);
    private static readonly Entry Ch3 = new(1.7f, 1.15f, 1.2f);
    private static readonly Entry Ch4 = new(2.2f, 1.25f, 1.3f);

    public static Entry Of(ChapterId chapter) => chapter switch
    {
        ChapterId.Chapter2 => Ch2,
        ChapterId.Chapter3 => Ch3,
        ChapterId.Chapter4 => Ch4,
        _                  => Ch1,
    };
}
