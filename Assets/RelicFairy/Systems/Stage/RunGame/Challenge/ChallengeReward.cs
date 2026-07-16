/// <summary>
/// ChallengeGrade → 보상 페이로드(ChallengeRewardTable.For 결과). RoomClearGate 보상 스케일에 사용.
/// 혼합 보상: 아이템(baseRarity×rewardCount) + 연료(fuelKind×fuelAmount) + 즉시가치(includeInstantValue).
/// </summary>
public struct ChallengeReward
{
    public ItemRarity baseRarity;      // 아이템 보상 최소 등급
    public int        rewardCount;     // 아이템 보상 개수
    public FuelKind   fuelKind;        // 연료 종류
    public int        fuelAmount;      // 연료 지급량
    public bool       includeInstantValue; // 즉시가치(회복 등) 포함 여부
}
