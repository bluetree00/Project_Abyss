using UnityEngine;

/// <summary>
/// 아이템 효과 인터페이스.
/// 각 effectType마다 이 인터페이스를 구현하는 클래스를 만들고,
/// ItemEffectRegistry에 등록하면 자동으로 동작.
///
/// 모든 메서드는 선택 구현 — ItemEffectBase에서 빈 virtual로 제공.
/// </summary>
public interface IItemEffect
{
    // ── 식별 ────────────────────────────────────────────────
    string EffectType { get; }
    string Trigger { get; }

    // ── 생명주기 ────────────────────────────────────────────
    void OnActivate(ItemEffectContext ctx);
    void OnDeactivate();

    /// <summary>현재 조건에서 이 효과가 활성인지 (원소/HP/장비 조건).</summary>
    bool IsActive(ItemEffectContext ctx);

    // ── 스탯 수정 (매 재계산 시) ────────────────────────────
    void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats);

    // ── 전투: 공격 ──────────────────────────────────────────
    /// <summary>데미지 나가기 전 수정 (버프 배율, 추가 타격 등).</summary>
    void OnPreDealDamage(ItemEffectContext ctx, ref DamagePacket pkt);
    /// <summary>적중 확정 후 (흡혈, 독, 빙결 등).</summary>
    void OnPostDealDamage(ItemEffectContext ctx, DamageReport report);
    /// <summary>적 처치 시.</summary>
    void OnKill(ItemEffectContext ctx, GameObject target);

    // ── 전투: 피격 ──────────────────────────────────────────
    /// <summary>피격 전 수정 (무효화, 감소).</summary>
    void OnPreTakeDamage(ItemEffectContext ctx, ref DamagePacket pkt);
    /// <summary>피격 후 (반사, 임시 방버프).</summary>
    void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report);
    /// <summary>사망 직전. true 반환 시 생존 (부활/무적).</summary>
    bool OnNearDeath(ItemEffectContext ctx, ref float healPercent, ref float invincibleDuration);

    // ── 이동/회피 ───────────────────────────────────────────
    void OnRollEnd(ItemEffectContext ctx);
    void OnRollLand(ItemEffectContext ctx, Vector3 position);
    void OnJumpLand(ItemEffectContext ctx, Vector3 position);

    // ── 진행 ────────────────────────────────────────────────
    void OnRoomClear(ItemEffectContext ctx);
    void OnBossClear(ItemEffectContext ctx);
    void OnRecipeComplete(ItemEffectContext ctx);
    void OnItemPickup(ItemEffectContext ctx, RuntimeItemData pickedItem);

    // ── 스킬 ────────────────────────────────────────────────
    void OnSkillUse(ItemEffectContext ctx, SkillType skill);

    // ── 치유 ────────────────────────────────────────────────
    void ModifyHeal(ItemEffectContext ctx, ref int amount);

    // ── 틱 ──────────────────────────────────────────────────
    void OnTick(ItemEffectContext ctx, float deltaTime);
}
