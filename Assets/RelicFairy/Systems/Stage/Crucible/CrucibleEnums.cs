// CrucibleEnums.cs

/// <summary>재련소 방 진입 시 결정적으로 롤되는 돌발 이벤트.</summary>
public enum CrucibleEvent
{
    None,      // 평상
    Discount,  // 반값 재련(재료비 감소)
    Fever,     // 열기 오른 화로(성공률 상승)
}

/// <summary>재련공 NPC 대사 상황(도발/부추김/축하/위로/성공/평상).</summary>
public enum CrucibleMood
{
    Idle,
    Taunt,
    Streak,
    Jackpot,
    Fail,
    Success,
}
