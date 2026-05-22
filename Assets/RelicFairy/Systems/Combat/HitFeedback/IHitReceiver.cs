/// <summary>
/// 피격자 로컬 피드백 수신 계약.
/// HitFeedbackService.RaiseHit 경로에서 target 오브젝트에 이 인터페이스가 구현되어 있으면
/// 전역 OnHit 이벤트와 별개로 직접 호출된다 — Broadcast 오버헤드 없이 타겟 전용 전달.
/// </summary>
public interface IHitReceiver
{
    void OnReceiveHit(in HitInfo info);
}
