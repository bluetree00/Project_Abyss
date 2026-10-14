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
        // 단계 도달 문장(속성 버스트)은 뺐다 — 단계는 룬판(시간 정지)에서 켜져 입자가 멈춘 채 회수돼 거의 안 보였고,
        // 같은 순간 판 터짐 · 「시너지 활성화」 알림이 이미 말한다(10-11 이펙트 톤 2차 D-3).
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
