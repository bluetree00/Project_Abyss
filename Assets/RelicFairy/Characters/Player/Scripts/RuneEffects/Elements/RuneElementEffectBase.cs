using UnityEngine;

/// <summary>
/// 6속성 룬 효과 공용 베이스 — 플레이어 캐시 + 유효 공격력/리소스 헬퍼.
/// 모든 속성(전기/불/얼음/풀/빛/어둠) 효과 클래스가 이 베이스를 상속한다.
/// </summary>
public abstract class RuneElementEffectBase : RuneEffect
{
    protected PlayerController CachedPlayer;

    public override void OnActivate(PlayerController player)
    {
        CachedPlayer = player;
        // [실제 VFX] 단계 도달 시 속성 버스트 1회 — 6속성 자기연출(빛/전기/어둠 자기강화 포함).
        if (player != null && RuneElementMap.FromEffectType(EffectType, out var element))
            ElementVfxPlayer.PlayBurst(element, player.transform.position);
    }

    /// <summary>플레이어 공유 리소스 컨테이너(스택/게이지/레지스터).</summary>
    protected static RuneResourceState Res(PlayerController player) => player?.RuneEffects?.Resources;

    /// <summary>현재 무기 타입 기준 유효 공격력(근접/원거리 분기).</summary>
    protected static int GetEffectiveAttack(PlayerController player)
    {
        var stats = player?.RuntimeStats;
        if (stats == null) return 0;
        var wd = player.WeaponManager?.CurrentWeaponData;
        AttackStatKind kind = wd != null ? wd.weaponType.GetAttackStatKind() : AttackStatKind.Melee;
        return stats.GetEffectiveAttack(kind);
    }
}
