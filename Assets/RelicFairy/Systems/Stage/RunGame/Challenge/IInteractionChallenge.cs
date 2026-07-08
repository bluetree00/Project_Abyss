using System;

/// <summary>
/// 비전투 이벤트방 상호작용 챌린지 공통 계약. 해결 시 OnResolved 발행 →
/// RunFlowController가 방 클리어 처리(출구 게이트 공개)를 이 시점까지 지연한다.
/// 미해결 상태로 방을 떠날 수 없게(=챌린지 필수) 하는 훅.
/// </summary>
public interface IInteractionChallenge
{
    event Action OnResolved;
}
