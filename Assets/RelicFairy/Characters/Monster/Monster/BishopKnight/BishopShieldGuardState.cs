using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 비숍나이트 방패 방어 + 반격 특수 상태.
/// Guard 페이즈: guardDuration 동안 제자리에서 방어 자세 유지.
/// Counter 페이즈: 반격 애니메이션 재생과 동시에 범위 데미지 적용 후 ChaseState 복귀.
/// MovementLockedState 포맷: 이동 잠금, 피격 반응은 허용.
/// </summary>
public class BishopShieldGuardState : MovementLockedState<BishopShieldData>
{
    private enum Phase { Guard, Counter }

    private const float CounterWindup = 0.34f;   // 방패 치기(Attack02) 정점 — 막기 끝(예고 앞줄이 닿는 순간)에 정점이 오게 미리 든다(10-14 정예 애니 점검)

    private Phase _phase;
    private float _timer;
    private bool  _swung;
    private MonsterTelegraph _telegraph;   // 반격 원 예고(10-13 — 막기가 끊기면 지운다)

    public BishopShieldGuardState(BishopShieldData data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        ctx.Agent.ResetPath();
        _phase = Phase.Guard;
        _timer = Data.guardDuration;
        _swung = false;
        ctx.Animator?.CrossFade(Data.guardStateName, 0.1f);
        Data.SpawnVFX(ctx.Transform);
        // 막기가 끝나는 순간 3 m 반격 — 앞줄이 그때 끝에 닿는다(10-13 이펙트 = 판정 · 전수조사 P3)
        _telegraph = MonsterTelegraph.Circle(ctx.Transform.position, Data.counterRadius, Mathf.Max(0.05f, Data.guardDuration), MonsterTelegraph.Normal);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer -= Time.deltaTime;

        switch (_phase)
        {
            case Phase.Guard:
                if (!_swung && _timer <= CounterWindup)
                {
                    _swung = true;
                    ctx.Animator?.CrossFade(Data.counterStateName, 0.1f);
                }
                if (_timer <= 0f)
                {
                    _phase = Phase.Counter;
                    _timer = 0.6f;
                    ApplyAreaDamage(ctx, Data.counterRadius, Data.counterDamage);
                    _telegraph = null;   // 맞는 순간까지 그렸다
                }
                break;

            case Phase.Counter:
                if (_timer <= 0f)
                    ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        if (_telegraph != null) _telegraph.Cancel();   // 반격 전에 끊겼다 — 헛예고를 지운다
        _telegraph = null;
    }

    /// <summary>
    /// 반격 — 원 안의 플레이어에게 피해 + 밀치기. 예전엔 플레이어는 밀치기만 받고 피해는 원 안의 다른 몬스터에게 들어갔다
    /// (3 m 원 예고와 어긋남 · 10-14 정예 애니 점검). 피해는 설정값을 기본 공격력 기준으로 챕터 · 시기 배율에 맞춘다.
    /// </summary>
    private static void ApplyAreaDamage(MonsterContext ctx, float radius, float damage)
    {
        foreach (var col in Physics.OverlapSphere(ctx.Transform.position, radius))
        {
            if (col.transform.IsChildOf(ctx.Transform)) continue;
            var player = col.GetComponent<PlayerController>()
                      ?? col.GetComponentInParent<PlayerController>();
            if (player == null) continue;
            Debug.Log($"[BishopShield] 반격 판정 — 플레이어 명중 t={Time.time:F2}");
            float scale = ctx.Stat.attackPower > 0f ? ctx.Monster.EffectiveAttackPower / ctx.Stat.attackPower : 1f;
            player.TakeDamage(Mathf.Max(1, Mathf.RoundToInt(damage * scale * ctx.Monster.AttackMultiplierNow)), ctx.Monster.gameObject, false, HitWeight.Medium);
            player.ApplyKnockback(
                (player.transform.position - ctx.Transform.position).normalized * 3f, 0.3f);
            return;   // 플레이어 몸에 콜라이더가 여럿이어도 한 번
        }
    }
}
