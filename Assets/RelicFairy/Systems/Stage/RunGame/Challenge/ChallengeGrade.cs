/// <summary>
/// 이벤트방 챌린지 성과 등급(바닥~천장). 전투 오버레이/상호작용 결과가 이 등급으로 수렴한다.
/// 벨류 원칙: Fail도 0/파괴가 아니라 하향된 보상(연료 소량+즉시가치) — 좌절 완화.
/// </summary>
public enum ChallengeGrade
{
    Fail = 0,
    Bronze,
    Silver,
    Gold,
    Platinum,
}
