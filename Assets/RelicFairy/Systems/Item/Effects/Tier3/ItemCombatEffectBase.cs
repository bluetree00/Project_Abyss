using UnityEngine;

/// <summary>
/// T3/T4 전투 트리거 아이템 효과 공용 베이스.
/// OnHit/OnKill/OnTick 등 이벤트 훅에서 동작하므로 IsActive=true로 고정(상시 수신).
/// 정적 스탯(ModifyStats)에는 기여하지 않는다.
/// </summary>
public abstract class ItemCombatEffectBase : ItemEffectBase
{
    protected ItemCombatEffectBase(ItemEffectSlot s) : base(s) { }

    public override bool IsActive(ItemEffectContext ctx) => true;

    /// <summary>현재 무기 타입 기준 유효 공격력(근접/원거리).</summary>
    protected static int EffAtk(ItemEffectContext ctx)
    {
        if (ctx?.Stats == null) return 0;
        return ctx.Stats.GetEffectiveAttack(ctx.WeaponType.GetAttackStatKind());
    }

    /// <summary>시전자 GameObject(없으면 null).</summary>
    protected static GameObject Self(ItemEffectContext ctx) => ctx?.Player != null ? ctx.Player.gameObject : null;

    /// <summary>다음 일반공격 1타 강화 예약(광폭형 공용). bonus 0.4 = +40%.</summary>
    protected static void QueueNext(float bonus)
        => GameRunBootstrapper.Instance?.Run?.EffectManager?.QueueNextAttackBonus(bonus);
}
