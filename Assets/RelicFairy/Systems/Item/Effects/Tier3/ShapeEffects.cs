using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════
// 형태변형 T3/T4 아이템 전투 효과 (설계 §6)
//
// 전부 ContributeCombatMods로 "공격 판정 변형" 스냅샷에 기여한다.
// 실제 콜라이더 스케일/오버랩/다단/투사체 적용은 ColliderInstance(P5)가 스냅샷을 읽어 수행.
// (분산의 화살통 ProjectileCount / 관통하는 창 ProjectilePierce 는 기존 스탯 효과 — 데이터만.)
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>이중 타격의 장갑 / 메아리 치는 일격 — 근접 1스윙당 추가 타격. value=추가 횟수, value2=각 타격 피해비.</summary>
public sealed class MeleeMultiHitEffect : ItemCombatEffectBase
{
    public MeleeMultiHitEffect(ItemEffectSlot s) : base(s) { }

    public override void ContributeCombatMods(ItemEffectContext ctx, ref ItemCombatModifiers mods)
    {
        mods.meleeExtraHits     += _value > 0f ? (int)_value : 1;
        mods.meleeExtraHitRatio  = Mathf.Max(mods.meleeExtraHitRatio, _value2 > 0f ? _value2 : 0.8f);
    }
}

/// <summary>확장된 칼끝 — 근접 사거리 +value(콜라이더 스케일).</summary>
public sealed class MeleeRangeExtendEffect : ItemCombatEffectBase
{
    public MeleeRangeExtendEffect(ItemEffectSlot s) : base(s) { }

    public override void ContributeCombatMods(ItemEffectContext ctx, ref ItemCombatModifiers mods)
        => mods.meleeRangeMult += _value > 0f ? _value : 0.2f;
}

/// <summary>두 줄기 빛 / 관통하는 빛살(스킬분) — 스킬 추가 투사체. value=추가 수.</summary>
public sealed class SkillProjectileCountEffect : ItemCombatEffectBase
{
    public SkillProjectileCountEffect(ItemEffectSlot s) : base(s) { }

    public override void ContributeCombatMods(ItemEffectContext ctx, ref ItemCombatModifiers mods)
        => mods.skillExtraProjectiles += _value > 0f ? (int)_value : 1;
}

/// <summary>원형 충격파 / 전방위 칼날 — 부채꼴→원형(스윙마다 원형 오버랩 추가). value2=반경, value=피해비(추가타 비율).</summary>
public sealed class MeleeShapeCircleEffect : ItemCombatEffectBase
{
    public MeleeShapeCircleEffect(ItemEffectSlot s) : base(s) { }

    public override void ContributeCombatMods(ItemEffectContext ctx, ref ItemCombatModifiers mods)
    {
        mods.meleeCircle = true;
        mods.meleeCircleRadius = Mathf.Max(mods.meleeCircleRadius, _value2 > 0f ? _value2 : 3f);
        // 원형 추가타 비율은 다단 비율 채널을 공유(없으면 0.8)
        if (mods.meleeExtraHitRatio <= 0f) mods.meleeExtraHitRatio = _value > 0f ? _value : 0.8f;
    }
}
