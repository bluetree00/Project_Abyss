/// <summary>
/// Lee FSM 몬스터 전용 범용 애니메이션 이벤트 수신자.
///
/// BatAnimationEventReceiver 대신 사용한다.
/// Animation Clip에 등록된 이벤트 함수명이 아래 public 메서드와 일치해야 한다.
///   - OnAttackStart
///   - OnAttackEnd
/// </summary>
public class LeeAnimationEventReceiver : MonsterAnimationEventReceiver
{
    public override void OnAttackStart()
    {
        if (controller == null) return;

        controller.SetAttack(true);

        if (TryGetAttackEffect(out var effectName, out var offset, out var rotationEuler))
        {
            var spawnPos = controller.transform.TransformPoint(offset);
            var spawnRot = UnityEngine.Quaternion.Euler(rotationEuler);
            SpawnAttackEffect(effectName, spawnPos, spawnRot);
        }

        RaiseAttackStartEvent();
    }

    public override void OnAttackEnd()
    {
        if (controller == null) return;

        controller.SetAttack(false);
        RaiseAttackEndEvent();
    }
}
