using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 플라잉데몬 전용 추격 상태 오버라이드 SO.
/// 기본 직진 ChaseState 를 선회 추격 버전(OrbitalChaseState)으로 교체한다.
///
/// 선회 행동:
///   • 플레이어와의 거리가 orbitalRadius * 1.5 이내이면 선회 모드
///   • 그 외 → 일반 직진 추격
///   • 공격 사거리 1.1배 이내이면 AttackReadyState 로 전환
///   • 추격 포기 거리 초과 시 PatrolState 로 전환
/// </summary>
[CreateAssetMenu(fileName = "FlyingDemonOverride", menuName = "Lee/Monster/Override/FlyingDemonOverride")]
public class FlyingDemonOverrideSO : MonsterStateOverrideSO
{
    [Tooltip("선회 반경 (m)")]
    public float orbitalRadius = 3.5f;

    [Tooltip("선회 속도 (deg/s)")]
    public float orbitSpeed = 120f;

    public override void RegisterOverrides(MonsterFSM fsm, MonsterBase monster)
    {
        fsm.RegisterAs<ChaseState>(new OrbitalChaseState(monster, this));
    }

    // ── 선회 추격 상태 (내부 클래스) ──────────────────────────────────
    private class OrbitalChaseState : ChaseState
    {
        private readonly FlyingDemonOverrideSO _data;
        private readonly MonsterBase           _owner;
        private float _orbitAngle;

        public OrbitalChaseState(MonsterBase owner, FlyingDemonOverrideSO data)
        {
            _owner = owner;
            _data  = data;
        }

        public override void Update(MonsterContext ctx)
        {
            if (ctx.Runtime.PlayerTarget == null) { base.Update(ctx); return; }

            float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);

            // 공격 사거리 이내 → 공격 준비
            if (dist <= ctx.Config.stat.attackRange * 1.1f)
            {
                ctx.Monster.ChangeState<AttackReadyState>();
                return;
            }

            // 추격 포기 거리 초과 → 순찰
            if (dist > ctx.Config.detection.chaseGiveUpRange)
            {
                ctx.Monster.ChangeState<PatrolState>();
                return;
            }

            // 선회 반경 1.5배 초과 → 일반 직진 추격
            if (dist > _data.orbitalRadius * 1.5f)
            {
                base.Update(ctx);
                return;
            }

            // 선회 이동
            _orbitAngle += _data.orbitSpeed * Time.deltaTime;
            float   rad    = _orbitAngle * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * _data.orbitalRadius;
            Vector3 target = ctx.Runtime.PlayerTarget.position + offset;
            ctx.Agent.SetDestination(target);

            // 플레이어 방향 회전
            Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                ctx.Transform.rotation = Quaternion.Slerp(
                    ctx.Transform.rotation,
                    Quaternion.LookRotation(dir),
                    Time.deltaTime * 8f);
        }
    }
}
