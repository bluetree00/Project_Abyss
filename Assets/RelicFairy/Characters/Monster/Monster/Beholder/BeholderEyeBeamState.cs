using RelicFairy.Monster;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 비홀더 눈 빔 특수 상태.
/// HP 60% 이하일 때 반복 발동 — Charge → Fire 두 단계로 구성.
/// Charge: 차지 애니 재생 후 Fire 단계로 전환.
/// Fire: 틱 단위로 범위 데미지, 종료 후 AttackReadyState 복귀.
/// MovementLockedState 포맷: 이동만 잠금, 방해 가능.
/// </summary>
public class BeholderEyeBeamState : MovementLockedState<BeholderEyeBeamData>
{
    private enum Phase { Charge, Fire }

    private Phase _phase;
    private float _chargeTimer;
    private float _beamTimer;
    private float _tickTimer;
    private GameObject _beamVfxInstance;

    public BeholderEyeBeamState(BeholderEyeBeamData data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        ctx.Agent.ResetPath();
        ctx.Agent.velocity = Vector3.zero;

        _phase       = Phase.Charge;
        _chargeTimer = Data.chargeDuration;
        _tickTimer   = 0f;

        if (ctx.Animator != null && !string.IsNullOrEmpty(Data.chargeStateName))
            ctx.Animator.CrossFade(Data.chargeStateName, 0.1f);

        // 예고 — 판정과 같은 원(자기 둘레 beamRadius) 외곽선 + 머리 위 반짝임. 예전엔 바닥 예고 없이 원 안을 태웠다(10-11 P1-7).
        MonsterGroundWarning.SpawnCircleOutline(ctx.Transform.position, Data.beamRadius, Data.chargeDuration, PatternAttackOverrideSO.WarningOutlineColor);
        ctx.Monster.NotifyAttackIncoming(Data.chargeDuration);
    }

    public override void Update(MonsterContext ctx)
    {
        switch (_phase)
        {
            case Phase.Charge:
                _chargeTimer -= Time.deltaTime;
                ctx.Monster.NotifyAttackIncoming(_chargeTimer);
                if (_chargeTimer <= 0f)
                {
                    // 발사 동안 원이 진하게 남는다 — 판정이 이어지는 동안 「여기가 위험」을 계속 알린다
                    MonsterGroundWarning.SpawnCircleOutline(ctx.Transform.position, Data.beamRadius, Data.beamDuration, PatternAttackOverrideSO.WarningOutlineColor, hold: true);
                    ctx.Monster.ResetAttackGlint();
                    _phase     = Phase.Fire;
                    _beamTimer = Data.beamDuration;
                    _tickTimer = 0f;
                    if (ctx.Animator != null && !string.IsNullOrEmpty(Data.beamStateName))
                        ctx.Animator.CrossFade(Data.beamStateName, 0.1f);
                    if (Data.beamVfxPrefab != null)
                    {
                        // 판정 원 그대로 — 바닥 · 월드 배율(몸에 붙이면 몸 배율이 곱해져 크기가 어긋난다 · 10-15 정예 점검)
                        _beamVfxInstance = Object.Instantiate(Data.beamVfxPrefab, ctx.Transform.position + Vector3.up * 0.05f, Quaternion.identity);
                        _beamVfxInstance.transform.localScale = Vector3.one * Data.beamVfxScale;
                        foreach (var ps in _beamVfxInstance.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            var main = ps.main;
                            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                        }
                    }
                }
                break;

            case Phase.Fire:
                _tickTimer  -= Time.deltaTime;
                _beamTimer  -= Time.deltaTime;

                if (_tickTimer <= 0f)
                {
                    _tickTimer = Data.damageTick;
                    ApplyAreaDamage(ctx, Data.beamRadius, Data.beamDamage);
                }

                if (_beamTimer <= 0f)
                {
                    if (_beamVfxInstance != null)
                    {
                        Object.Destroy(_beamVfxInstance, 0.5f);
                        _beamVfxInstance = null;
                    }
                    ctx.Monster.ChangeState<AttackReadyState>();
                }
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        if (_beamVfxInstance != null) { Object.Destroy(_beamVfxInstance, 0.3f); _beamVfxInstance = null; }   // 발사 중에 끊겼다(휘청 · 사망)
        ctx.Monster.ResetAttackGlint();
    }

    private static void ApplyAreaDamage(MonsterContext ctx, float radius, float damage)
    {
        // 플레이어만 맞는다 — 예전엔 원 안의 다른 몬스터도 맞았고(아군 피해), 공격자 없이 피해가 들어가 출처가 남지 않았다(10-11 P1-7).
        // 플레이어 콜라이더가 여럿이어도 틱마다 한 번.
        foreach (var col in Physics.OverlapSphere(ctx.Transform.position, radius))
        {
            if (col.transform.IsChildOf(ctx.Transform)) continue;
            var player = col.GetComponent<PlayerController>()
                      ?? col.GetComponentInParent<PlayerController>();
            if (player == null) continue;
            player.TakeDamage((int)damage, ctx.Monster.gameObject, false, HitWeight.Light);   // 틱 — 장판처럼 약(저스트 회피 · 연출 묶음 규칙)
            Vector3 knockDir = (player.transform.position - ctx.Transform.position).normalized;
            knockDir.y = 0.2f;
            player.ApplyKnockback(knockDir.normalized * ctx.Stat.knockbackForce);
            return;
        }
    }
}
