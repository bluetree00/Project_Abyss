using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// BlackKnight 전용 추격 상태 SO.
/// 기본 직진 ChaseState 를 선회 추격 버전(OrbitalChaseState)으로 교체한다.
///
/// 소울류 보스 "기회를 노리는" 행동:
///   • 패턴 브레이크 쿨다운이 breakThreshold 이상 남아있고
///     거리가 minOrbitDist ~ maxOrbitDist 사이이면 → 선회 모드
///   • 그 외 → 일반 직진 추격
///   • 선회 방향은 switchTimerMin~Max 초 마다 무작위 전환
///   • ChaseSpeedMult(HP 이정표 가속)가 선회/추격 모두에 반영됨
/// </summary>
[CreateAssetMenu(fileName = "BKChaseState", menuName = "Abyss/Boss/BlackKnight/ChaseState")]
public class BKChaseStateSO : MonsterStateOverrideSO
{
    [Header("선회 진입 조건")]
    [Tooltip("남은 패턴 브레이크 쿨다운이 이 값 이상일 때 선회 모드 진입")]
    public float breakThreshold = 1.2f;
    [Tooltip("선회 모드 진입 최소 거리 (m)")]
    public float minOrbitDist   = 3.5f;
    [Tooltip("선회 모드 진입 최대 거리 (m)")]
    public float maxOrbitDist   = 8f;

    [Header("선회 파라미터")]
    [Tooltip("목표 선회 반경 (m) — 이 거리를 유지하려 이동")]
    public float targetOrbitDist = 5.5f;
    [Tooltip("선회 속도 배율 (moveSpeed 대비)")]
    public float orbitSpeedMult  = 0.65f;
    [Tooltip("선회 방향 전환 간격 최솟값 (초)")]
    public float switchTimerMin  = 1.8f;
    [Tooltip("선회 방향 전환 간격 최댓값 (초)")]
    public float switchTimerMax  = 3.2f;

    // ─────────────────────────────────────────────────────────────
    public override void RegisterOverrides(MonsterFSM fsm, MonsterBase monster)
    {
        if (monster is BlackKnightBoss bk)
            fsm.RegisterAs<ChaseState>(new OrbitalChaseState(bk, this));
        else
            Debug.LogWarning("[BKChaseStateSO] BlackKnightBoss 가 아닌 몬스터에 사용됨. 기본 ChaseState 를 그대로 사용.", this);
    }

    // ── 선회 추격 상태 (내부 클래스) ──────────────────────────────
    private sealed class OrbitalChaseState : ChaseState
    {
        private readonly BlackKnightBoss _bk;
        private readonly BKChaseStateSO  _so;
        private float _orbitDir    = 1f;
        private float _switchTimer = 0f;

        public OrbitalChaseState(BlackKnightBoss bk, BKChaseStateSO so)
        {
            _bk = bk;
            _so = so;
        }

        public override void Update(MonsterContext ctx)
        {
            if (ctx.Runtime.PlayerTarget == null) { base.Update(ctx); return; }

            float dist = Vector3.Distance(ctx.Transform.position,
                                          ctx.Runtime.PlayerTarget.position);
            bool shouldOrbit = _bk.PatternBreakCooldown > _so.breakThreshold
                            && dist >= _so.minOrbitDist
                            && dist <= _so.maxOrbitDist;

            if (shouldOrbit) Orbit(ctx, dist);
            else             Chase(ctx);
        }

        // ── 선회 ──────────────────────────────────────────────────
        private void Orbit(MonsterContext ctx, float curDist)
        {
            _switchTimer -= Time.deltaTime;
            if (_switchTimer <= 0f)
            {
                _orbitDir    = Random.value > 0.5f ? 1f : -1f;
                _switchTimer = Random.Range(_so.switchTimerMin, _so.switchTimerMax);
            }

            Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
            toPlayer.y = 0f;
            Vector3 norm = toPlayer.normalized;

            Vector3 perp        = new Vector3(-norm.z, 0f, norm.x) * _orbitDir;
            float   distCorrect = (curDist - _so.targetOrbitDist) * 0.35f;
            Vector3 target      = ctx.Transform.position + perp * 3f + norm * distCorrect;

            ctx.Agent.SetDestination(target);
            ctx.Agent.speed = ctx.Config.stat.moveSpeed * _so.orbitSpeedMult
                              * _bk.Blackboard.ChaseSpeedMult;

            if (norm.sqrMagnitude > 0.001f)
                ctx.Transform.rotation = Quaternion.Slerp(
                    ctx.Transform.rotation,
                    Quaternion.LookRotation(norm),
                    Time.deltaTime * 6f);
        }

        // ── 직진 추격 (ChaseSpeedMult 반영) ──────────────────────
        private void Chase(MonsterContext ctx)
        {
            ctx.Agent.speed = ctx.Config.stat.moveSpeed * _bk.Blackboard.ChaseSpeedMult;
            base.Update(ctx);
        }
    }
}
}
