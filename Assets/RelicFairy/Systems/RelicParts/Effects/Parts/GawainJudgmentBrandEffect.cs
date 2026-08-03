using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// gawain_judgment_brand(심판의 낙인) — 화상 쌓인 적을 벨 때 체력 20% 이하면 즉시 처형한다.
/// 보스는 처형되지 않고 화상이 1중첩(잔여시간 연장)만 추가된다.
///
/// OnHit에서 대상이 화상 상태일 때만 발동. 처형은 현재 체력의 초과피해(오버킬)로 즉사 처리한다.
/// </summary>
public sealed class GawainJudgmentBrandEffect : RelicPartEffect
{
    private const float ExecuteThreshold = 0.20f;   // 체력 비율 이하 처형
    private const float BossBurnExtend   = 2f;       // 보스 화상 연장(초) = 1중첩 근사

    public GawainJudgmentBrandEffect() : base("gawain_judgment_brand") { }

    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        var target = hit.Target;
        if (target == null) return;

        // "화상 쌓인 적" — 화상 핸들러가 붙어 잔여가 남은 대상만.
        if (!target.TryGetComponent<MonsterBurnHandler>(out var burn) || burn.Remaining <= 0f) return;

        var mb = target.GetComponentInParent<MonsterBase>();
        if (mb == null || mb.IsDead) return;

        if (mb.Grade == MonsterGrade.Boss)
        {
            MonsterBurnHandler.ExtendOn(target, BossBurnExtend);   // 보스: 처형 대신 화상 연장
        }
        else if (mb.EffectiveMaxHp > 0 && (float)mb.CurrentHp / mb.EffectiveMaxHp <= ExecuteThreshold)
        {
            ElementVfxPlayer.PlayBurst(RuneElement.Fire, mb.transform.position, 1.5f);   // 처형 화염 섬광
            mb.TakeDamage(mb.CurrentHp * 10f, player.gameObject, 0.3f);                  // 처형(오버킬)
        }
    }
}
